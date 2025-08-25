using System.Net;
using MangoFaaS.IPAM.Models;

namespace MangoFaaS.IPAM;

/// <summary>
/// Contract for IP pool management (allocation / release / stats).
/// </summary>
public interface IIpPoolManager
{
    /// <summary>Add a new pool identified by name using a CIDR block.</summary>
    /// <param name="poolName">Unique pool name.</param>
    /// <param name="cidr">CIDR notation (IPv4).</param>
    /// <param name="reserved">Optional reserved addresses that will not be allocated.</param>
    void AddPool(string poolName, string cidr, IEnumerable<IPAddress>? reserved = null);

    /// <summary>Allocate the next available IP from the pool.</summary>
    IPAddress Allocate(string poolName);

    /// <summary>Release a previously allocated IP back to the pool.</summary>
    void Release(string poolName, IPAddress address);

    /// <summary>Attempt allocation without throwing if pool exhausted.</summary>
    bool TryAllocate(string poolName, out IPAddress? address);

    /// <summary>Get usage statistics for a pool.</summary>
    IpPoolStats GetStats(string poolName);

    /// <summary>List all pool names.</summary>
    IReadOnlyCollection<string> ListPools();

    /// <summary>Reserve a specific IP in a pool (marks as allocated and reserved).</summary>
    void Reserve(string poolName, IPAddress address);

    /// <summary>
    /// Split an existing pool into smaller sub-pools (e.g. /30). The original pool is removed by default.
    /// Returns the names of the created sub-pools.
    /// </summary>
    /// <param name="poolName">Existing parent pool name.</param>
    /// <param name="subPrefix">Target child prefix (default 30).</param>
    /// <param name="keepParent">If true keep the parent pool; allocations across parent & children are NOT coordinated (use with caution).</param>
    IReadOnlyCollection<string> SplitIntoSubPools(string poolName, int subPrefix = 30, bool keepParent = false);

    /// <summary>
    /// Returns the name of any pool with at least the specified number of free IPs, or null if none.
    /// </summary>
    /// <param name="minFree">Minimum free addresses required (default 1).</param>
    string? FindAvailablePool(int minFree = 1);

    /// <summary>
    /// Releases (frees) every non-reserved allocated IP in the specified pool.
    /// Reserved addresses remain reserved/allocated.
    /// </summary>
    void ReleaseAll(string poolName);
}
