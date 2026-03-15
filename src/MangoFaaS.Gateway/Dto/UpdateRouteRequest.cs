using MangoFaaS.Gateway.Enums;

namespace MangoFaaS.Gateway.Dto;

public class UpdateRouteRequest
{
    public string? Host { get; set; }
    public string? Data { get; set; }
    public string? FunctionId { get; set; }
    public string? FunctionVersion { get; set; }
    public RouteType? Type { get; set; }
}
