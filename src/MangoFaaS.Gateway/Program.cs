using System.Text;
using Aspire.Confluent.Kafka;
using Confluent.Kafka;
using MangoFaaS.Common.Helpers;
using MangoFaaS.Gateway.Enrichers;
using MangoFaaS.Gateway.Models;
using MangoFaaS.Models;
using MangoFaaS.Models.Helpers;
using Microsoft.AspNetCore.HttpOverrides;
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

        builder.AddKafkaProducer<string, Invocation>("kafka", consumerBuilder =>
        {
            consumerBuilder.SetValueSerializer(new ProtobufSerializer<Invocation>());
        });

        builder.AddKafkaConsumer<string, InvocationResponse>("kafka", (KafkaConsumerSettings settings) =>
        {
            settings.Config.GroupId = "gateway";
            settings.Config.EnableAutoCommit = true;
            settings.Config.AutoOffsetReset = AutoOffsetReset.Latest;
        }, consumerBuilder =>
        {
            consumerBuilder.SetValueDeserializer(new ProtobufDeserializer<InvocationResponse>());
        });

        builder.Services.AddMemoryCache();

        
        builder.Services.AddSingleton<ResponseReaderService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ResponseReaderService>());

        builder.Services.AddDbContext<MangoGatewayDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("gatewaydb")
                              ?? throw new InvalidOperationException("Connection string 'gatewaydb' not found.")));

        builder.Services.AddTransient<IEnricher, HttpFunctionEnricher>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        builder.AddMangoKeycloakAuth();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("MyCors", policy =>
            {
                policy
                    .WithOrigins("http://localhost", "http://localhost:5173")
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedHost;
        });
        
        
        builder.Services.AddControllers();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;

            var dbContext = services.GetRequiredService<MangoGatewayDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        app.UseCors("MyCors");

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseForwardedHeaders();

        app.MapControllers();

        app.MapFallback("/{**path}", HandleRequest);

        await app.RunAsync();
    }

    private static async Task HandleRequest(HttpContext context,
        IProducer<string, Invocation> producer,
        ResponseReaderService responseReaderService,
        IEnumerable<IEnricher> enrichers)
    {
        var correlationId = Guid.NewGuid().ToString("N");

        var headers = new Headers
        {
            new Header("correlationId", Encoding.UTF8.GetBytes(correlationId)),
            new Header("replyTo", Encoding.UTF8.GetBytes(ReplyToTopic))
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        using var bodyReader = new StreamReader(context.Request.Body);
        var invocation = new Invocation
        {
            CorrelationId = correlationId,
            HttpRequest = new HttpRequestTrigger
            {
                Method = context.Request.Method,
                Host = context.Request.Host.Host.ToLower(),
                Path = context.Request.Path,
                QueryStrings = context.Request.QueryString.ToString(),
                Body = await bodyReader.ReadToEndAsync(cts.Token),
            }
        };
        invocation.HttpRequest.Headers.Add(
            context
                .Request
                .Headers
                .ToDictionary(h => h.Key, h => h.Value.ToString()
                )
            );

        foreach (var enricher in enrichers)
        {
            if (enricher.CanEnrich(invocation))
                await enricher.EnrichAsync(invocation);
        }

        if (string.IsNullOrWhiteSpace(invocation.FunctionId) || string.IsNullOrWhiteSpace(invocation.FunctionVersion))
        {
            context.Response.StatusCode = 404;
            return;
        }

        await producer.ProduceAsync("requests", new Message<string, Invocation>
        {
            Key = $"{invocation.FunctionId}:{invocation.FunctionVersion}",
            Value = invocation,
            Headers = headers
        }, cts.Token);

        var tcs = new TaskCompletionSource<InvocationResponse>();
        // Add the request to the pending requests dictionary and remove it when the token is cancelled
        responseReaderService.AddRequest(correlationId, tcs);
        cts.Token.Register(() => responseReaderService.RemoveRequest(correlationId));

        var result = await tcs.Task.WaitAsync(cts.Token);
        if (result.HttpResponse is null)
        {
            context.Response.StatusCode = 500;
            return;
        }

        context.Response.StatusCode = result.HttpResponse.StatusCode;
        foreach (var header in result.HttpResponse.Headers)
        {
            context.Response.Headers[header.Key] = header.Value;
        }

        await context.Response.Body.WriteAsync(result.HttpResponse.Body.ToByteArray(), cts.Token);
    }
}
