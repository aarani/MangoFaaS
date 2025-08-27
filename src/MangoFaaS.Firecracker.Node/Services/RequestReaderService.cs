using System.Diagnostics;
using System.Formats.Tar;
using System.Net.Sockets;
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

namespace MangoFaaS.Firecracker.Node.Services;

public class RequestReaderService(ILogger<RequestReaderService> logger, IOptions<FirecrackerPoolOptions> firecrackerOptions, IConsumer<string, MangoHttpRequest> consumer, IMinioClient minioClient, IFirecrackerProcessPool pool, Instrumentation instrumentation, ProcessExecutionService processExecutionService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe("requests");

        await Task.Run(async () =>
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ConsumeResult<string, MangoHttpRequest>? cr = null;
                    try
                    {
                        cr = consumer.Consume(stoppingToken);
                        if (cr?.Message == null) continue;

                        var correlationId = GetHeader(cr.Message.Headers, "correlationId");
                        if (correlationId == null) continue;

                        // Process the request (cr.Message.Value)
                        await HandleRequestAsync(cr.Message.Value, correlationId, pool, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Ignore cancellation
                    }
                    catch (Exception ex)
                    {
                        // Log exception
                        Console.WriteLine($"Error consuming message: {ex}");
                    }
                    finally
                    {
                        // Manually commit the offset after processing
                        if (cr is not null)
                            consumer.Commit(cr);

                        cr = null;
                    }
                }

            }
            finally
            {
                consumer.Close();
            }
        }, stoppingToken);

    }

    private async Task HandleRequestAsync(MangoHttpRequest request, string correlationId, IFirecrackerProcessPool pool, CancellationToken ct)
    {
        using var activity = instrumentation.StartActivity($"Handling {correlationId}");
        await using var lease = await pool.AcquireAsync(request.FunctionId ?? throw new InvalidOperationException("Non-enriched request received at Node"), ct);
        var p = lease.Process;

        // We can short-circuit the initalization if VM is already initialized for this function
        if (!lease.IsWarm)
        {
            activity?.AddEvent(new("Starting Cold VM"));

            var client = lease.CreateClient();

            logger.LogInformation("Sending request to Firecracker API socket at {SocketPath}", lease.ApiSocketPath);
            logger.LogInformation("Handling {correlationId} using firecracker PID {processId}", correlationId, p.Id);
            logger.LogInformation("{}", (await client.Version.GetAsync(cancellationToken: ct))?.FirecrackerVersionProp);
            try
            {
                await ConfigureKernelAsync(client, lease, ct);
                activity?.AddEvent(new("Kernel set."));
                await ConfigureDiskAsync(client, request, lease, ct);
                activity?.AddEvent(new("Disk Set."));
                await StartVm(client, request, lease, ct);
                activity?.AddEvent(new("VM Started."));
                //TODO: setup network and vsock
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


        //TODO: give request to VM
    }

    private async Task StartVm(FirecrackerClient client, MangoHttpRequest request, FirecrackerLease lease, CancellationToken ct)
    {
        await client.Actions.PutAsync(new API.Models.InstanceActionInfo
        {
            ActionType = API.Models.InstanceActionInfo_action_type.InstanceStart
        }, cancellationToken: ct);
    }

    private async Task ConfigureDiskAsync(FirecrackerClient client, MangoHttpRequest request, FirecrackerLease lease, CancellationToken ct)
    {
        var rootfsPath = GetTempFileName();
        var overlayfsPath = GetTempFileName();

        async Task DownloadAndExtract(string bucket, string objectName, string destFilePath)
        {
            var compressedPath = GetTempFileName();
            var extractDirectory = new DirectoryInfo(GetTempFileName());
            extractDirectory.Create();

            using var memStream = new MemoryStream();

            using (var downloadActivity = instrumentation.StartActivity($"Downloading {objectName} from {bucket}"))
            {
                await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
                    .WithBucket(bucket)
                    .WithObject(objectName)
                    .WithFile(compressedPath), ct);   
            }

            using (var extractActivity = instrumentation.StartActivity($"Extracting {objectName}"))
            {
                await processExecutionService.RunProcess("tar", $"-xSvf {compressedPath} -C {extractDirectory}/", ct);
            }

            var filePath = extractDirectory.EnumerateFiles("*", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("No file found in extracted directory");

            File.Move(filePath.FullName, destFilePath, overwrite: true);
        }

        async Task FindAndDownloadRootfs()
        {
            using var memStream = new MemoryStream();

            await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
                .WithBucket("function-manifests")
                .WithObject($"{request.FunctionId}/{request.FunctionVersion}.json")
                .WithCallbackStream((s, ct) => s.CopyToAsync(memStream, ct)), ct);

            memStream.Seek(0, SeekOrigin.Begin);

            MangoFunctionManifest mangoFunctionManifest =
                await JsonSerializer.DeserializeAsync<MangoFunctionManifest>(memStream, cancellationToken: ct)
                    ?? throw new InvalidOperationException("Failed to deserialize function manifest");

            await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
                .WithBucket("runtimes")
                .WithObject($"{mangoFunctionManifest.RuntimeImage}.ext4.tar")
                .WithFile(rootfsPath), ct);

            await DownloadAndExtract("runtimes", $"{mangoFunctionManifest.RuntimeImage}.ext4.tar", rootfsPath);
        }

        await Task.WhenAll(FindAndDownloadRootfs(), DownloadAndExtract("functions", $"{request.FunctionId}/{request.FunctionVersion}.ext4.tar", overlayfsPath));

        await client.Drives["rootfs"].PutAsync(new API.Models.Drive
        {
            DriveId = "rootfs",
            PathOnHost = rootfsPath,
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
    }

    private async Task ConfigureKernelAsync(FirecrackerClient client, FirecrackerLease lease, CancellationToken ct)
    {
        var kernelPath = GetTempFileName();

        await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
            .WithBucket("runtimes")
            .WithObject("00000000-0000-0000-0000-000000000000.vmlinux")
            .WithFile(kernelPath), ct);

        // Configure the kernel for the Firecracker VM
        await client.BootSource.PutAsync(new API.Models.BootSource
        {
            BootArgs = "console=ttyS0 reboot=k panic=1 pci=off init=/sbin/overlay-init overlay_root=/vdb",
            KernelImagePath = kernelPath
        }, cancellationToken: ct);
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