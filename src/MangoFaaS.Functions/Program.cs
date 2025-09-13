using MangoFaaS.Common;
using MangoFaaS.Common.Services;
using MangoFaaS.Functions.Models;
using MangoFaaS.Functions.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;

using Minio;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.AddMinioClient("minio");

builder.Services.AddHostedService<ImageBuilderService>();

builder.Services.AddDbContext<MangoFunctionsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("functionsdb")
                        ?? throw new InvalidOperationException("Connection string 'functionsdb' not found.")));

// TODO: move to extension method
builder.Services.AddSingleton<ProcessExecutionService>();
builder.Services.AddSingleton<Instrumentation>();

// Authorization
builder.Services.AddAuthorization();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var publicKeyPem = builder.Configuration["Jwt:PublicKeyPem"];
        if (string.IsNullOrEmpty(publicKeyPem))
        {
            throw new InvalidOperationException("JWT Public Key PEM not found in configuration['Jwt:PublicKeyPem']. Please ensure it's configured.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicKeyPem);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to import RSA public key from PEM string. Ensure it's a valid RSA public key PEM.", ex);
        }
        
        var rsaSecurityKey = new RsaSecurityKey(rsa);

        options.Authority = null; // No authority since we're using self-contained tokens
        options.Audience = null;  // No specific audience

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = rsaSecurityKey,
            ValidateIssuer = false, 
            ValidateAudience = false,
            ValidateLifetime = true
        };
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    var dbContext = services.GetRequiredService<MangoFunctionsDbContext>();
    await dbContext.Database.MigrateAsync();

    var minioClient = services.GetRequiredService<IMinioClient>();

    async Task CreateBucketIfDoesNotExist(string bucketName)
    {
        if (!await minioClient.BucketExistsAsync(new Minio.DataModel.Args.BucketExistsArgs().WithBucket(bucketName), CancellationToken.None))
        {
            await minioClient.MakeBucketAsync(new Minio.DataModel.Args.MakeBucketArgs().WithBucket(bucketName), CancellationToken.None);
        }
    }

    await CreateBucketIfDoesNotExist("runtimes");
    await CreateBucketIfDoesNotExist("raw-runtimes");
    await CreateBucketIfDoesNotExist("raw-functions");
    await CreateBucketIfDoesNotExist("functions");
    await CreateBucketIfDoesNotExist("function-manifests");
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

await app.RunAsync();