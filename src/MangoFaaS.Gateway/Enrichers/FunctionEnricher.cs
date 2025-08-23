using MangoFaaS.Gateway.Models;
using MangoFaaS.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Route = MangoFaaS.Gateway.Models.Route;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MangoFaaS.Gateway.Enrichers;

public class FunctionEnricher(IMemoryCache memCache, MangoGatewayDbContext dbContext) : IEnricher
{
    public async Task EnrichAsync(MangoHttpRequest request)
    {
        // Remove query string for route matching
        var requestPath = request.Path;
        var qIndex = requestPath.IndexOf('?');
        if (qIndex >= 0) requestPath = requestPath[..qIndex];
        
        var cacheKey = $"routes_{request.Host}";
        if (!memCache.TryGetValue(cacheKey, out List<Route>? routes))
        {
            routes = await dbContext.Routes
                .Where(r => r.Host == request.Host)
                .ToListAsync();

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(TimeSpan.FromMinutes(5));

            memCache.Set(cacheKey, routes, cacheEntryOptions);
        }
        
        if (routes is null || routes.Count == 0) return;
        
        Route? winningRoute = null;
        int bestPriority = int.MinValue;
        int bestTieBreaker = int.MinValue; // prefer longer match when applicable
        
        foreach (var route in routes)
        {
            if (string.IsNullOrWhiteSpace(route.FunctionId) || string.IsNullOrWhiteSpace(route.Data))
                continue;
            
            var matchResult = Matches(route.Type, requestPath, route.Data);
            if (!matchResult.isMatch) continue;
            
            // Early-exit: Exact match is the highest possible priority; no need to check further
            if (route.Type == Enums.RouteType.Exact)
            {
                winningRoute = route;
                break;
            }
            
            var priority = matchResult.priority;
            var tieBreaker = matchResult.tieBreaker;
            
            if (priority > bestPriority || (priority == bestPriority && tieBreaker > bestTieBreaker))
            {
                bestPriority = priority;
                bestTieBreaker = tieBreaker;
                winningRoute = route;
            }
        }
        
        if (winningRoute != null)
        {
            request.FunctionId = winningRoute.FunctionId;
            request.FunctionVersion = winningRoute.FunctionVersion;
        }
    }
    
    private static (bool isMatch, int priority, int tieBreaker) Matches(Enums.RouteType type, string path, string pattern)
    {
        switch (type)
        {
            case Enums.RouteType.Exact:
                return (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase), 3000, pattern.Length);
            case Enums.RouteType.Prefix:
                return (path.StartsWith(pattern, StringComparison.OrdinalIgnoreCase), 2000, pattern.Length);
            case Enums.RouteType.Regex:
                try
                {
                    var isMatch = Regex.IsMatch(path, pattern);
                    return (isMatch, 1000, 0);
                }
                catch
                {
                    return (false, 0, 0);
                }
            default:
                return (false, 0, 0);
        }
    }
}