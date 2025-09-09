using MangoFaaS.Functions.Dto;
using MangoFaaS.Functions.Models;
using MangoFaaS.Models;
using MangoFaaS.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace MangoFaaS.Functions.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FunctionsController(MangoFunctionsDbContext dbContext, IMinioClient minioClient) : ControllerBase
{
    private const int DefaultPresignedUrlExpirySeconds = 3600;
    private string GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) 
               ?? throw new InvalidOperationException("User ID claim (sub) not found in the JWT token.");
    }

    [HttpGet]
    public IActionResult GetFunctions()
    {
        var currentUserId = GetCurrentUserId();
        var functions = dbContext.Functions.Where(f => f.OwnerId == currentUserId).ToList();
        return Ok(functions);
    }

    [HttpGet("versions")]
    public IActionResult GetFunctionVersions()
    {
        var currentUserId = GetCurrentUserId();
        var functionVersions = dbContext.FunctionVersions
            .Join(dbContext.Functions,
                  version => version.FunctionId,
                  function => function.Id,
                  (version, function) => new { Version = version, Function = function })
            .Where(joined => joined.Function.OwnerId == currentUserId)
            .Select(joined => joined.Version)
            .ToList();
        return Ok(functionVersions);
    }

    [HttpPut]
    public async Task<ActionResult<CreateFunctionResponse>> CreateFunction(CreateFunctionRequest request)
    {
        var currentUserId = GetCurrentUserId();
        var runtimeGuid = Guid.Parse(request.Runtime);

        var function = new Function()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            OwnerId = currentUserId
            Runtime = runtimeGuid.ToString()
        };

        var runtime = await dbContext.Runtimes.FindAsync(runtimeGuid);

        if (runtime is null || !runtime.IsActive) return BadRequest("Function runtime is not active");


        dbContext.Functions.Add(function);
        await dbContext.SaveChangesAsync();

        var response = new CreateFunctionResponse
        {
            Id = function.Id
        };

        return Created(string.Empty, response);
    }

    [HttpPut("version")]
    public async Task<ActionResult<CreateFunctionVersionResponse>> CreateFunctionVersion(CreateFunctionVersionRequest request)
    {
        var currentUserId = GetCurrentUserId();

        var function = await dbContext.Functions.FindAsync(request.FunctionId);

        if (function is null) return NotFound();

        if (function.OwnerId != currentUserId)
        {
            return Forbid(); 
        }

        var versionId = Guid.NewGuid();
        var runtime = await dbContext.Runtimes.FindAsync(Guid.Parse(function.Runtime));

        if (runtime is null || !runtime.IsActive) return BadRequest("Function runtime is not active");

        var version = new FunctionVersion()
        {
            Id = versionId,
            FunctionId = request.FunctionId,
            Name = request.Name,
            Description = request.Description,
            Entrypoint = request.Entrypoint,
            FilePath = $"{request.FunctionId}/{versionId}",
            CompressionMethod = CompressionMethod.Deflate
        };

        var functionManifest = new MangoFunctionManifest
        {
            FunctionId = request.FunctionId,
            VersionId = versionId,
            RuntimeImage = runtime.FileName,
            RuntimeCompression = runtime.CompressionMethod
        };

        using var memStream = new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(functionManifest));

        dbContext.FunctionVersions.Add(version);
        await dbContext.SaveChangesAsync();

        await minioClient.PutObjectAsync(
            new Minio.DataModel.Args.PutObjectArgs()
                .WithBucket("function-manifests")
                .WithObject($"{request.FunctionId}/{versionId}.json")
                .WithStreamData(memStream)
                .WithObjectSize(memStream.Length)
                .WithContentType("application/json")
        );

        var response = new CreateFunctionVersionResponse
        {
            Id = version.Id,
            PresignedUploadUrl = await minioClient.PresignedPutObjectAsync(
                new Minio.DataModel.Args.PresignedPutObjectArgs()
                    .WithBucket("raw-functions")
                    .WithObject(version.FilePath)
                    .WithExpiry(DefaultPresignedUrlExpirySeconds)
            )
        };

        return Ok(response);
    }
}