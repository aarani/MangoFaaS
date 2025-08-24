namespace MangoFaaS.Functions.Dto;

public class CreateFunctionVersionRequest
{
    public Guid FunctionId { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string Entrypoint { get; set; }
}
