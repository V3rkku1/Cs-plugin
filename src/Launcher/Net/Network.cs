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

    public static List<LocalAddress> Addresses()
    {
        var list = new List<LocalAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ip in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var kind = Classify(ip.Address, nic.Description + " " + nic.Name);
                    if (kind != null) list.Add(new LocalAddress(ip.Address, kind, nic.Name));
                }
            }
        }
        catch (NetworkInformationException)
        {
            // No adapters readable: show nothing.
        }
        // VPN first when present (that's what friends over the internet need), then LAN.
        return list.OrderBy(a => a.IsVpn ? 0 : 1).ToList();
    }

    /// <summary>What kind of network an address is on, or null for addresses friends can't use.</summary>
    public static string? Classify(IPAddress address, string adapter)
    {
        var b = address.GetAddressBytes();
        if (b[0] == 127 || (b[0] == 169 && b[1] == 254)) return null;
        if (b[0] == 26 || adapter.Contains("Radmin", StringComparison.OrdinalIgnoreCase)) return "Radmin VPN";
        if (b[0] == 25 || adapter.Contains("Hamachi", StringComparison.OrdinalIgnoreCase)) return "Hamachi";
        if (adapter.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase)) return "ZeroTier";
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
        var exe = game.Exe;
        var args = $"/c netsh advfirewall firewall delete rule name=\"{FirewallRule}\" >nul & " +
                   $"netsh advfirewall firewall add rule name=\"{FirewallRule}\" dir=in action=allow program=\"{exe}\" enable=yes profile=any";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            p?.WaitForExit(15_000);
            return p?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // admin prompt declined
        }
    }
}
