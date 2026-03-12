using MangoFaaS.Secrets.Dto;
using MangoFaaS.Secrets.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MangoFaaS.Secrets.Controllers;

[ApiController]
[Route("api/functions/{functionId:guid}/secrets")]
[Authorize]
public class FunctionSecretsController(MangoSecretsDbContext dbContext) : ControllerBase
{
    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID claim (sub) not found in the JWT token.");

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> GetFunctionSecrets(Guid functionId)
    {
        var currentUserId = GetCurrentUserId();

        var bindings = await dbContext.FunctionSecrets
            .AsNoTracking()
            .Include(fs => fs.Secret)
            .Where(fs => fs.FunctionId == functionId)
            .Where(fs => IsAdmin() || fs.Secret.OwnerId == currentUserId)
            .Select(fs => new FunctionSecretResponse
            {
                Id = fs.Id,
                FunctionId = fs.FunctionId,
                SecretId = fs.SecretId,
                SecretName = fs.Secret.Name
            })
            .ToListAsync();

        return Ok(bindings);
    }

    [HttpPut("{secretId:guid}")]
    public async Task<IActionResult> AddSecretToFunction(Guid functionId, Guid secretId)
    {
        var currentUserId = GetCurrentUserId();

        var secret = await dbContext.Secrets.FindAsync(secretId);
        if (secret is null) return NotFound("Secret not found.");
        if (secret.OwnerId != currentUserId && !IsAdmin()) return Forbid();

        var exists = await dbContext.FunctionSecrets
            .AnyAsync(fs => fs.FunctionId == functionId && fs.SecretId == secretId);

        if (exists) return Conflict("This secret is already bound to this function.");

        var binding = new FunctionSecret
        {
            FunctionId = functionId,
            SecretId = secretId
        };

        dbContext.FunctionSecrets.Add(binding);
        await dbContext.SaveChangesAsync();

        return Created(string.Empty, new FunctionSecretResponse
        {
            Id = binding.Id,
            FunctionId = binding.FunctionId,
            SecretId = binding.SecretId,
            SecretName = secret.Name
        });
    }

    [HttpDelete("{secretId:guid}")]
    public async Task<IActionResult> RemoveSecretFromFunction(Guid functionId, Guid secretId)
    {
        var currentUserId = GetCurrentUserId();

        var binding = await dbContext.FunctionSecrets
            .Include(fs => fs.Secret)
            .FirstOrDefaultAsync(fs => fs.FunctionId == functionId && fs.SecretId == secretId);

        if (binding is null) return NotFound();
        if (binding.Secret.OwnerId != currentUserId && !IsAdmin()) return Forbid();

        dbContext.FunctionSecrets.Remove(binding);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }
}
