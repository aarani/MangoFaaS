using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace MangoFaaS.Common.Helpers;

public class KafkaRpcClient<TRequest, TResponse>(
    IProducer<string, TRequest> producer,
    IConsumer<string, TResponse> consumer,
    string bootstrapServers,
    ILogger logger)
    : IDisposable
    where TRequest : class
    where TResponse : class
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TResponse>> _pending = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumeLoop;

    private string ReplyTopic { get; } = $"rpc.replies.{Guid.NewGuid():N}";

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await CreateReplyTopicAsync();

        consumer.Subscribe(ReplyTopic);
        _consumeLoop = Task.Run(() => ConsumeLoop(_cts.Token), cancellationToken);
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        if (_consumeLoop != null)
        {
            try { await _consumeLoop; }
            catch (OperationCanceledException) { }
        }
    }

    public async Task<TResponse> SendAsync(
        string topic,
        string key,
        TRequest request,
        CancellationToken cancellationToken = default,
        TimeSpan? timeout = null)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<TResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        linkedCts.Token.Register(() => {
            if (_pending.TryRemove(correlationId, out var removed))
                removed.TrySetCanceled(CancellationToken.None);
        });

        var headers = new Headers
        {
            new Header("correlationId", Encoding.UTF8.GetBytes(correlationId)),
            new Header("replyTo", Encoding.UTF8.GetBytes(ReplyTopic))
        };

        await producer.ProduceAsync(topic, new Message<string, TRequest>
        {
            Key = key,
            Value = request,
            Headers = headers
        }, cancellationToken);

        return await tcs.Task.WaitAsync(linkedCts.Token);
    }

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cr = consumer.Consume(stoppingToken);
                    if (cr?.Message == null) continue;

                    var correlationId = GetHeader(cr.Message.Headers, "correlationId");
                    if (correlationId == null)
                    {
                        logger.LogWarning("Received RPC reply without correlationId on {Topic}", cr.Topic);
                        continue;
                    }

                    logger.LogDebug("Received RPC reply for {CorrelationId}", correlationId);

                    if (_pending.TryRemove(correlationId, out var tcs))
                    {
                        tcs.TrySetResult(cr.Message.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error consuming RPC reply");
                }
            }
        }
        finally
        {
            consumer.Close();
            consumer.Dispose();
        }
    }

    private async Task CreateReplyTopicAsync()
    {
        var config = new AdminClientConfig { BootstrapServers = bootstrapServers };
        using var adminClient = new AdminClientBuilder(config).Build();

        try
        {
            await adminClient.CreateTopicsAsync([
                new TopicSpecification { Name = ReplyTopic, NumPartitions = 1, ReplicationFactor = 1 }
            ]);
        }
        catch (CreateTopicsException e) when (e.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        producer.Dispose();
    }

    private static string? GetHeader(Headers headers, string name)
    {
        return headers.TryGetLastBytes(name, out var v) ? Encoding.UTF8.GetString(v!) : null;
    }
}
