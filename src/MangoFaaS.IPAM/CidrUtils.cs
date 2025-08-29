using System.Net;
using System.Numerics;

namespace MangoFaaS.IPAM;

internal static class CidrUtils
{
    public static (uint network, int prefix) ParseCidr(string cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) throw new ArgumentException("CIDR must be provided", nameof(cidr));
        var parts = cidr.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) throw new FormatException($"Invalid CIDR '{cidr}'.");
        if (!IPAddress.TryParse(parts[0], out var ip)) throw new FormatException($"Invalid IP in CIDR '{cidr}'.");
        if (!int.TryParse(parts[1], out var prefix) || prefix is < 0 or > 32) throw new FormatException($"Invalid prefix in CIDR '{cidr}'.");
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new NotSupportedException("Only IPv4 CIDRs are supported currently.");
        var networkUint = ToUint(ip) & PrefixToMask(prefix);
        return (networkUint, prefix);
    }

    public static IPAddress ToIp(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return new IPAddress(bytes);
    }

    public static uint ToUint(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) throw new ArgumentException("Only IPv4 supported", nameof(ip));
        return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
    }

    public static uint PrefixToMask(int prefix)
    {
        if (prefix == 0) return 0u;
        return uint.MaxValue << (32 - prefix);
    }
}
