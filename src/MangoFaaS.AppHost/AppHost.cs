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

var gatewaydb = postgres.AddDatabase("gatewaydb");

builder.AddProject<Projects.MangoFaaS_Gateway>("MangoFaaS-Gateway")
    .WithReference(kafka)
    .WithReference(gatewaydb)
    .WaitFor(kafka)
    .WaitFor(gatewaydb);

builder.AddProject<Projects.MangoFaaS_Firecracker_Node>("MangoFaaS-Firecracker-Node")
    .WithReference(kafka)
    .WaitFor(kafka);

builder.Build().Run();