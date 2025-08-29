namespace MangoFaaS.IPAM.Exceptions;

public class PoolNotFoundException : IpamException
{
    public PoolNotFoundException(string poolName)
        : base($"IP pool '{poolName}' was not found.") {}
}
