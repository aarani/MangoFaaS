namespace MangoFaaS.Functions.Dto;

public class CreateFunctionVersionResponse
{
    public Guid Id { get; set; }
    public required string PresignedUploadUrl { get; set; }
}
