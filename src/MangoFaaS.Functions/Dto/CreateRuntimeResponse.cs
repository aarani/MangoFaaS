namespace MangoFaaS.Functions.Dto;

public class CreateRuntimeResponse
{
    public Guid Id { get; set; }
    public required string UploadUrl { get; set; }
}
