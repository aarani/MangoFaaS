using Confluent.Kafka;
using MangoFaaS.Models;

namespace MangoFaaS.Firecracker.Node.Models;

public class PendingRequest(Invocation Request, TaskCompletionSource<InvocationResponse> CompletionSource, string CorrelationId, TopicPartition Partition, long Offset)
{
    public Invocation Request { get; } = Request;
    public TaskCompletionSource<InvocationResponse> CompletionSource { get; } = CompletionSource;
    public string CorrelationId { get; } = CorrelationId;
    public TopicPartition Partition { get; } = Partition;
    public long Offset { get; } = Offset;
}

