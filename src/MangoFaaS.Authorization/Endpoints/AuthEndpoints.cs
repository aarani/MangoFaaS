using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MangoFaaS.Authorization.Dtos;
using System.Security.Cryptography;

namespace MangoFaaS.Authorization.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (
            [FromBody] LoginRequest request,
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IConfiguration configuration) =>
        {
            var result = await signInManager.PasswordSignInAsync(request.Email, request.Password, false, lockoutOnFailure: false);

            if (result.Succeeded)
            {
                var user = await userManager.FindByEmailAsync(request.Email);
                if (user == null)
                {
                    return Results.Unauthorized();
                }

                var roles = await userManager.GetRolesAsync(user);

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id),
                    new Claim(ClaimTypes.Email, user.Email!),
                    new Claim(ClaimTypes.Name, user.UserName!)
                };

                foreach (var role in roles)
                {
                    claims.Add(new Claim(ClaimTypes.Role, role));
                }

                var privateKeyPem = configuration["Jwt:PrivateKeyPem"]
                    ?? throw new InvalidOperationException("JWT Private Key 'Jwt:PrivateKeyPem' not found in configuration.");

                var rsa = RSA.Create();
                rsa.ImportFromPem(privateKeyPem);

                var securityKey = new RsaSecurityKey(rsa);
                var creds = new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);

                var token = new JwtSecurityToken(
                    issuer: configuration["Jwt:Issuer"],
                    audience: configuration["Jwt:Audience"],
                    claims: claims,
                    expires: DateTime.UtcNow.AddHours(1), // Token valid for 1 hour
                    signingCredentials: creds
                );

                return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token)));
            }

            return Results.Unauthorized();
        })
        .AllowAnonymous();

        return app;
    }
}
