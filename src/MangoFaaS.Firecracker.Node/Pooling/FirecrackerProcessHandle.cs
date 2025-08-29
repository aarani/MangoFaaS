using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using MangoFaaS.Firecracker.API;
using MangoFaaS.Firecracker.Node.Kestrel;
using MangoFaaS.Firecracker.Node.Network;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace MangoFaaS.Firecracker.Node.Pooling;

internal sealed class FirecrackerProcessHandle
{
    public required string Id { get; init; }
    public required Process Process { get; init; }
    public required string ApiSocketPath { get; init; }
    public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
    public string LastFunctionHash { get; set; } = string.Empty;
    public NetworkSetupEntry NetworkEntry { get; internal set; }
    public required UnixKestrelEntry KestrelEntry { get; internal set; }
    public required List<IDisposable> Disposables { get; internal set; }

    public volatile bool InUse;

    public FirecrackerClient CreateClient()
    {
        var httpHandler = new SocketsHttpHandler
        {
            // Called to open a new connection
            ConnectCallback = async (ctx, ct) =>
            {
                var socketPath = ApiSocketPath ?? throw new InvalidOperationException("No API socket path");
                // Define the type of socket we want, i.e. a UDS stream-oriented socket
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);

                // Create a UDS endpoint using the socket path
                var endpoint = new UnixDomainSocketEndPoint(socketPath);

                // Connect to the server!
                await socket.ConnectAsync(endpoint, ct);

                // Wrap the socket in a NetworkStream and return it
                // Setting ownsSocket: true means the NetworkStream will 
                // close and dispose the Socket when the stream is disposed
                return new NetworkStream(socket, ownsSocket: true);
            }
        };

        var httpClient = new HttpClient(httpHandler)
        {
            BaseAddress = new Uri("http://localhost")
        };


        var authProvider = new AnonymousAuthenticationProvider();
        var adapter = new HttpClientRequestAdapter(authProvider, httpClient: httpClient);
        var client = new FirecrackerClient(adapter);

        return client;
    }
}
