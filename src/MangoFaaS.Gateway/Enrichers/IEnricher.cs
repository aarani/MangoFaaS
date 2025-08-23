using MangoFaaS.Models;

namespace MangoFaaS.Gateway.Enrichers;

public interface IEnricher
{
    Task EnrichAsync(MangoHttpRequest request);
}