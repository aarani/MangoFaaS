using MangoFaaS.Common;
using MangoFaaS.Common.Helpers;
using MangoFaaS.Common.Services;
using MangoFaaS.Firecracker.Node.Kestrel;
using MangoFaaS.Firecracker.Node.Network;
using MangoFaaS.Firecracker.Node.Pooling;
using MangoFaaS.Firecracker.Node.Services;
using MangoFaaS.Firecracker.Node.Store;
using MangoFaaS.Models;
using MangoFaaS.Models.Helpers;
using Confluent.Kafka;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddMinioClient("minio");

await KafkaHelpers.CreateTopicAsync(builder, "kafka", "requests", numPartitions: 3, replicationFactor: 1);

builder.AddKafkaRpcClient<FunctionSecretsRequest, FunctionSecretsResponse>(
    "kafka",
    p => p.SetValueSerializer(new SystemTextJsonSerializer<FunctionSecretsRequest>()),
    c => c.SetValueDeserializer(new SystemTextJsonDeserializer<FunctionSecretsResponse>()),
    consumerGroupId: "firecracker-node-secrets");

builder.AddKafkaRpcServer<Invocation, InvocationResponse>(
    "kafka",
    "requests",
    sp => sp.GetRequiredService<RequestReaderService>().HandleRequestAsync,
    c => c.SetValueDeserializer(new ProtobufDeserializer<Invocation>()),
    p => p.SetValueSerializer(new ProtobufSerializer<InvocationResponse>()),
    consumerGroupId: "firecracker-node");

// Configure Firecracker pool options from configuration
builder.Services.Configure<FirecrackerPoolOptions>(builder.Configuration.GetSection("FirecrackerPool"));
builder.Services.Configure<FirecrackerNetworkOptions>(builder.Configuration.GetSection("FirecrackerNetwork"));
builder.Services.Configure<ImageDownloadServiceOptions>(builder.Configuration.GetSection("ImageDownloadService"));

// Register the pool as both a singleton and hosted service
builder.Services.AddSingleton<FirecrackerProcessPool>();
builder.Services.AddSingleton<IFirecrackerProcessPool>(sp => sp.GetRequiredService<FirecrackerProcessPool>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<FirecrackerProcessPool>());

builder.Services.AddSingleton<MinioImageDownloadService>();
builder.Services.AddSingleton<IImageDownloadService>(sp => sp.GetRequiredService<MinioImageDownloadService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MinioImageDownloadService>());
builder.Services.AddSingleton<UnixKestrelSetup>();
builder.Services.AddSingleton<Instrumentation>();
builder.Services.AddSingleton<ProcessExecutionService>();

builder.Services.AddSingleton<PendingRequestStore>();
builder.Services.AddSingleton<RequestReaderService>();

builder.Services.AddIpPoolManager();
builder.Services.AddSingleton<INetworkSetup, IpTablesNetworkSetup>();

var host = builder.Build();

await host.RunAsync();