using System.Security.Cryptography;
using MangoFaaS.Firecracker.Node.Services;
using MangoFaaS.Firecracker.Node.Store;
using MangoFaaS.Models;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Options;

namespace MangoFaaS.Firecracker.Node.Kestrel;

public record UnixKestrelEntry(KestrelServer Server, string UdsPath);

public class UnixKestrelSetup (ILoggerFactory loggerFactory, PendingRequestStore pendingRequestStore, IServiceProvider serviceProvider)
{
 
    public async Task<UnixKestrelEntry> StartListening(string socketPath, string functionIdWithVersion)
    {
        var options = new OptionsWrapper<KestrelServerOptions>(new ());
        options.Value.ListenUnixSocket(socketPath + "_80");

        var transportOptions = new OptionsWrapper<SocketTransportOptions>(new ());

        var transportFactory = new SocketTransportFactory(transportOptions, loggerFactory);

        var server = new KestrelServer(options, transportFactory, loggerFactory);
        await server.StartAsync(ActivatorUtilities.CreateInstance<PendingRequestServerApplication>(serviceProvider, functionIdWithVersion, pendingRequestStore), CancellationToken.None);

        return new UnixKestrelEntry(server, socketPath);
    }
}