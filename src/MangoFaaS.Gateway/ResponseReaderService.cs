using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using MangoFaaS.Models;

namespace MangoFaaS.Gateway;

public class ResponseReaderService(IConsumer<string, MangoHttpResponse> consumer, ILogger<ResponseReaderService> logger): BackgroundService
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<MangoHttpResponse>> _pendingRequests = new();
    
    public void AddRequest(string correlationId, TaskCompletionSource<MangoHttpResponse> tcs)
    {
        _pendingRequests[correlationId] = tcs;
    }

    public void RemoveRequest(string correlationId) => _pendingRequests.TryRemove(correlationId, out _);
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        consumer.Subscribe(Program.ReplyToTopic);
        
        await Task.Run(() =>
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var cr = consumer.Consume(stoppingToken);
                        if (cr?.Message == null) continue;
                        logger.LogDebug("Consumed message at {TopicPartitionOffset}", cr.TopicPartitionOffset);

                        var correlationId = GetHeader(cr.Message.Headers, "correlationId");
                        if (correlationId == null) continue;

                        logger.LogInformation("Received response for correlationId {CorrelationId}", correlationId);

                        if (_pendingRequests.TryRemove(correlationId, out var tcs))
                        {
                            tcs.SetResult(cr.Message.Value);
                        }
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
                }

            }
            finally
            {
                consumer.Close();
            }
        }, stoppingToken);

    }
    
    private static string? GetHeader(Headers headers, string name)
    {
        return headers.TryGetLastBytes(name, out var v) ? Encoding.UTF8.GetString(v!) : null;
    }
}