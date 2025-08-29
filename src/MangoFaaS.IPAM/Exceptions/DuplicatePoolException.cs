namespace MangoFaaS.IPAM.Exceptions;

public class DuplicatePoolException : IpamException
{
    public DuplicatePoolException(string poolName)
        : base($"IP pool '{poolName}' already exists.") {}
}
