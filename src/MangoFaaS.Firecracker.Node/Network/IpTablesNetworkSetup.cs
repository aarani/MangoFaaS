using System.Security.Cryptography;
using MangoFaaS.Common.Services;
using MangoFaaS.IPAM;
using Microsoft.Extensions.Options;

namespace MangoFaaS.Firecracker.Node.Network;

public class IpTablesNetworkSetup : INetworkSetup
{
    private readonly ILogger<IpTablesNetworkSetup> _logger;
    private readonly ProcessExecutionService _executionService;
    private readonly IIpPoolManager _ipPoolManager;
    private readonly FirecrackerNetworkOptions _options;

    public IpTablesNetworkSetup(ILogger<IpTablesNetworkSetup> logger, ProcessExecutionService executionService, IIpPoolManager ipPoolManager, IOptions<FirecrackerNetworkOptions> options)
    {
        _logger = logger;
        _executionService = executionService;
        _ipPoolManager = ipPoolManager;
        _options = options.Value;
        
        ipPoolManager.AddPool("pool", _options.IpSubnet);
        // We need /30 subnets for each VM, 2 reserved, 1 Host, 1 Guest
        ipPoolManager.SplitIntoSubPools("pool", 30, false);
    }

    public async Task DestroyFirecrackerNetwork(NetworkSetupEntry entry)
    {
//        try { await _executionService.RunProcess("iptables-nft", $"-t nat -D POSTROUTING -o {_options.EgressInterface} -s {entry.GuestIp} -j MASQUERADE"); } catch { /* ignored */ }
//        try { await _executionService.RunProcess("iptables-nft", $"-D FORWARD -i {entry.TapDevice} -o {_options.EgressInterface} -j ACCEPT"); } catch { /* ignored */ }
        try { await _executionService.RunProcess("ip", $"link del {entry.TapDevice}", CancellationToken.None); } catch { /* ignored */ }
        try { _ipPoolManager.ReleaseAll(entry.PoolName); } catch { /* ignored */ }
    }

    public async Task Initialize()
    {
        // Allow new outbound connections from any tap* interface to the egress interface
        await _executionService.RunProcess("iptables-nft", $"-A FORWARD -i tap+ -o \"{_options.EgressInterface}\" -m conntrack --ctstate NEW -j ACCEPT");
        // Allow return traffic
        await _executionService.RunProcess("iptables-nft", "-A FORWARD -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT");
        try { await _executionService.RunProcess("iptables-nft", $"-t nat -D POSTROUTING -o \"{_options.EgressInterface}\" -j MASQUERADE"); } catch { /* ignored */ }
        await _executionService.RunProcess("iptables-nft", $"-t nat -A POSTROUTING -o \"{_options.EgressInterface}\" -j MASQUERADE");
    }

    public async Task<NetworkSetupEntry> SetupFirecrackerNetwork(int processId, CancellationToken cancellationToken = default)
    {
        var freePool = _ipPoolManager.FindAvailablePool(minFree: 2)
            ?? throw new InvalidOperationException("No free IP subnets available for Firecracker VM");
        var hostIp = _ipPoolManager.Allocate(freePool);
        var guestIp = _ipPoolManager.Allocate(freePool);
        var tapId = $"tap{processId}-{RandomNumberGenerator.GetInt32(1000)}";
        _logger.LogInformation("Firecracker[{Id}] DEBUG: Assigning host ip {hostIp}, guest ip {guestIp} on tap device {tapId}", processId, hostIp, guestIp, tapId);

        await _executionService.RunProcess("ip", $"tuntap add {tapId} mode tap", cancellationToken);
        await _executionService.RunProcess("ip", $"addr add {hostIp}/30 dev {tapId}", cancellationToken);
        await _executionService.RunProcess("ip", $"link set {tapId} up", cancellationToken);


        //await _executionService.RunProcess("iptables-nft", $"-t nat -D POSTROUTING -o \"{_options.EgressInterface}\" -j MASQUERADE");

        //        await _executionService.RunProcess("iptables-nft", $"-t nat -A POSTROUTING -o {_options.EgressInterface} -s {guestIp} -j MASQUERADE", cancellationToken);
        //        await _executionService.RunProcess("iptables-nft", $"-A FORWARD -i {tapId} -o {_options.EgressInterface} -j ACCEPT", cancellationToken);

        return new NetworkSetupEntry(hostIp, guestIp, tapId, freePool);
    }
}