using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using MangoFaaS.Common;
using MangoFaaS.Common.Services;
using MangoFaaS.Models;
using MangoFaaS.Models.Enums;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions;
using Minio;

namespace MangoFaaS.Firecracker.Node.Services;

public class MinioImageDownloadService(IServiceProvider serviceProvider, ILogger<MinioImageDownloadService> logger, Instrumentation instrumentation, IOptions<ImageDownloadServiceOptions> options, IMinioClient minioClient, ProcessExecutionService processExecutionService) : BackgroundService, IImageDownloadService
{
    public readonly ConcurrentDictionary<string, FileInUse> _inUseFiles = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

    public async Task<FunctionImages> DownloadImagesForFunctionAsync(string functionId, string functionVersion, CancellationToken token = default)
    {
        MangoFunctionManifest? mangoFunctionManifest = null;

        await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
            .WithBucket("function-manifests")
            .WithObject($"{functionId}/{functionVersion}.json")
            .WithCallbackStream(async (s, ct) => mangoFunctionManifest =
            await JsonSerializer.DeserializeAsync<MangoFunctionManifest>(s, cancellationToken: ct)
                ?? throw new InvalidOperationException("Failed to deserialize function manifest")), token);

        if (mangoFunctionManifest is null)
            throw new InvalidOperationException("Minio did not return a valid function manifest");

        var rootfsDownloadTask = GetOrDownload("runtimes", $"{mangoFunctionManifest.RuntimeImage}", mangoFunctionManifest.RuntimeCompression);
        var kernelDownloadTask = GetOrDownload("runtimes", $"00000000-0000-0000-0000-000000000000.vmlinux", CompressionMethod.None);
        var overlayDownloadTask = GetOrDownload("functions", $"{functionId}/{functionVersion}", mangoFunctionManifest.OverlayCompression);
        await Task.WhenAll(rootfsDownloadTask, kernelDownloadTask, overlayDownloadTask);

        return new FunctionImages(
            await rootfsDownloadTask,
            await kernelDownloadTask,
            await overlayDownloadTask
        );
    }

    public async Task<FileInUse> GetOrDownload(string bucketName, string objectName, CompressionMethod compressionMethod = CompressionMethod.None, CancellationToken token = default)
    {
        var semaphore = _downloadLocks.GetOrAdd($"{bucketName}/{objectName}", _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(token);
        try
        {
            if (_inUseFiles.TryGetValue($"{bucketName}/{objectName}", out var existingFile))
            {
                existingFile.Increment();
                return existingFile;
            }

            var downloadedFile = await Download(bucketName, objectName, compressionMethod, token);
            return _inUseFiles.AddOrUpdate($"{bucketName}/{objectName}", downloadedFile, (_, _) => downloadedFile);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<FileInUse> Download(string bucketName, string objectName, CompressionMethod compressionMethod = CompressionMethod.None, CancellationToken token = default)
    {

        var compressedPath =
            new FileInfo(Path.Combine(options.Value.CacheDirectoryPath, "_compressed", objectName));
        compressedPath.Directory?.Create();
        var extractedPath =
            new FileInfo(Path.Combine(options.Value.CacheDirectoryPath, "_extracted", objectName));
        extractedPath.Directory?.Create();

        // If for some reason we have the files but we don't have in memory, let's just delete them, might be corrupted.
        if (compressedPath.Exists && compressionMethod is not CompressionMethod.None) compressedPath.Delete();
        if (extractedPath.Exists) extractedPath.Delete();

        using var downloadActivity = instrumentation.StartActivity($"Downloading {objectName} from {bucketName}");

        await minioClient.GetObjectAsync(new Minio.DataModel.Args.GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(objectName)
            .WithFile(compressionMethod is not CompressionMethod.None ? compressedPath.FullName : extractedPath.FullName), token);

        var result = compressionMethod switch
        {
            CompressionMethod.None => extractedPath,
            _ => await Decompress(compressedPath, extractedPath, compressionMethod, token)
        };

        if (compressedPath.Exists && compressionMethod is not CompressionMethod.None) compressedPath.Delete();

        return ActivatorUtilities.CreateInstance<FileInUse>(serviceProvider, result);
    }

    public async Task<FileInfo> Decompress(FileInfo compressedFile, FileInfo extractedPath, CompressionMethod compressionMethod, CancellationToken token = default)
    {
        using var extractActivity = instrumentation.StartActivity($"Extracting {compressedFile.FullName}");

        if (compressionMethod is CompressionMethod.Tar)
        {
            var cachePath =
                new DirectoryInfo(Path.Combine(options.Value.CacheDirectoryPath, "_toremove", Guid.NewGuid().ToString("N")));
            cachePath.Create();
            await processExecutionService.RunProcess("tar", $"-xSvf {compressedFile.FullName} -C {cachePath.FullName}/", token);
            var filePath = cachePath.EnumerateFiles("*", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("No file found in extracted directory");

            filePath.MoveTo(extractedPath.FullName, true);

            cachePath.Delete(true);
        }
        else if (compressionMethod is CompressionMethod.Deflate)
        {
            using var compressedFileStream = File.OpenRead(compressedFile.FullName);
            using var deflateStream = new DeflateStream(compressedFileStream, CompressionMode.Decompress);
            using var extractedFileStream = File.Create(extractedPath.FullName);
            await deflateStream.CopyToAsync(extractedFileStream, token);
        }

        return extractedPath;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Background cleanup loop
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {

                    foreach (var (key, file) in _inUseFiles)
                    {
                        var semaphore = _downloadLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
                        bool acquired = await semaphore.WaitAsync(TimeSpan.FromMilliseconds(50), stoppingToken);
                        if (!acquired) continue;
                        try
                        {
                            if (file.CanBeDeleted() && File.Exists(file.File.FullName))
                            {
#if DEBUG
                                logger.LogInformation("File {file} can be removed, removing...", file.File.FullName);
#endif
                                try
                                {
                                    _inUseFiles.TryRemove(key, out _);
                                    file.File.Delete();
                                    _downloadLocks.TryRemove(key, out _);
                                    semaphore?.Dispose();
                                }
                                catch (Exception ex)
                                {
                                    // Log and ignore
                                    logger.LogWarning($"Failed to delete cached file {file.File.FullName}: {ex}");
                                }
                            }
                        }
                        finally
                        {
                            try { semaphore.Release(); } catch { /* ignored */ }
                        }

                    }
                }
                catch (OperationCanceledException)
                {
                    // ignore
                }
            }
        }
        finally
        {
            timer.Dispose();
        }
    }
}

public class FileInUse(ILogger<FileInUse> logger, FileInfo file) : IDisposable
{
    public FileInfo File { get; } = file;
    private int _refCount = 1;
    private DateTime _lastUsed = DateTime.UtcNow;

    private Lock @lock = new();

    public void Increment()
    {
        lock (@lock)
        {
#if DEBUG
            logger.LogInformation("File {file} incremented, new value = {newValue}", File.FullName, _refCount + 1);
#endif
            _refCount++;
            _lastUsed = DateTime.UtcNow;
        }
    }

    public void Decrement()
    {
        lock (@lock)
        {
#if DEBUG
            logger.LogInformation("File {file} decremented, new value = {newValue}", File.FullName, _refCount - 1);
#endif
            if (_refCount > 0)
                _refCount--;
        }
    }

    public bool CanBeDeleted()
    {
        lock (@lock)
        {
            return
                _refCount <= 0 && _lastUsed.AddMinutes(5) < DateTime.UtcNow;
        }
    }

    public void Dispose()
    {
        Decrement();
    }
}

public interface IImageDownloadService
{
    public Task<FunctionImages> DownloadImagesForFunctionAsync(string functionId, string functionVersion, CancellationToken token = default);
}

public record FunctionImages(FileInUse Rootfs, FileInUse Kernel, FileInUse Overlay);

public class ImageDownloadServiceOptions
{
    public required string CacheDirectoryPath { get; set; }

}