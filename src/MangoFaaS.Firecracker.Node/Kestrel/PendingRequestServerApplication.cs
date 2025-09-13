using System.Buffers;
using System.Text.Json;
using Google.Protobuf;
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
            context.Response.ContentType = "application/protobuf";
            if (Request is null) return;
            using var memStream = new MemoryStream();
            Request.WriteTo(memStream);
            await context.Response.Body.WriteAsync(memStream.ToArray());
        }
        else if (context.Request.Path.StartsWithSegments("/response"))
        {
            var correlationId = context.Request.Query["correlationId"].ToString();

            logger.LogInformation("Received response for correlationId {CorrelationId}", correlationId);

            InvocationResponse response = null!;

            try
            {
                using MemoryStream memStream = new();
                await context.Request.Body.CopyToAsync(memStream);
                response = InvocationResponse.Parser.ParseFrom(memStream.ToArray());
            }
            catch (Exception e)
            {
                logger.LogWarning("Failed to deserialize response for correlationId {CorrelationId} {e}", correlationId, e);
                response = new InvocationResponse
                {
                    CorrelationId = correlationId,
                    HttpResponse = null!
                };
            }

            if (pendingRequestStore.TryComplete(response.CorrelationId ?? correlationId, response))
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
