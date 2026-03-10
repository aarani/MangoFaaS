var builder = DistributedApplication.CreateBuilder(args);

var keycloak =
    builder.AddKeycloak("keycloak", 8080)
        .WithRealmImport("Assets/realm-export.json")
        .WithDataVolume("keycloak")
        .WithOtlpExporter();

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

var gateway =
    builder.AddProject<Projects.MangoFaaS_Gateway>("MangoFaaS-Gateway")
    .WithReference(kafka)
    .WithReference(gatewaydb)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WaitFor(kafka)
    .WaitFor(gatewaydb);

builder.AddProject<Projects.MangoFaaS_Firecracker_Node>("MangoFaaS-Firecracker-Node")
    .WithReference(kafka)
    .WithReference(minio)
    .WaitFor(kafka)
    .WaitFor(minio);

var functions =
    builder.AddProject<Projects.MangoFaaS_Functions>("MangoFaaS-Functions")
    .WithReference(kafka)
    .WithReference(functionsdb)
    .WithReference(minio)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WaitFor(kafka)
    .WaitFor(functionsdb)
    .WaitFor(minio);


builder.AddJavaScriptApp("frontend", "../MangoFaaS.Frontend", "dev")
    .WithHttpEndpoint(port: 5173, env: "PORT")
    .WithEnvironment("VITE_FUNCTIONS_URL", functions.GetEndpoint("http"))
    .WithEnvironment("VITE_GATEWAY_URL", gateway.GetEndpoint("http"))
    .WithEnvironment("VITE_KEYCLOAK_URL", keycloak.GetEndpoint("https"))
    .WaitFor(keycloak)
    .WaitFor(functions)
    .WaitFor(gateway);

builder.Build().Run();