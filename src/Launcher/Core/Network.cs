using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LanSkinTester.Launcher.Core;

/// <summary>An IPv4 address of this PC that a friend could connect to.</summary>
public record LocalAddress(IPAddress Address, string Kind, string Adapter)
{
    public bool IsVpn => Kind != "Wi-Fi / LAN";
    public override string ToString() => Address.ToString();
}

/// <summary>Finding this PC's addresses and opening the Windows firewall for CS2.</summary>
public static class Network
{
    public const string FirewallRule = "LAN Skin Tester (CS2)";

    /// <summary>
    /// Every adapter's IP settings. Skips adapters Windows can't read, and returns none when the list
    /// itself can't be read (it throws "Request not supported" on some systems, e.g. under Wine).
    /// </summary>
    public static IEnumerable<(NetworkInterface Nic, IPInterfaceProperties Props)> Interfaces(bool upOnly = true)
    {
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { yield break; }
        foreach (var nic in nics)
        {
            IPInterfaceProperties props;
            try
            {
                if (upOnly && (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)) continue;
                props = nic.GetIPProperties();
            }
            catch (NetworkInformationException) { continue; }
            yield return (nic, props);
        }
    }

    /// <summary>Every unicast IPv4 address with its adapter.</summary>
    private static IEnumerable<(NetworkInterface Nic, UnicastIPAddressInformation Ip)> IPv4(bool upOnly = true) =>
        Interfaces(upOnly).SelectMany(x => x.Props.UnicastAddresses
            .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork).Select(ip => (x.Nic, ip)));

    /// <summary>Every address of this PC, on any adapter (to ignore our own broadcasts).</summary>
    public static HashSet<string> OwnAddresses() =>
        Interfaces(upOnly: false).SelectMany(x => x.Props.UnicastAddresses).Select(a => a.Address.ToString()).ToHashSet();

    /// <summary>The broadcast address of every IPv4 network this PC is on (LAN, Wi-Fi, Radmin...).</summary>
    public static List<IPAddress> BroadcastAddresses()
    {
        var list = new List<IPAddress> { IPAddress.Broadcast };
        foreach (var (_, a) in IPv4())
        {
            if (a.IPv4Mask == null) continue;
            var ip = a.Address.GetAddressBytes();
            var mask = a.IPv4Mask.GetAddressBytes();
            list.Add(new IPAddress(ip.Select((b, i) => (byte)(b | ~mask[i])).ToArray()));
        }
        return list.Distinct().ToList();
    }

    public static List<LocalAddress> Addresses() =>
        IPv4()
            .Select(x => (x.Ip.Address, Kind: Classify(x.Ip.Address, x.Nic.Description + " " + x.Nic.Name), x.Nic.Name))
            .Where(x => x.Kind != null)
            .Select(x => new LocalAddress(x.Address, x.Kind!, x.Name))
            // VPN first when present (that's what friends over the internet need), then LAN.
            .OrderBy(a => a.IsVpn ? 0 : 1)
            .ToList();

    /// <summary>Our LAN IPv4 addresses (not VPNs) with their adapter's default gateways.</summary>
    public static IEnumerable<(IPAddress Local, List<IPAddress> Gateways)> LanAddresses() =>
        Interfaces().SelectMany(x =>
        {
            var gateways = x.Props.GatewayAddresses.Select(g => g.Address)
                .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any)).ToList();
            return x.Props.UnicastAddresses
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork
                             && Classify(ip.Address, x.Nic.Description + " " + x.Nic.Name) == "Wi-Fi / LAN")
                .Select(ip => (ip.Address, gateways));
        });

    /// <summary>What kind of network an address is on, or null for addresses friends can't use.</summary>
    public static string? Classify(IPAddress address, string adapter)
    {
        var b = address.GetAddressBytes();
        bool Named(string name) => adapter.Contains(name, StringComparison.OrdinalIgnoreCase);
        if (b[0] == 127 || (b[0] == 169 && b[1] == 254)) return null;
        if (b[0] == 26 || Named("Radmin")) return "Radmin VPN";
        if (b[0] == 25 || Named("Hamachi")) return "Hamachi";
        if (Named("ZeroTier")) return "ZeroTier";
        if (b[0] == 100 && b[1] is >= 64 and < 128) return "Tailscale / VPN";
        if (b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and < 32) return "Wi-Fi / LAN";
        return null; // a public address: don't suggest it (that's port forwarding, not this)
    }

    /// <summary>True if our firewall rule is there (reading rules needs no admin).</summary>
    public static bool FirewallRuleExists()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{FirewallRule}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (p == null) return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5_000);
            return p.ExitCode == 0 && output.Contains(FirewallRule);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Adds an inbound firewall rule for cs2.exe (asks for admin). Returns false if the user said no.</summary>
    public static bool AllowThroughFirewall(GameFolder game)
    {
        var args = $"/c netsh advfirewall firewall delete rule name=\"{FirewallRule}\" >nul & " +
                   $"netsh advfirewall firewall add rule name=\"{FirewallRule}\" dir=in action=allow program=\"{game.Exe}\" enable=yes profile=any";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            p?.WaitForExit(15_000);
            return p?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // admin prompt declined
        }
    }
}
