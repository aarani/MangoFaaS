namespace MangoFaaS.Secrets.Dto;

public class FunctionSecretsRequest
{
    public Guid FunctionId { get; set; }
}

public class FunctionSecretsResponse
{
    public List<FunctionSecretEntry> Secrets { get; set; } = [];
}

public class FunctionSecretEntry
{
    public required string Name { get; set; }
    public required string Value { get; set; }
}
