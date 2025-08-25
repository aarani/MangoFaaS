namespace MangoFaaS.IPAM.Exceptions;

/// <summary>
/// Base exception type for the IPAM library.
/// </summary>
public class IpamException : Exception
{
    public IpamException(string message) : base(message) {}
    public IpamException(string message, Exception inner) : base(message, inner) {}
}
