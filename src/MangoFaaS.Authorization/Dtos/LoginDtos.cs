namespace MangoFaaS.Authorization.Dtos;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string Token);
