using System.Net;
using MangoFaaS.IPAM.Exceptions;
using Xunit;

namespace MangoFaaS.IPAM.Tests;

public class IpPoolManagerTests
{
    private readonly IpPoolManager _mgr = new();

    [Fact]
    public void AllocateSequential_InSmallCidr_SkipsNetworkAndBroadcast()
    {
        _mgr.AddPool("p1", "10.0.0.0/30"); // .0 net, .1 .2 usable, .3 broadcast
        var a1 = _mgr.Allocate("p1");
        var a2 = _mgr.Allocate("p1");
        Assert.Equal(IPAddress.Parse("10.0.0.1"), a1);
        Assert.Equal(IPAddress.Parse("10.0.0.2"), a2);
        Assert.Throws<NoAvailableIpException>(() => _mgr.Allocate("p1"));
    }

    [Fact]
    public void TryAllocate_ReturnsFalse_WhenExhausted()
    {
        _mgr.AddPool("p2", "10.0.1.0/30");
        Assert.True(_mgr.TryAllocate("p2", out var a1));
        Assert.True(_mgr.TryAllocate("p2", out var a2));
        Assert.NotNull(a1);
        Assert.NotNull(a2);
        Assert.False(_mgr.TryAllocate("p2", out var a3));
        Assert.Null(a3);
    }

    [Fact]
    public void Release_MakesAddressReusable()
    {
        _mgr.AddPool("p3", "10.0.2.0/30");
        var ip = _mgr.Allocate("p3");
        _mgr.Release("p3", ip);
        var again = _mgr.Allocate("p3");
        Assert.Equal(ip, again); // should reuse the released address first due to search index adjustment
    }

    [Fact]
    public void ReservedAddress_IsNotAllocated_AndReflectedInStats()
    {
        var reserved = IPAddress.Parse("10.0.3.1");
        _mgr.AddPool("p4", "10.0.3.0/30", new[]{ reserved }); // usable .1 .2, reserve .1
        var statsBefore = _mgr.GetStats("p4");
        Assert.Equal(2, statsBefore.TotalUsable); // two usable in /30
        Assert.Equal(1, statsBefore.Allocated); // reserved counted as allocated
        Assert.Equal(1, statsBefore.Free);
        var allocated = _mgr.Allocate("p4");
        Assert.Equal(IPAddress.Parse("10.0.3.2"), allocated); // should skip reserved .1
        var statsAfter = _mgr.GetStats("p4");
        Assert.Equal(2, statsAfter.Allocated); // reserved + allocated
        Assert.Equal(0, statsAfter.Free);
    }

    [Fact]
    public void Cidr31_AllAddressesUsable()
    {
        _mgr.AddPool("p5", "10.0.4.0/31"); // .0 and .1 usable
        var a1 = _mgr.Allocate("p5");
        var a2 = _mgr.Allocate("p5");
        var set = new HashSet<string>{ a1.ToString(), a2.ToString() };
        Assert.Contains("10.0.4.0", set);
        Assert.Contains("10.0.4.1", set);
        Assert.Throws<NoAvailableIpException>(() => _mgr.Allocate("p5"));
    }

    [Fact]
    public void Release_NotAllocated_Throws()
    {
        _mgr.AddPool("p6", "10.0.5.0/30");
        var ip = IPAddress.Parse("10.0.5.1");
        // releasing without allocation should fail
        Assert.Throws<InvalidIpReleaseException>(() => _mgr.Release("p6", ip));
    }

    [Fact]
    public void Reserve_AddsReservedAndPreventsReuse()
    {
        _mgr.AddPool("p7", "10.0.6.0/30");
        var reserveIp = IPAddress.Parse("10.0.6.1");
        _mgr.Reserve("p7", reserveIp);
        // allocate should skip reserved and allocate the other usable .2
        var alloc = _mgr.Allocate("p7");
        Assert.Equal(IPAddress.Parse("10.0.6.2"), alloc);
        Assert.Throws<NoAvailableIpException>(() => _mgr.Allocate("p7"));
    }

    [Fact]
    public void SplitIntoSubPools_Creates30sAndRemovesParent()
    {
        _mgr.AddPool("big", "10.0.8.0/24");
        var subs = _mgr.SplitIntoSubPools("big", 30);
        Assert.NotEmpty(subs);
        // Each /30 has 4 addresses -> 256 /30 = 64 subnets in /24
        Assert.Equal(64, subs.Count);
        // parent gone
        Assert.DoesNotContain("big", _mgr.ListPools());
        // allocate from one sub pool to ensure it's functional
        var firstSub = subs.First();
        var ip = _mgr.Allocate(firstSub);
        Assert.NotNull(ip);
    }

    [Fact]
    public void FindAvailablePool_ReturnsPoolWithCapacity()
    {
        _mgr.AddPool("fa1", "10.0.9.0/30");
        _mgr.AddPool("fa2", "10.0.10.0/30");
        // exhaust fa1
        _mgr.Allocate("fa1");
        _mgr.Allocate("fa1");
        var name2 = _mgr.FindAvailablePool(minFree:2); // fa2 still has 2 free
        Assert.Equal("fa2", name2);
        var name1 = _mgr.FindAvailablePool(); // any pool with >=1 free => fa2
        Assert.Equal("fa2", name1);
    }

    [Fact]
    public void ReleaseAll_FreesNonReserved()
    {
        _mgr.AddPool("ra", "10.0.11.0/30");
        var r = IPAddress.Parse("10.0.11.1");
        _mgr.Reserve("ra", r);
        var a = _mgr.Allocate("ra"); // should be .2
        Assert.Equal(IPAddress.Parse("10.0.11.2"), a);
        var statsBefore = _mgr.GetStats("ra");
        Assert.Equal(2, statsBefore.Allocated); // reserved + allocated
        _mgr.ReleaseAll("ra");
        var statsAfter = _mgr.GetStats("ra");
        Assert.Equal(1, statsAfter.Allocated); // reserved remains
        Assert.Equal(1, statsAfter.Free);
        // allocate again gets the freed .2
        var again = _mgr.Allocate("ra");
        Assert.Equal(IPAddress.Parse("10.0.11.2"), again);
    }
}
