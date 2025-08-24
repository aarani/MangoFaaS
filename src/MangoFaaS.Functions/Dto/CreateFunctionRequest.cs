namespace MangoFaaS.Functions.Dto;

public class CreateFunctionRequest
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Runtime { get; set; } = "dotnet";
}
