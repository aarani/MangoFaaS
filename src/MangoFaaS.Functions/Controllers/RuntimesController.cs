using System.IO.Compression;
using MangoFaaS.Functions.Dto;
using MangoFaaS.Functions.Models;
using MangoFaaS.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel.Args;

namespace MangoFaaS.Functions.Controllers;

[Route("api/[controller]")]
public class RuntimesController(MangoFunctionsDbContext mangoFunctionsDbContext, IMinioClient minioClient) : ControllerBase
{
    private const int DefaultPresignedUrlExpirySeconds = 3600;

    private const string RawRuntimesBucketName = "raw-runtimes";

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Runtime>>> GetRuntimes()
    {
        var runtimes =
            await mangoFunctionsDbContext
                .Runtimes
                .Where(runtime => runtime.IsActive)
                .AsNoTracking()
                .ToListAsync();
        return runtimes;
    }

    [HttpPut]
    public async Task<ActionResult> CreateRuntime([FromBody] CreateRuntimeRequest request)
    {
        var runtimeId = Guid.NewGuid();

        var runtime = new Runtime()
        {
            Id = runtimeId,
            Name = request.Name,
            Description = request.Description,
            IsActive = false
        };

        var response = new CreateRuntimeResponse
        {
            Id = runtime.Id,
            UploadUrl = await minioClient.PresignedPutObjectAsync(
                new PresignedPutObjectArgs()
                    .WithBucket(RawRuntimesBucketName)
                    .WithObject($"{runtime.Id}.ext4")
                    .WithExpiry(DefaultPresignedUrlExpirySeconds)
            )
        };

        mangoFunctionsDbContext.Runtimes.Add(runtime);
        await mangoFunctionsDbContext.SaveChangesAsync();

        return Created(string.Empty, response);
    }

    [HttpPatch("{id}/activate")]
    public async Task<ActionResult> ActivateRuntime(Guid id, [FromBody] ActivateRuntimeRequest request)
    {
        var runtime = await mangoFunctionsDbContext.Runtimes.FindAsync(id);
        if (runtime == null)
        {
            return NotFound();
        }

        if (runtime.IsActive) return Conflict();

        var tempRawRuntimePath = Path.GetTempFileName();

        _ =
            await minioClient.GetObjectAsync(new GetObjectArgs()
                .WithBucket(RawRuntimesBucketName)
                .WithObject($"{runtime.Id}.ext4")
                .WithFile(tempRawRuntimePath),
                CancellationToken.None
            );

        if (request.CompressionMethod == CompressionMethod.Deflate)
        {
            var tempDeflatedPath = Path.GetTempFileName();

            using (var deflatedFile = System.IO.File.OpenWrite(tempDeflatedPath))
            using (DeflateStream compressor = new(deflatedFile, CompressionLevel.Fastest))
            {
                using (var runtimeFile = System.IO.File.OpenRead(tempRawRuntimePath))
                {
                    runtimeFile.Seek(0, SeekOrigin.Begin);
                    await runtimeFile.CopyToAsync(compressor);
                    await compressor.FlushAsync();
                }

                System.IO.File.Delete(tempRawRuntimePath);
            }

            await minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket("runtimes")
                .WithObject($"{runtime.Id}.ext4.deflate")
                .WithFileName(tempDeflatedPath)
                .WithContentType("application/octet-stream"),
                CancellationToken.None
            );

            System.IO.File.Delete(tempDeflatedPath);

            runtime.FileName = $"{runtime.Id}.ext4.deflate";
            runtime.CompressionMethod = CompressionMethod.Deflate;
        }
        else if (request.CompressionMethod == CompressionMethod.None)
        {
            await minioClient.PutObjectAsync(new PutObjectArgs()
                .WithBucket("runtimes")
                .WithObject($"{runtime.Id}.ext4.raw")
                .WithFileName(tempRawRuntimePath)
                .WithContentType("application/octet-stream"),
                CancellationToken.None
            );

            runtime.FileName = $"{runtime.Id}.ext4.raw";
            runtime.CompressionMethod = CompressionMethod.None;
        }
        else
        {
            System.IO.File.Delete(tempRawRuntimePath);
            return BadRequest("Unsupported compression method");
        }

        runtime.IsActive = true;
        await mangoFunctionsDbContext.SaveChangesAsync();

        await minioClient.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket("raw-runtimes")
            .WithObject($"{runtime.Id}.ext4"),
            CancellationToken.None
        );

        return NoContent();
    }
}

