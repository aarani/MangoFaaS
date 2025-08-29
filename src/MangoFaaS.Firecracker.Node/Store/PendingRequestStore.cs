using System.Collections.Concurrent;
using System.Threading.Channels;
using MangoFaaS.Firecracker.Node.Models;
using MangoFaaS.Models;

namespace MangoFaaS.Firecracker.Node.Store;

public class PendingRequestStore(ILogger<PendingRequestStore> logger)
{
    private readonly ConcurrentDictionary<string, Channel<PendingRequest>> _functionChannels = new();
    private readonly ConcurrentDictionary<string, PendingRequest> _inFlightByCorrelation = new();
    private const int PerFunctionQueueCapacity = 16;

    private Channel<PendingRequest> GetChannel(string functionId) =>
        _functionChannels.GetOrAdd(functionId, _ => Channel.CreateBounded<PendingRequest>(new BoundedChannelOptions(PerFunctionQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        }));

    public async Task WritePendingRequest(string functionId, PendingRequest request)
    {
        var channel = GetChannel(functionId);
        _inFlightByCorrelation[request.CorrelationId] = request;
        await channel.Writer.WriteAsync(request);
    }

    // Called by a VM / handler polling for next request for a function
    public async ValueTask<(MangoHttpRequest Request, string CorrelationId)> DequeueAsync(string functionId, CancellationToken ct)
    {
        var channel = GetChannel(functionId);
        var pending = await channel.Reader.ReadAsync(ct).ConfigureAwait(false);
        return (pending.Request, pending.CorrelationId);
    }

    // Called when VM finishes processing and has a response
    public bool TryComplete(string correlationId, MangoHttpResponse response)
    {
        logger.LogInformation("Attempting to complete processing for correlationId {CorrelationId}", correlationId);
        if (!_inFlightByCorrelation.TryRemove(correlationId, out var pending))
            return false;
        logger.LogInformation("Completed processing for correlationId {CorrelationId}", correlationId);
        pending.CompletionSource.SetResult(response);
        return true;
    }


}