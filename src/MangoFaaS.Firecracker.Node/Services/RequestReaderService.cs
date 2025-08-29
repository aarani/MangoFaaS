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

namespace MangoFaaS.Firecracker.Node.Services;

public class RequestReaderService(
    ILogger<RequestReaderService> logger,
    IOptions<FirecrackerPoolOptions> firecrackerOptions,
    IConsumer<string, MangoHttpRequest> consumer,
    IProducer<string, MangoHttpResponse> producer,
    IFirecrackerProcessPool pool,
    Instrumentation instrumentation,
    IImageDownloadService imageDownloadService,
    PendingRequestStore pendingRequestStore) : BackgroundService
{
    // Global + per-partition concurrency limits (TODO: make configurable via options)
    private const int GlobalMaxConcurrency = 64;
    private const int MaxInFlightPerPartition = 4;

    private sealed class PartitionState
    {
        public long NextCommitOffset; // first offset NOT yet committed (commit cursor)
        public bool Initialized;
        public readonly SortedSet<long> CompletedOffsets = new(); // processed offsets awaiting gap closure
        public int InFlight; // tasks currently running for this partition
        public readonly Queue<ConsumeResult<string, MangoHttpRequest>> Pending = new(); // queued when per-partition limit hit
        public readonly object LockObj = new();
    }

    private readonly ConcurrentDictionary<TopicPartition, PartitionState> _partitionStates = new();
    private readonly ConcurrentQueue<TopicPartitionOffset> _commitQueue = new();
    private readonly SemaphoreSlim _globalSemaphore = new(GlobalMaxConcurrency, GlobalMaxConcurrency);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Run(() =>
        {
            
            consumer.Subscribe("requests");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    // Drain pending commits on consumer thread to maintain ordering guarantees
                    while (_commitQueue.TryDequeue(out var tpo))
                    {
                        try { consumer.Commit([tpo]); }
                        catch (Exception e) { logger.LogWarning(e, "Commit failed for {TPO}", tpo); }
                    }

                    ConsumeResult<string, MangoHttpRequest>? cr = null;
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

                    var tp = cr.TopicPartition;
                    var state = _partitionStates.GetOrAdd(tp, _ => new PartitionState());
                    bool enqueuedPending = false;

                    lock (state.LockObj)
                    {
                        if (!state.Initialized)
                        {
                            state.NextCommitOffset = cr.Offset.Value; // earliest offset we may commit (exclusive)
                            state.Initialized = true;
                        }

                        if (state.InFlight >= MaxInFlightPerPartition)
                        {
                            state.Pending.Enqueue(cr);
                            enqueuedPending = true;
                        }
                    }

                    if (enqueuedPending) continue; // will be dispatched when a slot frees

                    Dispatch(cr, state, stoppingToken);
                }
            }
            finally
            {
                try { consumer.Close(); } catch { /* ignore */ }
            }
        }, stoppingToken);
    }

    private void Dispatch(ConsumeResult<string, MangoHttpRequest> cr, PartitionState state, CancellationToken ct)
    {
        lock (state.LockObj)
        {
            state.InFlight++;
        }

        _ = Task.Run(async () =>
        {
            var tp = cr.TopicPartition;
            var offsetVal = cr.Offset.Value;
            try
            {
                await _globalSemaphore.WaitAsync(ct).ConfigureAwait(false);
                await HandleRequestAsync(cr, ct).ConfigureAwait(false);
                MarkCompleted(tp, offsetVal);
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
            finally
            {
                _globalSemaphore.Release();
                FinishAndMaybeDispatchNext(tp);
            }
        }, CancellationToken.None);
    }

    private void FinishAndMaybeDispatchNext(TopicPartition tp)
    {
        if (!_partitionStates.TryGetValue(tp, out var state)) return;
        ConsumeResult<string, MangoHttpRequest>? next = null;

        lock (state.LockObj)
        {
            state.InFlight--;
            if (state.InFlight < MaxInFlightPerPartition && state.Pending.Count > 0)
            {
                next = state.Pending.Dequeue();
            }
        }

        if (next != null)
        {
            Dispatch(next, state, CancellationToken.None);
        }
    }

    private void MarkCompleted(TopicPartition tp, long offset)
    {
        var state = _partitionStates[tp];
        TopicPartitionOffset? toCommit = null;

        lock (state.LockObj)
        {
            state.CompletedOffsets.Add(offset);

            // Advance commit cursor while we have a contiguous run from NextCommitOffset
            while (state.CompletedOffsets.Contains(state.NextCommitOffset))
            {
                state.CompletedOffsets.Remove(state.NextCommitOffset);
                state.NextCommitOffset++;
            }

            // If we advanced past this offset we can commit up to NextCommitOffset (exclusive)
            if (offset < state.NextCommitOffset)
            {
                toCommit = new TopicPartitionOffset(tp, new Offset(state.NextCommitOffset));
            }
        }

        if (toCommit is TopicPartitionOffset tpo)
        {
            _commitQueue.Enqueue(tpo);
        }
    }

    private async Task HandleRequestAsync(ConsumeResult<string, MangoHttpRequest> cr, CancellationToken ct)
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

        var responseTcs = new TaskCompletionSource<MangoHttpResponse>();
        await pendingRequestStore.WritePendingRequest(functionId, new PendingRequest(httpRequest, responseTcs, correlationId, cr.TopicPartition, cr.Offset));
        var response = await responseTcs.Task;
        await producer.ProduceAsync(replyTo, new Message<string, MangoHttpResponse>() { Key = functionId, Value = response, Headers = [new Header("correlationId", Encoding.UTF8.GetBytes(correlationId))] }, ct);
        logger.LogInformation("Sent response for correlationId {CorrelationId} to topic {topic}", correlationId, replyTo);
    }

    private async Task StartVm(FirecrackerClient client, CancellationToken ct)
    {
        await client.Actions.PutAsync(new API.Models.InstanceActionInfo
        {
            ActionType = API.Models.InstanceActionInfo_action_type.InstanceStart
        }, cancellationToken: ct);
    }

    private async Task ConfigureDiskAsync(FirecrackerClient client, MangoHttpRequest request, FirecrackerLease lease, CancellationToken ct)
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