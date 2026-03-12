using MangoFaaS.Common;
using MangoFaaS.Common.Helpers;
using MangoFaaS.Models.Helpers;
using MangoFaaS.Models;
using MangoFaaS.Secrets.Dto;
using MangoFaaS.Secrets.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddOpenApi();
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });

builder.Services.AddDbContext<MangoSecretsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("secretsdb")
                        ?? throw new InvalidOperationException("Connection string 'secretsdb' not found.")));

await KafkaHelpers.CreateTopicAsync(builder, "kafka", "secrets.requests", numPartitions: 1, replicationFactor: 1);

builder.AddKafkaRpcServer<FunctionSecretsRequest, FunctionSecretsResponse>(
    "kafka",
    "secrets.requests",
    sp => async (request, ctx, ct) =>
    {
        using var scope = sp.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MangoSecretsDbContext>();

        var secrets = await dbContext.FunctionSecrets
            .AsNoTracking()
            .Where(fs => fs.FunctionId == request.FunctionId)
            .Include(fs => fs.Secret)
            .Select(fs => new FunctionSecretEntry
            {
                Name = fs.Secret.Name,
                Value = fs.Secret.Value
            })
            .ToListAsync(ct);

        return new FunctionSecretsResponse { Secrets = secrets };
    },
    c => c.SetValueDeserializer(new SystemTextJsonDeserializer<FunctionSecretsRequest>()),
    p => p.SetValueSerializer(new SystemTextJsonSerializer<FunctionSecretsResponse>()),
    consumerGroupId: "secrets-rpc");

builder.Services.AddSingleton<Instrumentation>();

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

builder.AddMangoKeycloakAuth();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<MangoSecretsDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.UseCors("MyCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();
