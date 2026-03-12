using System.Diagnostics;
using MangoFaaS.Common;
using MangoFaaS.Common.Helpers;
using MangoFaaS.Firecracker.API;
using MangoFaaS.Firecracker.Node.Pooling;
using MangoFaaS.Models;
using Microsoft.Extensions.Options;
using MangoFaaS.Firecracker.Node.Models;
using MangoFaaS.Firecracker.Node.Store;

namespace MangoFaaS.Firecracker.Node.Services;

public class RequestReaderService(
    ILogger<RequestReaderService> logger,
    IOptions<FirecrackerPoolOptions> firecrackerOptions,
    IFirecrackerProcessPool pool,
    Instrumentation instrumentation,
    IImageDownloadService imageDownloadService,
    PendingRequestStore pendingRequestStore)
{
    public async Task<InvocationResponse> HandleRequestAsync(Invocation httpRequest, RpcContext ctx, CancellationToken ct)
    {
        var functionId = $"{httpRequest.FunctionId}:{httpRequest.FunctionVersion}";

        using var activity = instrumentation.StartActivity($"Handling {ctx.CorrelationId}");
        await using var lease = await pool.AcquireAsync(functionId ?? throw new InvalidOperationException("Non-enriched request received at Node"), ct);
        var p = lease.Process;

        // We can short-circuit the initialization if VM is already initialized for this function
        if (!lease.IsWarm)
        {
            activity?.AddEvent(new ActivityEvent("Starting Cold VM"));

            var client = lease.CreateClient();

            logger.LogInformation("Sending request to Firecracker API socket at {SocketPath}", lease.ApiSocketPath);
            logger.LogInformation("Handling {correlationId} using firecracker PID {processId}", ctx.CorrelationId, p.Id);
            try
            {
                await ConfigureDiskAsync(client, httpRequest, lease, ct);
                activity?.AddEvent(new ActivityEvent("Disk Set."));
                await StartVm(client, ct);
                activity?.AddEvent(new ActivityEvent("VM Started."));
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
            activity?.AddEvent(new ActivityEvent("Reusing warm VM"));
        }

        var responseTcs = new TaskCompletionSource<InvocationResponse>();
        await pendingRequestStore.WritePendingRequest(functionId, new PendingRequest(httpRequest, responseTcs, ctx.CorrelationId, ctx.TopicPartition, ctx.Offset));
        var response = await responseTcs.Task;
        logger.LogInformation("Completed request for correlationId {CorrelationId}", ctx.CorrelationId);
        return response;
    }

    private static async Task StartVm(FirecrackerClient client, CancellationToken ct)
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

        var (cachedRootfs, cachedKernelPath, cachedOverlayfsPath) =
            await imageDownloadService.DownloadImagesForFunctionAsync(request.FunctionId!, request.FunctionVersion!, ct);

        // Register immediately so ref counts are decremented even if subsequent operations throw
        lease.Handle.Disposables.Add(cachedRootfs);
        lease.Handle.Disposables.Add(cachedKernelPath);
        lease.Handle.Disposables.Add(cachedOverlayfsPath);

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
    }

    private string GetTempFileName() =>
        firecrackerOptions.Value.WorkingDirectory switch
        {
            null => Path.GetTempFileName(),
            _ => Path.Combine(firecrackerOptions.Value.WorkingDirectory, Path.GetRandomFileName())
        };
}
