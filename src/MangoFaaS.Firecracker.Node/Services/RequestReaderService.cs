using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using MangoFaaS.Common;
using MangoFaaS.Common.Services;
using MangoFaaS.Firecracker.API;
using MangoFaaS.Firecracker.Node.Pooling;
using MangoFaaS.Models;
using Microsoft.Extensions.Options;
using Minio;
using MangoFaaS.Firecracker.Node.Models;
using MangoFaaS.Firecracker.Node.Store;
using MangoFaaS.Common.Helpers;
using Kafka.OffsetManagement;

namespace MangoFaaS.Firecracker.Node.Services;

public class RequestReaderService(
    ILogger<RequestReaderService> logger,
    IOptions<FirecrackerPoolOptions> firecrackerOptions,
    IConsumer<string, Invocation> consumer,
    IProducer<string, InvocationResponse> producer,
    IFirecrackerProcessPool pool,
    Instrumentation instrumentation,
    IImageDownloadService imageDownloadService,
    PendingRequestStore pendingRequestStore) : BackgroundService
{
    // Global + per-partition concurrency limits (TODO: make configurable via options)
    private const int MaxPerPartition = 8;

    private readonly ConcurrentDictionary<TopicPartition, KafkaOffsetManager> _offsetManagers = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() =>
        {

            consumer.Subscribe("requests");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var (topicPartition, offsetManager) in _offsetManagers)
                        {
                            var maybeCommitOffset = offsetManager.GetCommitOffset();
                            if (maybeCommitOffset.HasValue)
                                consumer.Commit([new TopicPartitionOffset(topicPartition, new Offset(maybeCommitOffset.Value))]);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to commit topic partition offsets");
                    }


                    ConsumeResult<string, Invocation>? cr = null;
                    try
                    {
                        cr = consumer.Consume(TimeSpan.FromMilliseconds(100)); // short poll to keep heartbeats flowing
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Consume error");
                        continue;
                    }

                    if (cr?.Message == null) continue;

                    Dispatch(cr, stoppingToken);
                }
            }
            finally
            {
                try { consumer.Close(); } catch { /* ignore */ }
            }
        }, stoppingToken);
    }

    private void Dispatch(ConsumeResult<string, Invocation> cr, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var tp = cr.TopicPartition;
            var offsetManager = _offsetManagers.GetOrAdd(tp, (_) => new KafkaOffsetManager(MaxPerPartition));

            var offsetVal = cr.Offset.Value;
            try
            {
                var ackId = await offsetManager.GetAckIdAsync(offsetVal);
                await HandleRequestAsync(cr, ct).ConfigureAwait(false);
                offsetManager.Ack(ackId);
            }
            catch (OperationCanceledException)
            {
                // shutting down; do not mark completed (will reprocess on restart)
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processing failed for {TPO}", cr.TopicPartitionOffset);
                // leave gap -> earlier offsets block commit window (at-least-once semantics)
            }
        }, CancellationToken.None);
    }

    private async Task HandleRequestAsync(ConsumeResult<string, Invocation> cr, CancellationToken ct)
    {
        var correlationId = GetHeader(cr.Message.Headers, "correlationId") ?? throw new InvalidOperationException("No correlationId header in request");
        var replyTo = GetHeader(cr.Message.Headers, "replyTo") ?? throw new InvalidOperationException("No replyTo header in request");
        var httpRequest = cr.Message.Value;
        var functionId = $"{cr.Message.Value.FunctionId}:{cr.Message.Value.FunctionVersion}";

        using var activity = instrumentation.StartActivity($"Handling {correlationId}");
        await using var lease = await pool.AcquireAsync(functionId ?? throw new InvalidOperationException("Non-enriched request received at Node"), ct);
        var p = lease.Process;

        // We can short-circuit the initalization if VM is already initialized for this function
        if (!lease.IsWarm)
        {
            activity?.AddEvent(new("Starting Cold VM"));

            var client = lease.CreateClient();

            logger.LogInformation("Sending request to Firecracker API socket at {SocketPath}", lease.ApiSocketPath);
            logger.LogInformation("Handling {correlationId} using firecracker PID {processId}", correlationId, p.Id);
            try
            {
                await ConfigureDiskAsync(client, httpRequest, lease, ct);
                activity?.AddEvent(new("Disk Set."));
                await StartVm(client, ct);
                activity?.AddEvent(new("VM Started."));
            }
            catch (Exception)
            {
                await lease.MarkAsUnusable();
                activity?.SetStatus(ActivityStatusCode.Error, "Failed to initialize VM for request");
                throw;
            }
        }
        else
        {
            activity?.AddEvent(new("Reusing warm VM"));
        }

        var responseTcs = new TaskCompletionSource<InvocationResponse>();
        await pendingRequestStore.WritePendingRequest(functionId, new PendingRequest(httpRequest, responseTcs, correlationId, cr.TopicPartition, cr.Offset));
        var response = await responseTcs.Task;
        await producer.ProduceAsync(replyTo, new Message<string, InvocationResponse>() { Key = functionId, Value = response, Headers = [new Header("correlationId", Encoding.UTF8.GetBytes(correlationId))] }, ct);
        logger.LogInformation("Sent response for correlationId {CorrelationId} to topic {topic}", correlationId, replyTo);
    }

    private async Task StartVm(FirecrackerClient client, CancellationToken ct)
    {
        await client.Actions.PutAsync(new API.Models.InstanceActionInfo
        {
            ActionType = API.Models.InstanceActionInfo_action_type.InstanceStart
        }, cancellationToken: ct);
    }

    private async Task ConfigureDiskAsync(FirecrackerClient client, Invocation request, FirecrackerLease lease, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request.FunctionId);
        ArgumentNullException.ThrowIfNull(request.FunctionVersion);

        (var cachedRootfs, var cachedKernelPath, var cachedOverlayfsPath) =
            await imageDownloadService.DownloadImagesForFunctionAsync(request.FunctionId!, request.FunctionVersion!, ct);

        var overlayfsPath = GetTempFileName();
        cachedOverlayfsPath.File.CopyTo(overlayfsPath, true);

        await client.Drives["rootfs"].PutAsync(new API.Models.Drive
        {
            DriveId = "rootfs",
            PathOnHost = cachedRootfs.File.FullName,
            IsReadOnly = true,
            IsRootDevice = true
        }, cancellationToken: ct);

        await client.Drives["overlayfs"].PutAsync(new API.Models.Drive
        {
            DriveId = "overlayfs",
            PathOnHost = overlayfsPath,
            IsReadOnly = false,
            IsRootDevice = false
        }, cancellationToken: ct);

        // Configure the kernel for the Firecracker VM
        await client.BootSource.PutAsync(new API.Models.BootSource
        {
            BootArgs = $"console=ttyS0 reboot=k panic=1 pci=off ip={lease.Handle.NetworkEntry.GuestIp}::{lease.Handle.NetworkEntry.HostIp}:255.255.255.252::eth0:off init=/sbin/overlay-init overlay_root=/vdb",
            KernelImagePath = cachedKernelPath.File.FullName
        }, cancellationToken: ct);

        lease.Handle.Disposables.Add(cachedRootfs);
        lease.Handle.Disposables.Add(cachedKernelPath);
        lease.Handle.Disposables.Add(cachedOverlayfsPath);
    }

    private static string? GetHeader(Headers headers, string name)
    {
        return headers.TryGetLastBytes(name, out var v) ? Encoding.UTF8.GetString(v!) : null;
    }

    private string GetTempFileName() =>
        firecrackerOptions.Value.WorkingDirectory switch
        {
            null => Path.GetTempFileName(),
            _ => Path.Combine(firecrackerOptions.Value.WorkingDirectory, Path.GetRandomFileName())
        };
}