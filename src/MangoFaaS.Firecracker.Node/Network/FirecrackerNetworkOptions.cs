
namespace MangoFaaS.Firecracker.Node.Network;

public class FirecrackerNetworkOptions
{
    // IP Subnet for allocation
    public string IpSubnet { get; set; } = "172.16.0.0/16";

    // Name of the egress interface used for NAT (defaults to eth0 if null)
    public string EgressInterface { get; set; } = "eth0";
}
