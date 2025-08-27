using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using MangoFaaS.Firecracker.API;
using MangoFaaS.IPAM;
using Microsoft.Extensions.Options;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;

namespace MangoFaaS.Firecracker.Node;

public interface IFirecrackerProcessPool
{
    Task<FirecrackerLease> AcquireAsync(string functionHash, CancellationToken cancellationToken = default);
}

public sealed class FirecrackerLease : IAsyncDisposable
{
    private readonly FirecrackerProcessPool _pool;
    internal readonly FirecrackerProcessHandle Handle;

    internal FirecrackerLease(FirecrackerProcessPool pool, FirecrackerProcessHandle handle, bool isWarm)
    {
        _pool = pool;
        Handle = handle;
        IsWarm = isWarm;
    }

    public Process Process => Handle.Process;
    public string ApiSocketPath => Handle.ApiSocketPath;
    public bool IsWarm { get; private set; }
    public FirecrackerClient CreateClient() => Handle.CreateClient();
    public async ValueTask DisposeAsync()
    {
        await _pool.Release(Handle);
    }
    public async Task MarkAsUnusable(){
        await _pool.Release(Handle, true);
    }
}

internal sealed class FirecrackerProcessHandle
{
    public required string Id { get; init; }
    public required Process Process { get; init; }
    public required string ApiSocketPath { get; init; }
    public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
    public string LastFunctionHash { get; set; } = string.Empty;
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

//FIXME: InUse is not thread-safe; consider using locks if needed
public sealed class FirecrackerProcessPool: IHostedService, IFirecrackerProcessPool, IAsyncDisposable
{
    private readonly FirecrackerPoolOptions _options;
    private readonly ILogger<FirecrackerProcessPool> _logger;

    private readonly ConcurrentDictionary<string, FirecrackerProcessHandle> _all = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<FirecrackerProcessHandle>> _idle = new();

    private readonly IIpPoolManager _ipPoolManager = new IpPoolManager();

    public FirecrackerProcessPool(IOptions<FirecrackerPoolOptions> options, ILogger<FirecrackerProcessPool> logger)
    {
        _options = options.Value;
        _logger = logger;

        _ipPoolManager.AddPool("pool", _options.IpSubnet);
        _ipPoolManager.SplitIntoSubPools("pool", 30, false);
    }

    public async Task<FirecrackerLease> AcquireAsync(string functionHash = "", CancellationToken cancellationToken = default)
    {
        // Function name is empty, this is used when prewarming or no specific function is known
        if (string.IsNullOrWhiteSpace(functionHash))
        {
            var newHandle = await StartNewAsync(cancellationToken).ConfigureAwait(false);
            newHandle.InUse = true;
            newHandle.LastUsed = DateTimeOffset.UtcNow;
            return new FirecrackerLease(this, newHandle, false);
        }

        // Fast path: reuse an idle process
        while (true)
        {
            // Really fast path, check if we have VM with the same function id
            if (_idle.TryGetValue(functionHash, out var queue) && queue.TryDequeue(out var handle))
            {
                if (IsHealthy(handle))
                {
                    handle.InUse = true;
                    handle.LastUsed = DateTimeOffset.UtcNow;
                    return new FirecrackerLease(this, handle, true);
                }
                // Drop unhealthy/exited; continue loop
                await RemoveHandle(handle);
                continue;
            }

            // Somewhat fast path: reuse any idle generic process
            if (_idle.TryGetValue(string.Empty, out var genericIdles) && genericIdles.TryDequeue(out var genericHandle))
            {
                if (IsHealthy(genericHandle))
                {
                    genericHandle.InUse = true;
                    genericHandle.LastUsed = DateTimeOffset.UtcNow;
                    genericHandle.LastFunctionHash = functionHash;
                    return new FirecrackerLease(this, genericHandle, false);
                }
                // Drop unhealthy/exited; continue loop
                await RemoveHandle(genericHandle);
                continue;
            }

            // No idle: we can create a new one
            var newHandle = await StartNewAsync(cancellationToken).ConfigureAwait(false);
            newHandle.InUse = true;
            newHandle.LastUsed = DateTimeOffset.UtcNow;
            newHandle.LastFunctionHash = functionHash;
            return new FirecrackerLease(this, newHandle, false);
        }
    }

    internal async Task Release(FirecrackerProcessHandle handle, bool kill = false)
    {
        if (kill || !IsHealthy(handle))
        {
            await RemoveHandle(handle);
            return;
        }

        handle.InUse = false;
        handle.LastUsed = DateTimeOffset.UtcNow;
        if (!_idle.TryAdd(handle.LastFunctionHash, new ConcurrentQueue<FirecrackerProcessHandle>([handle])))
            _idle[handle.LastFunctionHash].Enqueue(handle);
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Background cleanup loop
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, (int)_options.IdleTimeout.TotalSeconds / 2)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await CleanupIdle();
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        finally
        {
            timer.Dispose();
        }
    }

    private async Task CleanupIdle()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _all)
        {
            var h = kv.Value;
            // We don't kill "generic" idle processes, only those tied to a specific function
            if (!string.IsNullOrEmpty(h.LastFunctionHash) && !h.InUse && now - h.LastUsed > _options.IdleTimeout)
            {
                if (_all.TryRemove(h.Id, out _))
                {
                    await TryKill(h);
                }
            }
        }

        foreach (var (key, idles) in _idle)
        {
            if (string.IsNullOrEmpty(key)) continue; // skip generic idles
            
            // Drain idle queue of dead processes
            var temp = new List<FirecrackerProcessHandle>();
            while (idles.TryDequeue(out var h2))
            {
                if (IsHealthy(h2) && _all.ContainsKey(h2.Id)) temp.Add(h2);
                else await TryKill(h2);
            }
            foreach (var h in temp) idles.Enqueue(h);
        }
    }

    private bool IsHealthy(FirecrackerProcessHandle handle)
    {
        try
        {
            return handle.Process is { HasExited: false };
        }
        catch
        {
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is Process p)
        {
            foreach (var kv in _all)
            {
                if (kv.Value.Process == p)
                {
                    _all.TryRemove(kv.Key, out _);
                    break;
                }
            }
        }
    }

    private async Task<FirecrackerProcessHandle> StartNewAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_options.FirecrackerPath))
        {
            throw new FileNotFoundException($"Firecracker binary not found at '{_options.FirecrackerPath}'");
        }

        // Each process typically needs a unique API socket; here we create a temp folder for isolation
        var workDir = _options.WorkingDirectory ?? Path.Combine(Path.GetTempPath(), "mangofaas-fc");
        Directory.CreateDirectory(workDir);
        var id = Guid.NewGuid().ToString("n");
        var apiSock = Path.Combine(workDir, $"fc-{id}.sock");
        // Build args; caller can override via DefaultArgs if needed
        var args = _options.DefaultArgs;
        if (string.IsNullOrWhiteSpace(args))
        {
            // Minimal: set api socket; the caller will configure VM via API after acquisition
            args = $"--api-sock {apiSock}";
        }

        var si = new ProcessStartInfo
        {
            FileName = _options.FirecrackerPath,
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var p = new Process { StartInfo = si, EnableRaisingEvents = true };
        p.Exited += OnProcessExited;
        p.OutputDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogInformation("Firecracker[{Id}] OUT: {Data}", id, e.Data);
        };
        p.ErrorDataReceived += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogError("Firecracker[{Id}] ERR: {Data}", id, e.Data);
        };


        if (!p.Start())
        {
            throw new InvalidOperationException("Failed to start firecracker process");
        }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        // Optionally, wait a bit for the API socket to become available
        var startedAt = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.StartupTimeoutSeconds));
        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(apiSock)) break;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        var handle = new FirecrackerProcessHandle
        {
            Id = id,
            Process = p,
            ApiSocketPath = apiSock,
            LastUsed = DateTimeOffset.UtcNow,
            InUse = false
        };

        _all[handle.Id] = handle;
        _logger.LogInformation("Started firecracker process {Id} (PID {Pid}) with API socket {Sock}", handle.Id, p.Id, apiSock);
        return handle;
    }

    private async Task RemoveHandle(FirecrackerProcessHandle handle)
    {
        _all.TryRemove(handle.Id, out _);
        await TryKill(handle);
    }

    private static async Task TryKill(FirecrackerProcessHandle handle)
    {
        try
        {
            if (handle.Process.HasExited) return;
            handle.Process.Kill(entireProcessTree: true);
            await handle.Process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
        }
        catch
        {
            // ignore
        }
        finally
        {
            try { handle.Process.Dispose(); } catch { /* ignore */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Best-effort cleanup
        foreach (var h in _all.Values)
        {
            await TryKill(h);
        }
        _all.Clear();
        foreach (var q in _idle.Values)
            q.Clear();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _ = ExecuteAsync(cancellationToken);
        for (var i = 0; i < _options.PrepareCount; i++) // prewarm
        {
            try
            {
                var handle = await AcquireAsync("", cancellationToken);
                await handle.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to prewarm firecracker process: {Message}", ex.Message);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync();
    }
}
