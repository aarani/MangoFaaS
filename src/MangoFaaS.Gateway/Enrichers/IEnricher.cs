using MangoFaaS.Models;

namespace MangoFaaS.Gateway.Enrichers;

public interface IEnricher
{
    Task EnrichAsync(Invocation invocation);

    bool CanEnrich(Invocation invocation);
}