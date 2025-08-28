namespace MangoFaaS.Firecracker.Node.Network;

using System.Net;

public record struct NetworkSetupEntry(IPAddress HostIp, IPAddress GuestIp, string TapDevice, string PoolName);