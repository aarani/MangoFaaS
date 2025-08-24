using MangoFaaS.Functions.Models;
using MangoFaaS.Functions.Services;
using Microsoft.EntityFrameworkCore;
using Minio;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers();

builder.AddMinioClient("minio");

builder.Services.AddHostedService<ImageBuilderService>();

builder.Services.AddDbContext<MangoFunctionsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("functionsdb")
                        ?? throw new InvalidOperationException("Connection string 'functionsdb' not found.")));

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

    await CreateBucketIfDoesNotExist("raw-functions");
    await CreateBucketIfDoesNotExist("functions");
}

app.UseHttpsRedirection();

app.MapControllers();

await app.RunAsync();