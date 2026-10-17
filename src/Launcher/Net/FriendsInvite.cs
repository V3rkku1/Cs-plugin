using System.Text;
using System.Text.RegularExpressions;

namespace LanSkinTester.Launcher.Core;

/// <summary>A server to join: address with port, plus the password (may be empty).</summary>
public record JoinTarget(string Address, string Password)
{
    public override string ToString() => Address;
}

/// <summary>
/// The address friends use to reach this PC, invite texts and one-line invite codes ("LST-…", which
/// carry address, port and password), and reading whatever a friend pastes into the Join box.
/// </summary>
public static class FriendsInvite
{
    public const string DownloadUrl = "https://github.com/V3rkku1/Cs-plugin/releases/tag/latest";
    private const string CodePrefix = "LST-";

    /// <summary>This PC's address for the current friends mode, or null when there isn't one yet.</summary>
    public static string? HostAddress(ServerSettings s)
    {
        if (s.Friends == ServerSettings.FriendsNetwork.Internet) return RouterPort.External?.ToString();
        var vpn = s.Friends == ServerSettings.FriendsNetwork.Vpn;
        return Network.Addresses().FirstOrDefault(a => a.IsVpn == vpn)?.ToString();
    }

    public static string Code(string address, string password)
    {
        if (!address.Contains(':')) address += ":" + ServerSettings.Port;
        var raw = Encoding.UTF8.GetBytes($"{address}|{password}");
        return CodePrefix + Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string InviteText(ServerSettings s, string address)
    {
        var lines = new List<string>
        {
            "Join my CS2 server!",
            $"1. Get LAN Skin Tester: {DownloadUrl}",
            "2. Friends → Join a Friend → paste this code:",
            "   " + Code(address, s.Password),
            $"(or type the address {address}" + (s.Password != "" ? $" and password {s.Password})" : ")"),
        };
        if (s.Friends == ServerSettings.FriendsNetwork.Vpn) lines.Insert(1, "0. Be in the same Radmin VPN network as me");
        if (!s.SharedSkins) lines.Add("Pick skins in chat: !skin <name>, !knife <name>, !gen <code>");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Reads an invite code, a whole invite message, "connect 1.2.3.4:27015; password x",
    /// steam://connect/… or a plain address. The password box is used when the text has none.
    /// </summary>
    public static JoinTarget? Parse(string text, string fallbackPassword)
    {
        text = (text ?? "").Trim();
        if (text == "") return null;

        var code = Regex.Match(text, CodePrefix + @"([A-Za-z0-9_\-]+)");
        if (code.Success)
        {
            try
            {
                var b64 = code.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|', 2);
                if (parts[0] != "") return new JoinTarget(WithPort(parts[0]), parts.Length > 1 ? parts[1] : "");
            }
            catch (FormatException) { /* not a valid code: try the rest */ }
        }

        var steam = Regex.Match(text, @"steam://(?:run/730//\+)?connect/([^/\s]+)(?:/([^\s]*))?", RegexOptions.IgnoreCase);
        if (steam.Success) return new JoinTarget(WithPort(steam.Groups[1].Value), Uri.UnescapeDataString(steam.Groups[2].Value));

        var password = Regex.Match(text, @"password\s+""?([^"";\s]+)", RegexOptions.IgnoreCase);
        var address = Regex.Match(text, @"(\d{1,3}(?:\.\d{1,3}){3}(?::\d{1,5})?|[a-z0-9][a-z0-9\-\.]*\.[a-z]{2,}(?::\d{1,5})?)", RegexOptions.IgnoreCase);
        if (!address.Success) return null;
        return new JoinTarget(WithPort(address.Groups[1].Value), password.Success ? password.Groups[1].Value : fallbackPassword.Trim());
    }

    private static string WithPort(string address) => address.Contains(':') ? address : address + ":" + ServerSettings.Port;

    /// <summary>Makes an already running CS2 connect (Steam hands the link to the open game).</summary>
    public static string SteamConnectUrl(JoinTarget target) =>
        $"steam://connect/{target.Address}" + (target.Password != "" ? "/" + Uri.EscapeDataString(target.Password) : "");
}
