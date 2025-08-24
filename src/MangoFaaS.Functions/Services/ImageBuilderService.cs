
using System.Diagnostics;
using System.IO.Compression;
using MangoFaaS.Functions.Models;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel;
using Minio.DataModel.Args;
using Polly;

namespace MangoFaaS.Functions.Services;

public class ImageBuilderService(ILogger<ImageBuilderService> logger, IServiceProvider serviceProvider) : BackgroundService
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
                await ConvertAndStoreFunctionImage(rawFunc, stoppingToken);
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

        var versionId = Path.GetFileNameWithoutExtension(rawFunc.Key);

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

            await RunProcess("dd", $"if=/dev/zero of={resultOverlayFile} bs=1M count=2048", cts.Token);
            await RunProcess("mkfs.ext4", $"{resultOverlayFile}", cts.Token);
            Directory.CreateDirectory($"/mnt/{versionId}");
            await RunProcess("mount", $"-o loop {resultOverlayFile} /mnt/{versionId}", cts.Token);
            Directory.CreateDirectory($"/mnt/{versionId}/overlay/root/app");
            ZipFile.ExtractToDirectory(rawFuncZipFilePath, $"/mnt/{versionId}/overlay/root/app/", true);
            await RunProcess("umount", $"/mnt/{versionId}", cts.Token);

            ResiliencePipeline pipeline = new ResiliencePipelineBuilder()
                .AddRetry(new Polly.Retry.RetryStrategyOptions { MaxRetryAttempts = 5, Delay = TimeSpan.FromMilliseconds(500) })
                .Build();

            pipeline.Execute(_ => Directory.Delete($"/mnt/{versionId}", true), CancellationToken.None);

            _ = await minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket("functions")
                .WithObject($"{funcKeyWithoutZip}.ext4")
                .WithFileName(resultOverlayFile)
                .WithContentType("application/octet-stream"),
                stoppingToken
            );

            await minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket("raw-functions")
                .WithObject(rawFunc.Key),
                stoppingToken
            );

            await dbContext
                .FunctionVersions
                .Where(x => x.Id == Guid.Parse(versionId))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.State, v => FunctionState.Deployed), stoppingToken);
        }
        catch (Exception ex)
        {
            await dbContext
                .FunctionVersions
                .Where(x => x.Id == Guid.Parse(versionId))
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

    private async Task RunProcess(string fileName, string args, CancellationToken token = default)
    {
        using Process process = new()
        {
            StartInfo =
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                FileName = fileName,
                Arguments = args
            }
        };
        process.Start();
        await process.WaitForExitAsync(token);
        if (process.HasExited)
        {
            var errorOutput = await process.StandardError.ReadToEndAsync(token);
            var output = await process.StandardOutput.ReadToEndAsync(token);

            if (process.ExitCode != 0)
            {
                logger.LogError("Process {FileName} {Args} failed with exit code {ExitCode}. Error: {ErrorOutput}", fileName, args, process.ExitCode, errorOutput);
                throw new InvalidOperationException($"Process failed with error: {errorOutput}");
            }

            logger.LogInformation("Process {FileName} {Args} completed successfully. Output: {Output}", fileName, args, output);

            return;
        }

        process.Kill(entireProcessTree: true);
        throw new InvalidOperationException("Process did not exit properly/timeout-ed!");
    }
}
