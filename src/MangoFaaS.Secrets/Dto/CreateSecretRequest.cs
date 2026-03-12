namespace MangoFaaS.Secrets.Dto;

public class CreateSecretRequest
{
    public required string Name { get; set; }
    public required string Value { get; set; }
    public string? Description { get; set; }
}
