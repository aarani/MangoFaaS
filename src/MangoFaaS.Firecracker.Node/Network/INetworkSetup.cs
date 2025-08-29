namespace MangoFaaS.Firecracker.Node.Network;

public interface INetworkSetup
{
    Task Initialize();
    Task<NetworkSetupEntry> SetupFirecrackerNetwork(int processId, CancellationToken cancellationToken = default);
    Task DestroyFirecrackerNetwork(NetworkSetupEntry entry);
}
