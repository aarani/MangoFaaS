using MangoFaaS.Secrets.Dto;
using MangoFaaS.Secrets.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MangoFaaS.Secrets.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SecretsController(MangoSecretsDbContext dbContext) : ControllerBase
{
    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID claim (sub) not found in the JWT token.");

    private bool IsAdmin() => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> GetSecrets()
    {
        var currentUserId = GetCurrentUserId();

        var secrets = await dbContext.Secrets
            .AsNoTracking()
            .Where(s => IsAdmin() || s.OwnerId == currentUserId)
            .Select(s => new SecretResponse
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                CreatedAt = s.CreatedAt,
                UpdatedAt = s.UpdatedAt
            })
            .ToListAsync();

        return Ok(secrets);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetSecret(Guid id)
    {
        var currentUserId = GetCurrentUserId();

        var secret = await dbContext.Secrets.FindAsync(id);
        if (secret is null) return NotFound();
        if (secret.OwnerId != currentUserId && !IsAdmin()) return Forbid();

        return Ok(new SecretValueResponse
        {
            Id = secret.Id,
            Name = secret.Name,
            Value = secret.Value,
            Description = secret.Description,
            CreatedAt = secret.CreatedAt,
            UpdatedAt = secret.UpdatedAt
        });
    }

    [HttpPut]
    public async Task<IActionResult> CreateSecret(CreateSecretRequest request)
    {
        var currentUserId = GetCurrentUserId();

        var exists = await dbContext.Secrets
            .AnyAsync(s => s.OwnerId == currentUserId && s.Name == request.Name);

        if (exists) return Conflict($"A secret with name '{request.Name}' already exists.");

        var secret = new Secret
        {
            Name = request.Name,
            Value = request.Value,
            Description = request.Description,
            OwnerId = currentUserId
        };

        dbContext.Secrets.Add(secret);
        await dbContext.SaveChangesAsync();

        return Created(string.Empty, new SecretResponse
        {
            Id = secret.Id,
            Name = secret.Name,
            Description = secret.Description,
            CreatedAt = secret.CreatedAt,
            UpdatedAt = secret.UpdatedAt
        });
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> UpdateSecret(Guid id, UpdateSecretRequest request)
    {
        var currentUserId = GetCurrentUserId();

        var secret = await dbContext.Secrets.FindAsync(id);
        if (secret is null) return NotFound();
        if (secret.OwnerId != currentUserId && !IsAdmin()) return Forbid();

        if (request.Value is not null) secret.Value = request.Value;
        if (request.Description is not null) secret.Description = request.Description;
        secret.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync();

        return Ok(new SecretResponse
        {
            Id = secret.Id,
            Name = secret.Name,
            Description = secret.Description,
            CreatedAt = secret.CreatedAt,
            UpdatedAt = secret.UpdatedAt
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteSecret(Guid id)
    {
        var currentUserId = GetCurrentUserId();

        var secret = await dbContext.Secrets.FindAsync(id);
        if (secret is null) return NotFound();
        if (secret.OwnerId != currentUserId && !IsAdmin()) return Forbid();

        dbContext.Secrets.Remove(secret);
        await dbContext.SaveChangesAsync();

        return NoContent();
    }
}
