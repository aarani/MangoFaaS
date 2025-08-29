using System.Text.Json.Serialization;

namespace MangoFaaS.Models;

public class MangoHttpResponse
{
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
    
    [JsonPropertyName("body")]
    public required string Body { get; set; }

    [JsonPropertyName("headers")]
    public required Dictionary<string, string> Headers { get; set; }
}
