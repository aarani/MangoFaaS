using System.Collections;
using System.Net;
using MangoFaaS.IPAM.Exceptions;

namespace MangoFaaS.IPAM;

/// <summary>
/// Represents a single IPv4 pool carved from a CIDR block.
/// </summary>
internal sealed class IpPool
{
    private readonly object _sync = new();
    private readonly BitArray _allocated; // true means allocated
    private readonly HashSet<uint> _reserved;

    private int _nextSearchIndex; // simple pointer to speed up scan

    public string Name { get; }
    public uint Network { get; }
    public int Prefix { get; }
    public uint FirstUsable { get; }
    public uint LastUsable { get; }
    public int UsableCount { get; }

    public IpPool(string name, string cidr, IEnumerable<IPAddress>? reserved = null)
    {
        Name = name;
        (Network, Prefix) = CidrUtils.ParseCidr(cidr);

        // Determine usable host range
        var totalAddresses = Prefix == 32 ? 1 : 1u << (32 - Prefix);
        if (totalAddresses > int.MaxValue) throw new NotSupportedException("CIDR too large.");

        uint firstHost;
        uint lastHost;
        if (Prefix >= 31)
        {
            // /31 and /32: all addresses usable (RFC 3021 for /31)
            firstHost = Network;
            lastHost = Network + totalAddresses - 1;
        }
        else
        {
            firstHost = Network + 1; // skip network
            lastHost = Network + totalAddresses - 2; // skip broadcast
        }

        FirstUsable = firstHost;
        LastUsable = lastHost;
        UsableCount = (int)(LastUsable - FirstUsable + 1);
        _allocated = new BitArray(UsableCount, false);
        _reserved = reserved?.Select(CidrUtils.ToUint).ToHashSet() ?? [];

        // Mark reserved as allocated to exclude them.
        foreach (var r in _reserved)
        {
            if (r < FirstUsable || r > LastUsable) continue; // ignore if outside
            var index = (int)(r - FirstUsable);
            _allocated[index] = true;
        }
    }

    public IPAddress Allocate()
    {
        lock (_sync)
        {
            for (int i = 0; i < UsableCount; i++)
            {
                var idx = (_nextSearchIndex + i) % UsableCount;
                if (!_allocated[idx])
                {
                    _allocated[idx] = true;
                    _nextSearchIndex = (idx + 1) % UsableCount;
                    var ipUint = FirstUsable + (uint)idx;
                    return CidrUtils.ToIp(ipUint);
                }
            }
            throw new NoAvailableIpException(Name);
        }
    }

    public bool TryAllocate(out IPAddress? address)
    {
        lock (_sync)
        {
            for (int i = 0; i < UsableCount; i++)
            {
                var idx = (_nextSearchIndex + i) % UsableCount;
                if (!_allocated[idx])
                {
                    _allocated[idx] = true;
                    _nextSearchIndex = (idx + 1) % UsableCount;
                    address = CidrUtils.ToIp(FirstUsable + (uint)idx);
                    return true;
                }
            }
            address = null;
            return false;
        }
    }

    public void Release(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new InvalidIpReleaseException(Name, address, "Only IPv4 supported");
        var value = CidrUtils.ToUint(address);
        lock (_sync)
        {
            if (value < FirstUsable || value > LastUsable)
                throw new InvalidIpReleaseException(Name, address, "Address not inside pool");
            var index = (int)(value - FirstUsable);
            if (!_allocated[index])
                throw new InvalidIpReleaseException(Name, address, "Address was not allocated");
            if (_reserved.Contains(value))
                throw new InvalidIpReleaseException(Name, address, "Address is reserved and cannot be released (should not have been allocated)"
                );
            _allocated[index] = false;
            // Optionally adjust search index backwards for quicker reuse
            if (index < _nextSearchIndex) _nextSearchIndex = index;
        }
    }

    public int AllocatedCount
    {
        get
        {
            lock (_sync)
            {
                var count = 0;
                foreach (bool bit in _allocated)
                    if (bit) count++;
                return count;
            }
        }
    }

    public void Reserve(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new InvalidIpReleaseException(Name, address, "Only IPv4 supported");
        var value = CidrUtils.ToUint(address);
        lock (_sync)
        {
            if (value < FirstUsable || value > LastUsable)
                throw new InvalidIpReleaseException(Name, address, "Address not inside pool");
            var index = (int)(value - FirstUsable);
            _allocated[index] = true;
            _reserved.Add(value);
            if (index < _nextSearchIndex) _nextSearchIndex = index;
        }
    }

    public void ReleaseAll()
    {
        lock (_sync)
        {
            for (int i = 0; i < UsableCount; i++)
            {
                var ipUint = FirstUsable + (uint)i;
                if (_reserved.Contains(ipUint)) continue; // keep reserved allocated
                _allocated[i] = false;
            }
            _nextSearchIndex = 0;
        }
    }
}
