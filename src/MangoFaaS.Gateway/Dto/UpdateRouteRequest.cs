using MangoFaaS.Gateway.Dto;
using MangoFaaS.Gateway.Enums;

public class UpdateRouteRequest
{
    public string? Host;
    public string? Data;
    public string? FunctionId;
    public string? FunctionVersion;
    public RouteType? Type;
}