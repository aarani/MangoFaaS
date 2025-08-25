using System.Net;

namespace MangoFaaS.IPAM.Models;

/// <summary>
/// Represents usage statistics of an IP pool.
/// </summary>
public sealed record IpPoolStats(
    string PoolName,
    IPAddress Network,
    int PrefixLength,
    int TotalUsable,
    int Allocated,
    int Free)
{
    public double Utilization => TotalUsable == 0 ? 0 : (double)Allocated / TotalUsable;
}
