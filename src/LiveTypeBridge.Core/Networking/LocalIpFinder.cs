using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LiveTypeBridge.Core.Networking;

public sealed record IpCandidate(string Address, string InterfaceName, bool IsPreferred);

/// <summary>
/// Picks the IPv4 address the phone should connect to. Ignores loopback, link-local,
/// tunnels and known virtual/VPN adapters; prefers interfaces that have a real gateway
/// (i.e., the actual Wi-Fi/Ethernet path to the LAN).
/// </summary>
public static class LocalIpFinder
{
    private static readonly string[] VirtualNameHints =
    {
        "virtual", "vmware", "virtualbox", "vethernet", "wsl", "hyper-v", "container",
        "tailscale", "zerotier", "hamachi", "openvpn", " tap", "loopback", "bluetooth",
        "pseudo", "ppp", "wireguard",
    };

    public static List<IpCandidate> GetCandidates()
    {
        var result = new List<IpCandidate>();
        NetworkInterface[] adapters;
        try { adapters = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (var nic in adapters)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

            var description = (nic.Description + " " + nic.Name).ToLowerInvariant();
            var isVirtual = VirtualNameHints.Any(h => description.Contains(h));

            bool hasGateway;
            try
            {
                hasGateway = nic.GetIPProperties().GatewayAddresses
                    .Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                           && !g.Address.Equals(IPAddress.Any));
            }
            catch (NetworkInformationException) { hasGateway = false; }

            var isWifiOrEthernet = nic.NetworkInterfaceType is NetworkInterfaceType.Wireless80211
                                                              or NetworkInterfaceType.Ethernet
                                                              or NetworkInterfaceType.GigabitEthernet;

            List<UnicastIPAddressInformation> addrs;
            try { addrs = nic.GetIPProperties().UnicastAddresses.ToList(); }
            catch (NetworkInformationException) { continue; }

            foreach (var ua in addrs)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IPAddress.IsLoopback(ua.Address)) continue;
                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue; // APIPA / unplugged cable

                result.Add(new IpCandidate(ip, nic.Name, !isVirtual && (hasGateway || isWifiOrEthernet)));
            }
        }

        return result
            .OrderByDescending(c => c.IsPreferred)
            .ThenBy(c => c.Address, StringComparer.Ordinal)
            .ToList();
    }

    public static IpCandidate? GetPrimary() => GetCandidates().FirstOrDefault();
}
