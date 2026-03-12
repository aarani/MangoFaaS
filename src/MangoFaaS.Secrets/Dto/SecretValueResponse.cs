namespace MangoFaaS.Secrets.Dto;

public class SecretValueResponse
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Value { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
