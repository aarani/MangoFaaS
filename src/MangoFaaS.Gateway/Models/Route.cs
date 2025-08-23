using System.ComponentModel.DataAnnotations;
using MangoFaaS.Gateway.Enums;

namespace MangoFaaS.Gateway.Models;

public class Route
{
    [MaxLength(100)]
    public required string Id { get; set; }
    [MaxLength(100)]
    public required string TenantId { get; set; }
    [MaxLength(100)]
    public required string Host { get; set; }
    [MaxLength(200)]
    public required string Data { get; set; }
    [MaxLength(200)]
    public required string FunctionId { get; set; }
    [MaxLength(200)]
    public required string FunctionVersion { get; set; }
    
    public RouteType Type { get; set; }
}