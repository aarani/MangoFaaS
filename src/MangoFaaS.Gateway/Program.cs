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
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        await KafkaHelpers.CreateTopicAsync(builder, "kafka", "requests", numPartitions: 3, replicationFactor: 1);

        builder.AddServiceDefaults();

        builder.AddKafkaRpcClient<Invocation, InvocationResponse>(
            "kafka",
            p => p.SetValueSerializer(new ProtobufSerializer<Invocation>()),
            c => c.SetValueDeserializer(new ProtobufDeserializer<InvocationResponse>()),
            consumerGroupId: "gateway");

        builder.Services.AddMemoryCache();

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
        KafkaRpcClient<Invocation, InvocationResponse> rpcClient,
        IEnumerable<IEnricher> enrichers)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        cts.CancelAfter(TimeSpan.FromMinutes(1));
        using var bodyReader = new StreamReader(context.Request.Body);
        var invocation = new Invocation
        {
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

        var result = await rpcClient.SendAsync(
            "requests",
            $"{invocation.FunctionId}:{invocation.FunctionVersion}",
            invocation,
            cts.Token,
            timeout: TimeSpan.FromMinutes(1));

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
