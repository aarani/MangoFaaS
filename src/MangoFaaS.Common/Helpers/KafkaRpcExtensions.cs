using Aspire.Confluent.Kafka;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MangoFaaS.Common.Helpers;

public static class KafkaRpcExtensions
{
    public static IHostApplicationBuilder AddKafkaRpcClient<TRequest, TResponse>(
        this IHostApplicationBuilder builder,
        string kafkaConnectionName,
        Action<ProducerBuilder<string, TRequest>> configureProducer,
        Action<ConsumerBuilder<string, TResponse>> configureConsumer,
        string? consumerGroupId = null)
        where TRequest : class
        where TResponse : class
    {
        var groupId = consumerGroupId ?? $"rpc-{typeof(TRequest).Name}-{Guid.NewGuid():N}";

        builder.AddKafkaProducer<string, TRequest>(kafkaConnectionName, configureProducer);

        builder.AddKafkaConsumer<string, TResponse>(kafkaConnectionName, (KafkaConsumerSettings settings) =>
        {
            settings.Config.GroupId = groupId;
            settings.Config.EnableAutoCommit = true;
            settings.Config.AutoOffsetReset = AutoOffsetReset.Latest;
        }, configureConsumer);

        builder.Services.AddSingleton(sp =>
        {
            var producer = sp.GetRequiredService<IProducer<string, TRequest>>();
            var consumer = sp.GetRequiredService<IConsumer<string, TResponse>>();
            var config = sp.GetRequiredService<IConfiguration>();
            var bootstrapServers = config.GetConnectionString(kafkaConnectionName)
                ?? throw new InvalidOperationException($"Connection string '{kafkaConnectionName}' not found.");
            var logger = sp.GetRequiredService<ILogger<KafkaRpcClient<TRequest, TResponse>>>();
            return new KafkaRpcClient<TRequest, TResponse>(producer, consumer, bootstrapServers, logger);
        });

        builder.Services.AddHostedService<KafkaRpcHostedService<TRequest, TResponse>>();

        return builder;
    }

    public static IHostApplicationBuilder AddKafkaRpcServer<TRequest, TResponse>(
        this IHostApplicationBuilder builder,
        string kafkaConnectionName,
        string topic,
        Func<IServiceProvider, Func<TRequest, RpcContext, CancellationToken, Task<TResponse>>> handlerFactory,
        Action<ConsumerBuilder<string, TRequest>> configureConsumer,
        Action<ProducerBuilder<string, TResponse>> configureProducer,
        string? consumerGroupId = null,
        int maxPerPartition = 8)
        where TRequest : class
        where TResponse : class
    {
        var groupId = consumerGroupId ?? $"rpc-server-{typeof(TRequest).Name}";

        builder.AddKafkaConsumer<string, TRequest>(kafkaConnectionName, (KafkaConsumerSettings settings) =>
        {
            settings.Config.GroupId = groupId;
            settings.Config.EnableAutoCommit = false;
            settings.Config.EnableAutoOffsetStore = false;
            settings.Config.AutoOffsetReset = AutoOffsetReset.Latest;
        }, configureConsumer);

        builder.AddKafkaProducer<string, TResponse>(kafkaConnectionName, configureProducer);

        builder.Services.AddSingleton(sp =>
        {
            var consumer = sp.GetRequiredService<IConsumer<string, TRequest>>();
            var producer = sp.GetRequiredService<IProducer<string, TResponse>>();
            var handler = handlerFactory(sp);
            var logger = sp.GetRequiredService<ILogger<KafkaRpcServer<TRequest, TResponse>>>();
            return new KafkaRpcServer<TRequest, TResponse>(consumer, producer, handler, topic, logger, maxPerPartition);
        });

        builder.Services.AddHostedService<KafkaRpcServerHostedService<TRequest, TResponse>>();

        return builder;
    }
}

internal class KafkaRpcHostedService<TRequest, TResponse>(KafkaRpcClient<TRequest, TResponse> client)
    : IHostedService
    where TRequest : class
    where TResponse : class
{
    public Task StartAsync(CancellationToken cancellationToken) => client.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => client.StopAsync();
}

internal class KafkaRpcServerHostedService<TRequest, TResponse>(KafkaRpcServer<TRequest, TResponse> server)
    : IHostedService
    where TRequest : class
    where TResponse : class
{
    public Task StartAsync(CancellationToken cancellationToken) => server.StartAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => server.StopAsync();
}
