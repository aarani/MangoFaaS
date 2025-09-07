namespace MangoFaaS.Authorization.Models;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token);
