namespace MangoFaaS.Secrets.Dto;

public class FunctionSecretResponse
{
    public Guid Id { get; set; }
    public Guid FunctionId { get; set; }
    public Guid SecretId { get; set; }
    public required string SecretName { get; set; }
}
