using System.Security.Cryptography;
using Aspire.Hosting;

using var rsa = new RSACryptoServiceProvider(1024);
var privateKeyPem = rsa.ExportRSAPrivateKeyPem();
var publicKeyPem = rsa.ExportRSAPublicKeyPem();

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
var authdb = postgres.AddDatabase("authdb");

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
    .WithEnvironment("Jwt__PublicKeyPem", publicKeyPem)
    .WaitFor(kafka)
    .WaitFor(functionsdb)
    .WaitFor(minio);

builder.AddProject<Projects.MangoFaaS_Authorization>("MangoFaaS-Authorization")
    .WithReference(authdb)
    .WithEnvironment("Jwt__PrivateKeyPem", privateKeyPem)
    .WaitFor(authdb);

builder.Build().Run();