using Confluent.Kafka;
using MangoFaaS.Firecracker.Node;
using MangoFaaS.IPAM;
using MangoFaaS.Models;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddMinioClient("minio");

builder.AddKafkaConsumer<string, MangoHttpRequest>("kafka", settings  =>
{
    settings.Config.GroupId = "firecracker-node";
    settings.Config.EnableAutoCommit = false;
    settings.Config.AutoOffsetReset = AutoOffsetReset.Earliest;
}, consumerBuilder =>
{
    consumerBuilder.SetValueDeserializer(new SystemTextJsonDeserializer<MangoHttpRequest>());
});

// Configure Firecracker pool options from configuration
builder.Services.Configure<FirecrackerPoolOptions>(builder.Configuration.GetSection("FirecrackerPool"));

// Register the pool as both a singleton and hosted service
builder.Services.AddSingleton<FirecrackerProcessPool>();
builder.Services.AddSingleton<IFirecrackerProcessPool>(sp => sp.GetRequiredService<FirecrackerProcessPool>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<FirecrackerProcessPool>());

builder.Services.AddIpPoolManager();

builder.Services.AddHostedService<RequestReaderService>();

var host = builder.Build();

var ipPool = host.Services.GetRequiredService<IIpPoolManager>();
ipPool.AddPool("pool", "172.16.0.0/16");
ipPool.SplitIntoSubPools("pool", 30, false);

await host.RunAsync();