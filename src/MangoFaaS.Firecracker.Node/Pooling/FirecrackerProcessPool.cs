using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using MangoFaaS.Common.Services;
using MangoFaaS.Firecracker.Node.Kestrel;
using MangoFaaS.Firecracker.Node.Network;
using MangoFaaS.Firecracker.Node.Pooling;
using MangoFaaS.IPAM;
using Microsoft.Extensions.Options;

namespace MangoFaaS.Firecracker.Node.Pooling;

//FIXME: InUse is not thread-safe; consider using locks if needed
public sealed class FirecrackerProcessPool: IHostedService, IFirecrackerProcessPool, IAsyncDisposable
{
    private readonly FirecrackerPoolOptions _options;
    private readonly ILogger<FirecrackerProcessPool> _logger;
    private readonly INetworkSetup _networkSetup;
    private readonly ConcurrentDictionary<string, FirecrackerProcessHandle> _all = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<FirecrackerProcessHandle>> _idle = new();
    private readonly UnixKestrelSetup _kestrelSetup;

    public FirecrackerProcessPool(IOptions<FirecrackerPoolOptions> options, ILogger<FirecrackerProcessPool> logger, INetworkSetup networkSetup, UnixKestrelSetup kestrelSetup)
    {
        _options = options.Value;
        _logger = logger;
        _networkSetup = networkSetup;
        _kestrelSetup = kestrelSetup;
    }

    public async Task<FirecrackerLease> AcquireAsync(string functionId, CancellationToken cancellationToken = default)
    {
        // Function name is empty, this is used when prewarming or no specific function is known
        if (string.IsNullOrWhiteSpace(functionId))
        {
            throw new Exception("Function ID must be provided");
        }

        // Fast path: reuse an idle process
        while (true)
        {
            // Really fast path, check if we have VM with the same function id
            if (_idle.TryGetValue(functionId, out var queue) && queue.TryDequeue(out var handle))
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
                    genericHandle.LastFunctionHash = functionId;
                    return new FirecrackerLease(this, genericHandle, false);
                }
                // Drop unhealthy/exited; continue loop
                await RemoveHandle(genericHandle);
                continue;
            }

            // No idle: we can create a new one
            var newHandle = await StartNewAsync(functionId, cancellationToken).ConfigureAwait(false);
            newHandle.InUse = true;
            newHandle.LastUsed = DateTimeOffset.UtcNow;
            newHandle.LastFunctionHash = functionId;
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

    private async Task<FirecrackerProcessHandle> StartNewAsync(string functionId, CancellationToken cancellationToken)
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
        var args = $"{_options.DefaultArgs ?? string.Empty} --api-sock {apiSock}";

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
            NetworkEntry = await _networkSetup.SetupFirecrackerNetwork(p.Id, cancellationToken),
            KestrelEntry = null!,
            LastUsed = DateTimeOffset.UtcNow,
            InUse = false
        };

        var client = handle.CreateClient();

        await client.NetworkInterfaces["net1"].PutAsync(new API.Models.NetworkInterface
        {
            IfaceId = "net1",
            HostDevName = handle.NetworkEntry.TapDevice
        }, cancellationToken: cancellationToken);

        var udsPath = Path.Combine(workDir, $"v-{id}.sock");

        await client.Vsock.PutAsync(new API.Models.Vsock() { GuestCid = 3, UdsPath = $"./v-{id}.sock" }, cancellationToken: cancellationToken);
        _logger.LogInformation("Configured vsock (CID 3) with UDS path {Path}", udsPath);

        handle.KestrelEntry = await _kestrelSetup.StartListening(udsPath, functionId);

        _all[handle.Id] = handle;
        _logger.LogInformation("Started firecracker process {Id} (PID {Pid}) with API socket {Sock}", handle.Id, p.Id, apiSock);
        return handle;
    }

    private async Task RemoveHandle(FirecrackerProcessHandle handle)
    {
        _all.TryRemove(handle.Id, out _);
        await TryKill(handle);
    }

    private async Task TryKill(FirecrackerProcessHandle handle)
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
            try { await handle.KestrelEntry.Server.StopAsync(default); } catch { /* ignored */ }
            try { await _networkSetup.DestroyFirecrackerNetwork(handle.NetworkEntry); } catch { /* ignored */ }
            try { File.Delete(handle.ApiSocketPath); } catch { /* ignored */ }
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
        await _networkSetup.Initialize();
        _ = Task.Run(() => ExecuteAsync(cancellationToken), cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisposeAsync();
    }
}
