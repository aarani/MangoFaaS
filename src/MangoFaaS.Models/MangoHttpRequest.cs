namespace MangoFaaS.Models;

public class MangoHttpRequest
{
    public required string Method { get; set; }
    public required string Host { get; set; }
    public required string Path { get; set; }
    public required string Body { get; set; }
    public required Dictionary<string, string> Headers { get; set; }
    
    
    public string? FunctionId { get; set; }
    public string? FunctionVersion { get; set; }
}