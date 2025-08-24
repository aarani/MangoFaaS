var builder = DistributedApplication.CreateBuilder(args);

var kafka =
    builder
        .AddKafka("kafka")
        .WithKafkaUI();

var postgres =
    builder
        .AddPostgres("postgres")
        .WithDataVolume("pgdata")
        .WithPgAdmin();

var minio =
    builder
        .AddMinioContainer("minio")
        .WithDataVolume("miniodata");

var gatewaydb = postgres.AddDatabase("gatewaydb");
var functionsdb = postgres.AddDatabase("functionsdb");

builder.AddProject<Projects.MangoFaaS_Gateway>("MangoFaaS-Gateway")
    .WithReference(kafka)
    .WithReference(gatewaydb)
    .WaitFor(kafka)
    .WaitFor(gatewaydb);

builder.AddProject<Projects.MangoFaaS_Firecracker_Node>("MangoFaaS-Firecracker-Node")
    .WithReference(kafka)
    .WithReference(minio)
    .WaitFor(kafka)
    .WaitFor(minio);

builder.AddProject<Projects.MangoFaaS_Functions>("MangoFaaS-Functions")
    .WithReference(kafka)
    .WithReference(functionsdb)
    .WithReference(minio)
    .WaitFor(kafka)
    .WaitFor(postgres)
    .WaitFor(minio);

builder.Build().Run();