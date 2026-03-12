using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Kafka.OffsetManagement;
using Microsoft.Extensions.Logging;

namespace MangoFaaS.Common.Helpers;

public class RpcContext
{
    public required string CorrelationId { get; init; }
    public required TopicPartition TopicPartition { get; init; }
    public required long Offset { get; init; }
    public required string Key { get; init; }
}

public class KafkaRpcServer<TRequest, TResponse>(
    IConsumer<string, TRequest> consumer,
    IProducer<string, TResponse> producer,
    Func<TRequest, RpcContext, CancellationToken, Task<TResponse>> handler,
    string topic,
    ILogger logger,
    int maxPerPartition = 8)
    where TRequest : class
    where TResponse : class
{
    private readonly ConcurrentDictionary<TopicPartition, KafkaOffsetManager> _offsetManagers = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _consumeLoop;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        consumer.Subscribe(topic);
        _consumeLoop = Task.Run(() => ConsumeLoop(_cts.Token), cancellationToken);
        return Task.CompletedTask;
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

    private void ConsumeLoop(CancellationToken stoppingToken)
    {
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

                ConsumeResult<string, TRequest>? cr;
                try
                {
                    cr = consumer.Consume(TimeSpan.FromMilliseconds(100));
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
    }

    private void Dispatch(ConsumeResult<string, TRequest> cr, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            var tp = cr.TopicPartition;
            var offsetManager = _offsetManagers.GetOrAdd(tp, _ => new KafkaOffsetManager(maxPerPartition));

            var offsetVal = cr.Offset.Value;
            try
            {
                var ackId = await offsetManager.GetAckIdAsync(offsetVal, ct);
                await HandleRequestAsync(cr, ct);
                offsetManager.Ack(ackId);
            }
            catch (OperationCanceledException)
            {
                // shutting down; do not mark completed (will reprocess on restart)
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Processing failed for {TPO}", cr.TopicPartitionOffset);
            }
        }, CancellationToken.None);
    }

    private async Task HandleRequestAsync(ConsumeResult<string, TRequest> cr, CancellationToken ct)
    {
        var correlationId = GetHeader(cr.Message.Headers, "correlationId")
            ?? throw new InvalidOperationException("No correlationId header in request");
        var replyTo = GetHeader(cr.Message.Headers, "replyTo")
            ?? throw new InvalidOperationException("No replyTo header in request");

        logger.LogDebug("Handling RPC request {CorrelationId} on {Topic}", correlationId, topic);

        var context = new RpcContext
        {
            CorrelationId = correlationId,
            TopicPartition = cr.TopicPartition,
            Offset = cr.Offset.Value,
            Key = cr.Message.Key
        };

        var response = await handler(cr.Message.Value, context, ct);

        await producer.ProduceAsync(replyTo, new Message<string, TResponse>
        {
            Key = cr.Message.Key,
            Value = response,
            Headers = [new Header("correlationId", Encoding.UTF8.GetBytes(correlationId))]
        }, ct);

        logger.LogDebug("Sent RPC reply for {CorrelationId}", correlationId);
    }

    private static string? GetHeader(Headers headers, string name)
    {
        return headers.TryGetLastBytes(name, out var v) ? Encoding.UTF8.GetString(v!) : null;
    }
}
