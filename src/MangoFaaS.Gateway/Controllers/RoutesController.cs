using System.Security.Claims;
using MangoFaaS.Gateway.Dto;
using MangoFaaS.Gateway.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MangoFaaS.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RoutesController(MangoGatewayDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetRoutes()
    {
        var routes = await dbContext.Routes.ToListAsync();
        return Ok(routes);
    }

    [HttpGet("{id: required}")]
    [Authorize]
    public async Task<IActionResult> GetRoute(string id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var route = await dbContext.Routes.FirstOrDefaultAsync(r => (r.TenantId == currentUserId || IsAdmin()) && r.Id == id);
        if (route is null) return NotFound();
        return Ok(route);
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> AddRoute(AddRouteRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();


        var route = new Models.Route()
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = currentUserId,
            Host = request.Host,
            Data = request.Data,
            FunctionId = request.FunctionId,
            FunctionVersion = request.FunctionVersion,
            Type = request.Type
        };


        await dbContext.Routes.AddAsync(route);
        await dbContext.SaveChangesAsync();
        return Created(string.Empty, route.Id);
    }

    [HttpPut("{id: required}")]
    [Authorize]
    public async Task<IActionResult> UpdateRoute(string id, UpdateRouteRequest request)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var route = await dbContext.Routes.FirstOrDefaultAsync(r => r.Id == id && (r.TenantId == currentUserId || IsAdmin()));
        if (route is null) return NotFound();

        var updatedRoute = new Models.Route()
        {
            Id = id,
            TenantId = currentUserId,
            Host = request.Host ?? route.Host,
            Data = request.Data ?? route.Data,
            FunctionId = request.FunctionId ?? route.FunctionId,
            FunctionVersion = request.FunctionVersion ?? route.FunctionVersion,
            Type = request.Type ?? route.Type
        };


        dbContext.Routes.Remove(route);
        await dbContext.SaveChangesAsync();
        await dbContext.Routes.AddAsync(updatedRoute);
        await dbContext.SaveChangesAsync();
        return Created(string.Empty, updatedRoute.Id);
    }

    [HttpDelete("{id: required}")]
    [Authorize]
    public async Task<IActionResult> DeleteRoute(string id)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var route = dbContext.Routes.FirstOrDefault((r) => (r.TenantId == currentUserId || IsAdmin()) && r.Id == id);
        if (route is null) return NotFound();

        dbContext.Routes.Remove(route);
        await dbContext.SaveChangesAsync();
        return NoContent();
    }

    private string GetCurrentUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("User ID claim (sub) not found in the JWT token.");

    private bool IsAdmin() => User.IsInRole("Admin");
}