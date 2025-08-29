using System.Text;
using Aspire.Confluent.Kafka;
using Confluent.Kafka;
using MangoFaaS.Common.Helpers;
using MangoFaaS.Gateway.Enrichers;
using MangoFaaS.Gateway.Models;
using MangoFaaS.Models;
using MangoFaaS.Models.Helpers;
using Microsoft.EntityFrameworkCore;

namespace MangoFaaS.Gateway;

public static class Program
{
    public static readonly string ReplyToTopic = $"rpc.replies.{Guid.NewGuid():N}";
    
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        await KafkaHelpers.CreateTopicAsync(builder, "kafka", "requests", numPartitions: 3, replicationFactor: 1);
        await KafkaHelpers.CreateTopicAsync(builder, "kafka", ReplyToTopic, numPartitions: 1, replicationFactor: 1);

        builder.AddServiceDefaults();
        
        builder.AddKafkaProducer<string, MangoHttpRequest>("kafka", consumerBuilder =>
        {
            consumerBuilder.SetValueSerializer(new SystemTextJsonSerializer<MangoHttpRequest>());
        });
        
        builder.AddKafkaConsumer<string, MangoHttpResponse>("kafka", (KafkaConsumerSettings settings)  =>
        {
            settings.Config.GroupId = "gateway";
            settings.Config.EnableAutoCommit = true;
            settings.Config.AutoOffsetReset = AutoOffsetReset.Earliest;
        }, consumerBuilder =>
        {
            consumerBuilder.SetValueDeserializer(new SystemTextJsonDeserializer<MangoHttpResponse>());
        });
        
        builder.Services.AddMemoryCache();
        
        builder.Services.AddSingleton<ResponseReaderService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ResponseReaderService>());

        builder.Services.AddDbContext<MangoGatewayDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("gatewaydb")
                              ?? throw new InvalidOperationException("Connection string 'gatewaydb' not found.")));

        builder.Services.AddTransient<IEnricher, FunctionEnricher>();
        
        var app = builder.Build();
        
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            
            var dbContext = services.GetRequiredService<MangoGatewayDbContext>();
            await dbContext.Database.MigrateAsync();
        }
        
        app.UseHttpsRedirection();

        app.Map("/{**path}", HandleRequest);
        
        await app.RunAsync();
    }

    private static async Task HandleRequest(HttpContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var serviceProvider = context.RequestServices; 
        var producer = serviceProvider.GetRequiredService<IProducer<string, MangoHttpRequest>>();
        var responseReader = serviceProvider.GetRequiredService<ResponseReaderService>();
        
        var headers = new Headers
        {
            new Header("correlationId", Encoding.UTF8.GetBytes(correlationId)),
            new Header("replyTo", Encoding.UTF8.GetBytes(ReplyToTopic))
        };
        
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        
        var request = new MangoHttpRequest()
        {
            Method = context.Request.Method,
            Host = context.Request.Host.Host.ToLower(),
            Path = context.Request.Path + context.Request.QueryString,
            Body = await new StreamReader(context.Request.Body).ReadToEndAsync(cts.Token),
            Headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
            CorrelationId = correlationId
        };
        
        var enrichers = serviceProvider.GetServices<IEnricher>();
        foreach (var enricher in enrichers)
        {
            await enricher.EnrichAsync(request);
        }

        if (string.IsNullOrWhiteSpace(request.FunctionId) || string.IsNullOrWhiteSpace(request.FunctionVersion))
        {
            context.Response.StatusCode = 404;
            return;
        }

        await producer.ProduceAsync("requests", new Message<string, MangoHttpRequest>
        {
            Key = $"{request.FunctionId}:{request.FunctionVersion}",
            Value = request,
            Headers = headers
        }, cts.Token);
        
        var tcs = new TaskCompletionSource<MangoHttpResponse>();
        // Add the request to the pending requests dictionary and remove it when the token is cancelled
        responseReader.AddRequest(correlationId, tcs);
        cts.Token.Register(() => responseReader.RemoveRequest(correlationId));
        
        var result = await tcs.Task.WaitAsync(cts.Token);
        context.Response.StatusCode = result.StatusCode;
        foreach (var header in result.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }
        await context.Response.WriteAsync(result.Body, cancellationToken: cts.Token);
    }
}