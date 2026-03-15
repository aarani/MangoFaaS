namespace MangoFaaS.Firecracker.Node.Pooling;

public interface IFirecrackerProcessPool
{
    Task<FirecrackerLease> AcquireAsync(string functionHash, CancellationToken cancellationToken = default);
}
