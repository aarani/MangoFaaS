using System.Collections.Concurrent;
using System.Net;
using MangoFaaS.IPAM.Exceptions;
using MangoFaaS.IPAM.Models;

namespace MangoFaaS.IPAM;

/// <summary>
/// Thread-safe manager orchestrating multiple named IP pools.
/// </summary>
public sealed class IpPoolManager : IIpPoolManager
{
    private readonly ConcurrentDictionary<string, IpPool> _pools = new(StringComparer.OrdinalIgnoreCase);

    public void AddPool(string poolName, string cidr, IEnumerable<IPAddress>? reserved = null)
    {
        var pool = new IpPool(poolName, cidr, reserved);
        if (!_pools.TryAdd(poolName, pool))
            throw new DuplicatePoolException(poolName);
    }

    public IPAddress Allocate(string poolName)
    {
        var pool = GetPool(poolName);
        return pool.Allocate();
    }

    public bool TryAllocate(string poolName, out IPAddress? address)
    {
        var pool = GetPool(poolName);
        return pool.TryAllocate(out address);
    }

    public void Release(string poolName, IPAddress address)
    {
        var pool = GetPool(poolName);
        pool.Release(address);
    }

    public IpPoolStats GetStats(string poolName)
    {
        var pool = GetPool(poolName);
        var allocated = pool.AllocatedCount;
        return new IpPoolStats(
            poolName,
            CidrUtils.ToIp(pool.Network),
            pool.Prefix,
            pool.UsableCount,
            allocated,
            pool.UsableCount - allocated);
    }

    public IReadOnlyCollection<string> ListPools() => _pools.Keys.ToArray();

    public void Reserve(string poolName, IPAddress address)
    {
        var pool = GetPool(poolName);
        pool.Reserve(address);
    }

    public IReadOnlyCollection<string> SplitIntoSubPools(string poolName, int subPrefix = 30, bool keepParent = false)
    {
        var parent = GetPool(poolName);
        if (subPrefix <= parent.Prefix)
            throw new ArgumentException("Child prefix must be larger (more specific) than parent prefix", nameof(subPrefix));
        if (subPrefix > 30)
            throw new NotSupportedException("Splitting currently only intended for /30 to maximize small pools.");

        // compute child block size
        var childBlockSize = 1u << (32 - subPrefix);
        var parentBlockSize = 1u << (32 - parent.Prefix);
        var mask = CidrUtils.PrefixToMask(parent.Prefix);

        var created = new List<string>();
        for (uint offset = 0; offset < parentBlockSize; offset += childBlockSize)
        {
            var childNet = (parent.Network & mask) + offset;
            var childName = $"{poolName}-{CidrUtils.ToIp(childNet)}/{subPrefix}";
            // Avoid duplicate names if already exist
            if (_pools.ContainsKey(childName)) continue;
            var childCidr = $"{CidrUtils.ToIp(childNet)}/{subPrefix}";
            var pool = new IpPool(childName, childCidr);
            if (_pools.TryAdd(childName, pool))
                created.Add(childName);
        }

        if (!keepParent)
        {
            _pools.TryRemove(poolName, out _);
        }

        return created;
    }

    private IpPool GetPool(string poolName)
        => _pools.TryGetValue(poolName, out var pool) ? pool : throw new PoolNotFoundException(poolName);

    public string? FindAvailablePool(int minFree = 1)
    {
        if (minFree < 1) minFree = 1;
        foreach (var kvp in _pools)
        {
            var p = kvp.Value;
            var allocated = p.AllocatedCount;
            var free = p.UsableCount - allocated;
            if (free >= minFree) return kvp.Key;
        }
        return null;
    }

    public void ReleaseAll(string poolName)
    {
        var pool = GetPool(poolName);
        pool.ReleaseAll();
    }
}
