using System.Diagnostics;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>Friends page: host (connection, address, firewall, skins, password, invite) or join a friend.</summary>
public partial class Api
{
    private bool? _firewallOk;
    private DateTime _firewallCheckedAt;

    public record FriendAddress(string Address, string Kind, string Adapter);

    public record FriendsState(int Network, bool SharedSkins, string Password, List<FriendAddress> Addresses, string AddressNote,
        string AddressKind, bool Searching, bool CanRetry, bool FirewallOk, bool HasGame, bool Ready, bool TestMode, bool Busy,
        string HostTitle, string HostDetail, string HostKind, List<string> Players, List<JoinTarget> Recent, int Port);

    [Call("friends.state")]
    private FriendsState FriendsStateNow()
    {
        var s = App.Settings.Server;
        var internet = s.Friends == ServerSettings.FriendsNetwork.Internet;
        var vpn = s.Friends == ServerSettings.FriendsNetwork.Vpn;

        List<LocalAddress> addresses;
        if (internet)
        {
            addresses = RouterPort.External != null ? [new LocalAddress(RouterPort.External, "Internet", RouterPort.Router?.Name ?? "router")] : [];
            if (!RouterPort.Checked && !RouterPort.Searching) _ = RouterPort.FindAsync(App.Log);
        }
        else
        {
            var all = Network.Addresses();
            var matching = all.Where(a => a.IsVpn == vpn).ToList();
            addresses = matching.Count > 0 ? matching : all;
        }

        // What the first address (the one shown) means for this connection type.
        var selected = addresses.FirstOrDefault();
        var router = RouterPort.Router;
        var (note, kind) = internet
            ? RouterPort.Searching ? ("Looking for your router…", "muted")
            : router == null ? ("No UPnP router - turn UPnP on in your router, or use Radmin", "warning")
            : RouterPort.External == null ? ($"{router.Name} didn't say its internet address", "warning")
            : Upnp.IsBehindAnotherNat(RouterPort.External) ? ("Your provider shares one address between homes (CGNAT): friends can't reach you - use Radmin", "warning")
            : Upnp.Active != null ? ($"Port {ServerSettings.Port} is open on {router.Name}", "good")
            : ($"{router.Name} · the port opens when you start hosting", "muted")
            : selected == null ? (vpn ? "No Radmin address yet - open Radmin VPN and join a network" : "No network found - connect to Wi-Fi", "warning")
            : selected.IsVpn != vpn ? (vpn ? "No Radmin address yet - open Radmin VPN and join a network with your friend"
                : $"Only a {selected.Kind} address found - switch to Radmin above", "warning")
            : ($"{selected.Kind} · {selected.Adapter}", "muted");

        // netsh is slow: check the firewall rule at most every 30 seconds.
        if (_firewallOk == null || DateTime.UtcNow - _firewallCheckedAt > TimeSpan.FromSeconds(30))
        {
            _firewallOk = Network.FirewallRuleExists();
            _firewallCheckedAt = DateTime.UtcNow;
        }

        var status = App.ReadServerStatus();
        string title, detail, hostKind;
        if (status == null)
        {
            title = App.TestModeOn ? "Server starting…" : App.Ready ? "Not hosting" : "Install first (Setup page) to host";
            detail = "Uses the mode, map and bots from Live Server.";
            hostKind = App.TestModeOn ? "busy" : "off";
        }
        else
        {
            var others = Math.Max(status.Humans - 1, 0);
            title = $"Hosting {status.Map} · {PluralOf(status.Humans, "player")}";
            detail = !status.SkinsAllowed ? status.DisabledReason : status.Players.Count > 0 ? string.Join(", ", status.Players) : "Waiting for players…";
            hostKind = !status.SkinsAllowed ? "warning" : others > 0 ? "good" : "running";
        }

        return new FriendsState((int)s.Friends, s.SharedSkins, s.Password,
            addresses.Select(a => new FriendAddress(a.ToString(), a.Kind, a.Adapter)).ToList(), note, kind,
            RouterPort.Searching, internet && selected == null && !RouterPort.Searching, _firewallOk == true,
            App.Game != null, App.Ready, App.TestModeOn, _playBusy, title, detail, hostKind,
            status?.Players ?? [], App.Settings.RecentServers, ServerSettings.Port);
    }

    public record FriendsSetArgs(int? Network, bool? SharedSkins, string? Password);

    [Call("friends.set")]
    private async Task SetFriends(FriendsSetArgs a)
    {
        var s = App.Settings.Server;
        if (a.Network is { } n && n is >= 0 and <= 2 && n != (int)s.Friends)
        {
            s.Friends = (ServerSettings.FriendsNetwork)n;
            SavePlaySettings();
            RouterPort.ForgetSearch(); // look again when Internet is picked
            if (App.TestModeOn) App.Log("Connection type changes on the next Start (press Stop, then Start Hosting).");
            App.NotifyChanged();
            if (s.Friends != ServerSettings.FriendsNetwork.Internet && Upnp.Active != null) await RouterPort.CloseAsync(App.Log);
        }
        if (a.SharedSkins is { } shared && shared != s.SharedSkins)
        {
            s.SharedSkins = shared;
            SavePlaySettings();
            SendIfRunning($"lst_shared_loadout {(shared ? 1 : 0)}");
            App.NotifyChanged();
        }
        if (a.Password != null)
        {
            var password = a.Password.Replace("\"", "").Trim();
            if (password == s.Password) return;
            s.Password = password;
            SavePlaySettings();
            SendIfRunning($"sv_password \"{password}\"");
            App.NotifyChanged();
        }
    }

    private void SendIfRunning(string command)
    {
        if (App.Game == null || !App.TestModeOn) return;
        try { ServerSettings.SendCommands(App.Game, command); }
        catch (IOException ex) { App.Log("Could not send: " + ex.Message); }
    }

    [Call("friends.findRouter")]
    private Task FindRouter() => RouterPort.FindAsync(App.Log);

    [Call("friends.firewall")]
    private async Task AllowFirewall()
    {
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first.");
        var ok = await Task.Run(() => Network.AllowThroughFirewall(game));
        if (ok)
        {
            App.Log("Windows Firewall: CS2 allowed (inbound).");
            Toast("CS2 is allowed through Windows Firewall.", "good");
        }
        else
        {
            App.Log("Firewall rule not added (admin prompt declined?). Windows also asks the first time the server starts - tick Private and Public.");
            Toast("Firewall rule not added - Windows asks again when the server starts.", "warning");
        }
        _firewallOk = null;
        App.NotifyChanged();
    }

    [Call("friends.host")]
    private Task Host() => StartServerAsync(new AskArgs(ForFriends: true));

    public record CopyArgs(string What, string? Address);

    /// <summary>Copies this PC's address, the invite code (address + password) or a whole invite message.</summary>
    [Call("friends.copy")]
    private async Task CopyInvite(CopyArgs a)
    {
        var s = App.Settings.Server;
        var address = string.IsNullOrWhiteSpace(a.Address) ? FriendsInvite.HostAddress(s) : a.Address.Trim();
        if (address == null)
            throw new ApiException(s.Friends == ServerSettings.FriendsNetwork.Internet
                ? "No internet address yet - the router has to answer first (Friends page)."
                : "No address found for this connection type - see the Friends page.");
        var (text, message) = a.What switch
        {
            "code" => (FriendsInvite.Code(address, s.Password), "Code copied - your friend pastes it in Join a Friend."),
            "message" => (FriendsInvite.InviteText(s, address), "Invite copied - paste it to your friend."),
            _ => (address.Contains(':') ? address : $"{address}:{ServerSettings.Port}", "Address copied - send it to your friend."),
        };
        try
        {
            await OnUi(() => Clipboard.SetText(text));
        }
        catch (Exception ex)
        {
            App.Log("Could not copy: " + ex.Message);
            throw new ApiException("Could not copy: " + ex.Message);
        }
        App.Log(message);
        Toast(message, "good");
    }

    public record JoinArgs(string? Text, string? Password, int? Recent);

    [Call("friends.join")]
    private void Join(JoinArgs a)
    {
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first (Setup page).");
        var target = a.Recent is { } i
            ? App.Settings.RecentServers.ElementAtOrDefault(i)
            : FriendsInvite.Parse(a.Text ?? "", a.Password ?? "");
        if (target == null) throw new ApiException("Paste the invite code your friend sent (LST-…), or type their address.");

        try
        {
            if (TestSession.Cs2Running())
            {
                // No restart: Steam hands the connect link to the open game.
                Process.Start(new ProcessStartInfo(FriendsInvite.SteamConnectUrl(target)) { UseShellExecute = true });
                App.Log($"Joining {target.Address} in your open CS2... If it says the server is insecure, close CS2 and press Join again " +
                        "(then it restarts with VAC off for this game).");
            }
            else
            {
                Process.Start(new ProcessStartInfo(game.Exe)
                {
                    Arguments = App.Settings.Server.JoinArguments(game, target.Address, target.Password),
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(game.Exe)!,
                });
                App.Log($"Joining {target.Address} (VAC off for this game). If it doesn't connect: console (~) → connect {target.Address}");
            }
            Toast($"Joining {target.Address}…");
            Remember(target);
        }
        catch (Exception ex)
        {
            App.Log("Could not join: " + ex.Message);
            throw new ApiException("Could not join: " + ex.Message);
        }
    }

    private void Remember(JoinTarget target)
    {
        var list = App.Settings.RecentServers;
        list.RemoveAll(r => r.Address == target.Address);
        list.Insert(0, target);
        if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
        SavePlaySettings();
        App.NotifyChanged();
    }

    [Call("friends.forget")]
    private void ForgetRecent(int index)
    {
        var list = App.Settings.RecentServers;
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        SavePlaySettings();
        App.NotifyChanged();
    }
}
