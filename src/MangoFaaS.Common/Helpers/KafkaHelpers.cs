using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MangoFaaS.Common.Helpers;

public static class KafkaHelpers
{
    public static async Task CreateTopicAsync(IHostApplicationBuilder builder, string kafkaConnName, string topicName, int numPartitions, short replicationFactor)
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = builder.Configuration.GetConnectionString(kafkaConnName)
                ?? throw new InvalidOperationException($"Connection string '{kafkaConnName}' not found.")
        };

        using var adminClient = new AdminClientBuilder(config).Build();
        try
        {
            await adminClient.CreateTopicsAsync([
                new TopicSpecification { Name = topicName, NumPartitions = numPartitions, ReplicationFactor = replicationFactor }
            ]);
        }
        catch (CreateTopicsException e) when (e.Results[0].Error.Code == ErrorCode.TopicAlreadyExists)
        {
            // Topic already exists, ignore
        }
        catch (Exception e)
        {
            Console.WriteLine($"An error occurred creating topic: {e.Message}");
            throw;
        }

    }
}