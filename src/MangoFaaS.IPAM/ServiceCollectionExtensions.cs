using MangoFaaS.IPAM;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIpPoolManager(this IServiceCollection services)
    {
        services.AddSingleton<IIpPoolManager, IpPoolManager>();
        return services;
    }
}
