using Confluent.Kafka;
using MangoFaaS.Models;

namespace MangoFaaS.Firecracker.Node.Models;

public class PendingRequest(MangoHttpRequest Request, TaskCompletionSource<MangoHttpResponse> CompletionSource, string CorrelationId, TopicPartition Partition, long Offset)
{
    public MangoHttpRequest Request { get; } = Request;
    public TaskCompletionSource<MangoHttpResponse> CompletionSource { get; } = CompletionSource;
    public string CorrelationId { get; } = CorrelationId;
    public TopicPartition Partition { get; } = Partition;
    public long Offset { get; } = Offset;
}

