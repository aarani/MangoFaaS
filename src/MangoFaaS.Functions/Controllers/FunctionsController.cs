using MangoFaaS.Functions.Dto;
using MangoFaaS.Functions.Models;
using Microsoft.AspNetCore.Mvc;
using Minio;

namespace MangoFaaS.Functions.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FunctionsController(MangoFunctionsDbContext dbContext, IMinioClient minioClient) : ControllerBase
{
    private const int DefaultPresignedUrlExpirySeconds = 3600;

    [HttpGet]
    public IActionResult GetFunctions()
    {
        //TODO: Logic to retrieve functions
        return Ok();
    }

    [HttpPut]
    public async Task<ActionResult<CreateFunctionResponse>> CreateFunction(CreateFunctionRequest request)
    {
        var function = new Function()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Runtime = request.Runtime
        };

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
        var versionId = Guid.NewGuid();

        var version = new FunctionVersion()
        {
            Id = versionId,
            FunctionId = request.FunctionId,
            Name = request.Name,
            Description = request.Description,
            Entrypoint = request.Entrypoint,
            FilePath = $"{request.FunctionId}/{versionId}"
        };

        dbContext.FunctionVersions.Add(version);
        await dbContext.SaveChangesAsync();

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