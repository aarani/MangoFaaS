using Confluent.Kafka;
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

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddMinioClient("minio");

await KafkaHelpers.CreateTopicAsync(builder, "kafka", "requests", numPartitions: 3, replicationFactor: 1);

builder.AddKafkaConsumer<string, MangoHttpRequest>("kafka", settings  =>
{
    settings.Config.GroupId = "firecracker-node";
    settings.Config.EnableAutoCommit = false;
    settings.Config.AutoOffsetReset = AutoOffsetReset.Earliest;
}, consumerBuilder =>
{
    consumerBuilder.SetValueDeserializer(new SystemTextJsonDeserializer<MangoHttpRequest>());
});

builder.AddKafkaProducer<string, MangoHttpResponse>("kafka", consumerBuilder =>
{
    consumerBuilder.SetValueSerializer(new SystemTextJsonSerializer<MangoHttpResponse>());
});

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
builder.Services.AddHostedService(sp => sp.GetRequiredService<RequestReaderService>());

builder.Services.AddIpPoolManager();
builder.Services.AddSingleton<INetworkSetup, IpTablesNetworkSetup>();

var host = builder.Build();

await host.RunAsync();