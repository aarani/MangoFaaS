using System.Net.Sockets;
using System.Text;
using Confluent.Kafka;
using MangoFaaS.Firecracker.API;
using MangoFaaS.Models;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace MangoFaaS.Firecracker.Node;

public class RequestReaderService(ILogger<RequestReaderService> logger, IConsumer<string, MangoHttpRequest> consumer, IFirecrackerProcessPool pool): BackgroundService
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
                    try
                    {
                        var cr = consumer.Consume(stoppingToken);
                        if (cr?.Message == null) continue;

                        var correlationId = GetHeader(cr.Message.Headers, "correlationId");
                        if (correlationId == null) continue;

                        // Process the request (cr.Message.Value)
                        await HandleRequestAsync(cr.Message.Value, correlationId, pool, stoppingToken);

                        // Manually commit the offset after processing
                        consumer.Commit(cr);
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

    private async Task HandleRequestAsync(MangoHttpRequest request, string correlationId, IFirecrackerProcessPool pool, CancellationToken ct)
    {
        await using var lease = await pool.AcquireAsync(request.FunctionId ?? throw new InvalidOperationException("Non-enriched request received at Node"), ct);
        var p = lease.Process;

        var client = lease.CreateClient();

        logger.LogInformation("Sending request to Firecracker API socket at {SocketPath}", lease.ApiSocketPath);
        logger.LogInformation($"[{DateTimeOffset.UtcNow:o}] Handling {correlationId} using firecracker PID {p.Id}");
        logger.LogInformation("{}", (await client.Version.GetAsync(cancellationToken: ct))?.FirecrackerVersionProp);


        
    }
    
    private static string? GetHeader(Headers headers, string name)
    {
        return headers.TryGetLastBytes(name, out var v) ? Encoding.UTF8.GetString(v!) : null;
    }
}