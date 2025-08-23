namespace MangoFaaS.Models;

public class MangoHttpResponse
{
    public int StatusCode { get; set; }
    public required string Body { get; set; }
    public required Dictionary<string, string> Headers { get; set; }
}
