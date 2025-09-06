namespace MangoFaaS.Functions.Dto;

public class CreateRuntimeRequest
{
    public required string Name { get; set; }
    public required string Description { get; set; }
}