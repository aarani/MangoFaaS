using System.Text.Json;
using MangoFaaS.Firecracker.Node.Services;
using MangoFaaS.Firecracker.Node.Store;
using MangoFaaS.Models;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MangoFaaS.Firecracker.Node.Kestrel;

public class PendingRequestServerApplication(string functionIdWithVersion, PendingRequestStore pendingRequestStore, ILogger<PendingRequestStore> logger) : IHttpApplication<HttpContext>
{
    public HttpContext CreateContext(IFeatureCollection contextFeatures)
    {
        return new DefaultHttpContext(contextFeatures);
    }

    public void DisposeContext(HttpContext context, Exception? exception)
    {
    }

    public async Task ProcessRequestAsync(HttpContext context)
    {
        if (context.Request.Path == "/next")
        {
            var (Request, _) = await pendingRequestStore.DequeueAsync(functionIdWithVersion, context.RequestAborted);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(Request);
        }
        else if (context.Request.Path.StartsWithSegments("/response"))
        {
            var correlationId = context.Request.Query["correlationId"].ToString();

            logger.LogInformation("Received response for correlationId {CorrelationId}", correlationId);

            using var streamReader = new StreamReader(context.Request.Body);
            var json = await streamReader.ReadToEndAsync();

            MangoHttpResponse? response;

            try
            {
                response = JsonSerializer.Deserialize<MangoHttpResponse>(json)
                    ?? new MangoHttpResponse() { StatusCode = 500, Body = "Invalid response payload", Headers = [] };
            }
            catch
            {
                logger.LogWarning("Failed to deserialize response for correlationId {CorrelationId}, body = {Body}", correlationId, json);
                response = new MangoHttpResponse() { StatusCode = 500, Body = "Invalid response payload", Headers = [] };
            }
            
            if (pendingRequestStore.TryComplete(correlationId, response))
            {
                context.Response.StatusCode = 204;
            }
            else
            {
                context.Response.StatusCode = 404;
            }
        }
    }
}
