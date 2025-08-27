using System.Diagnostics;
using MangoFaaS.Firecracker.API;

namespace MangoFaaS.Firecracker.Node.Pooling;

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
