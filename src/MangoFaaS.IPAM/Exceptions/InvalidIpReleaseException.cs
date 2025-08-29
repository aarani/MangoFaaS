using System.Net;

namespace MangoFaaS.IPAM.Exceptions;

public class InvalidIpReleaseException : IpamException
{
    public InvalidIpReleaseException(string poolName, IPAddress address, string reason)
        : base($"Cannot release IP {address} to pool '{poolName}': {reason}.") {}
}
