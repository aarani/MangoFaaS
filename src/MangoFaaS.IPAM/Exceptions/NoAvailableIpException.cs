namespace MangoFaaS.IPAM.Exceptions;

public class NoAvailableIpException : IpamException
{
    public NoAvailableIpException(string poolName)
        : base($"No available IP addresses in pool '{poolName}'.") {}
}
