
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using MangoFaaS.Common.Services;
using MangoFaaS.Functions.Models;
using MangoFaaS.Models.Enums;
using Medallion.Threading.Postgres;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Polly;

namespace MangoFaaS.Functions.Services;

public class ImageBuilderService(ILogger<ImageBuilderService> logger, IConfiguration configuration, IServiceProvider serviceProvider, ProcessExecutionService processExecutionService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = serviceProvider.CreateScope();
            var minioClient = scope.ServiceProvider.GetRequiredService<IMinioClient>();

            await foreach (var rawFunc in minioClient.ListObjectsEnumAsync(new ListObjectsArgs()
                .WithBucket("raw-functions")
                .WithRecursive(true),
                stoppingToken
            ))
            {
                logger.LogInformation("Processing raw function: {FunctionKey}", rawFunc.Key);
                var connectionString = configuration.GetConnectionString("functionsdb") ?? throw new InvalidOperationException("Connection string 'functionsdb' not found.");
                var @lock = new PostgresDistributedLock(new PostgresAdvisoryLockKey(rawFunc.Key, allowHashing: true), connectionString);
                await using (await @lock.AcquireAsync(cancellationToken: stoppingToken))
                {   
                    await ConvertAndStoreFunctionImage(rawFunc, stoppingToken);
                }
            }
        }
    }

    private async Task ConvertAndStoreFunctionImage(Item rawFunc, CancellationToken stoppingToken)
    {
        using var scope = serviceProvider.CreateScope();
        var minioClient = scope.ServiceProvider.GetRequiredService<IMinioClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MangoFunctionsDbContext>();

        var funcKeyWithoutZip =
            rawFunc.Key.Replace(".zip", "");

        var funcKeyWithoutZipParts = funcKeyWithoutZip.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var functionId = Guid.Parse(funcKeyWithoutZipParts[0]);
        var versionId = Guid.Parse(funcKeyWithoutZipParts[1]);

        var rawFuncZipFilePath = Path.GetTempFileName();
        var resultOverlayFile = Path.GetTempFileName();

        CancellationTokenSource cts = new(TimeSpan.FromMinutes(10));

        try
        {
            var function = await minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket("raw-functions")
                .WithObject(rawFunc.Key)
                .WithFile(rawFuncZipFilePath),
                stoppingToken
            );

            var version = await dbContext.FunctionVersions.FindAsync([versionId], cancellationToken: cts.Token) ??
                throw new InvalidOperationException("Function version not found in DB, cannot proceed!");

            //TODO: get overlay size from the function version metadata
            await processExecutionService.RunProcess("dd", $"if=/dev/zero of={resultOverlayFile} conv=sparse bs=1M count=2048", cts.Token);
            await processExecutionService.RunProcess("mkfs.ext4", $"{resultOverlayFile}", cts.Token);
            Directory.CreateDirectory($"/mnt/{versionId}");
            await processExecutionService.RunProcess("mount", $"-o loop,sync {resultOverlayFile} /mnt/{versionId}", cts.Token);
            Directory.CreateDirectory($"/mnt/{versionId}/root/app");
            ZipFile.ExtractToDirectory(rawFuncZipFilePath, $"/mnt/{versionId}/root/app/", true);
            await File.WriteAllTextAsync($"/mnt/{versionId}/root/entrypoint.txt", version.Entrypoint, cts.Token);
            await processExecutionService.RunProcess("umount", $"/mnt/{versionId}", cts.Token);

            ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
                .AddRetry(new Polly.Retry.RetryStrategyOptions { MaxRetryAttempts = 5, Delay = TimeSpan.FromMilliseconds(500) })
                .Build();

            pipeline.Execute(_ => Directory.Delete($"/mnt/{versionId}", true), CancellationToken.None);

            Stream? compressedStream = null;
            try
            {
                if (version.CompressionMethod is CompressionMethod.Deflate)
                {
                    compressedStream = new MemoryStream();
                    using (var compressor = new DeflateStream(compressedStream, CompressionMode.Compress, true))
                    {
                        using var tarResultFileStream = File.OpenRead(resultOverlayFile);
                        await tarResultFileStream.CopyToAsync(compressor, stoppingToken);
                        await compressor.FlushAsync(stoppingToken);
                    }
                    compressedStream.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    throw new InvalidOperationException("Unknown compression method");
                }

                _ = await minioClient.PutObjectAsync(new PutObjectArgs()
                    .WithBucket("functions")
                    .WithObject($"{functionId}/{versionId}")
                    .WithStreamData(compressedStream)
                    .WithObjectSize(compressedStream.Length)
                    .WithContentType("application/octet-stream"),
                    stoppingToken
                );
            }
            finally
            {
                compressedStream?.Dispose();
            }

            await minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket("raw-functions")
                .WithObject(rawFunc.Key),
                stoppingToken
            );

            await dbContext
                .FunctionVersions
                .Where(x => x.Id == versionId)
                .ExecuteUpdateAsync(version =>
                    version
                        .SetProperty(v => v.State, v => FunctionState.Deployed),
                    stoppingToken
                );
        }
        catch (Exception ex)
        {
            await dbContext
                .FunctionVersions
                .Where(x => x.Id == versionId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.State, v => FunctionState.Failed), stoppingToken);

            await minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket("raw-functions")
                .WithObject(rawFunc.Key),
                stoppingToken
            );

            logger.LogCritical(ex, "Error processing function {FunctionKey}, might need manual cleanup :S", rawFunc.Key);
        }
        finally
        {
            try
            {
                File.Delete(rawFuncZipFilePath);
                File.Delete(resultOverlayFile);
            }
            catch
            {
                // ignored
            }
        }
    }
}
