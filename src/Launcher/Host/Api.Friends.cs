using System.Diagnostics;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

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
            App.SaveSettings();
            RouterPort.ForgetSearch(); // look again when Internet is picked
            if (App.TestModeOn) App.Log("Connection type changes on the next Start (press Stop, then Start Hosting).");
            App.NotifyChanged();
            if (s.Friends != ServerSettings.FriendsNetwork.Internet && Upnp.Active != null) await RouterPort.CloseAsync(App.Log);
        }
        if (a.SharedSkins is { } shared && shared != s.SharedSkins)
        {
            s.SharedSkins = shared;
            App.SaveSettings();
            SendIfRunning($"lst_shared_loadout {(shared ? 1 : 0)}");
            App.NotifyChanged();
        }
        if (a.Password != null)
        {
            var password = ServerSettings.CleanPassword(a.Password);
            if (password == s.Password) return;
            s.Password = password;
            App.SaveSettings();
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
        JoinServer(game, target);
    }

    private void JoinServer(GameFolder game, JoinTarget target)
    {
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
                new TestSession(game).Join(App.Settings.Server, target.Address, target.Password);
                App.Log($"Joining {target.Address} (VAC off for this game). If it doesn't connect: console (~) → connect {target.Address}");
            }
            Toast($"Joining {target.Address}…");
            Remember(target);
            SendMySkins(game);
        }
        catch (Exception ex)
        {
            App.Log("Could not join: " + ex.Message);
            throw new ApiException("Could not join: " + ex.Message);
        }
    }

    /// <summary>Your own skins on the friend's server (their plugin wears them for you); see <see cref="ClientConsole"/>.</summary>
    private void SendMySkins(GameFolder game)
    {
        var started = DateTime.UtcNow;
        // The cfg's text (null: nothing to send). Same gate as the inventory calls: it reads (and may prune) the
        // loadout an equip is writing.
        string? Refresh()
        {
            InventoryGate.Wait();
            try { RefreshNow(); }
            finally { InventoryGate.Release(); }
            var file = Path.Combine(game.Csgo, "cfg", ClientConsole.SkinsCfg + ".cfg");
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        void RefreshNow()
        {
            var l = ReadLoadout();
            var cfg = Path.Combine(game.Csgo, "cfg", ClientConsole.SkinsCfg + ".cfg");
            var empty = l.Skins.Count + l.SkinsT.Count + l.SkinsCT.Count == 0 && l.Knife + l.KnifeT + l.KnifeCT + l.Gloves + l.GlovesT + l.GlovesCT == 0
                        && l.AgentT == null && l.AgentCT == null && l.Pin == 0 && l.MusicKit == 0;
            if (empty || !ClientConsole.WriteSkinsCfg(game, l)) File.Delete(cfg);
        }
        try { Refresh(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log("Could not write your skins for the friend's server: " + ex.Message); return; }
        // Until CS2 closes (it may take a while to start), and not while you host your own server.
        ClientConsole.StartSendingSkins(() => (TestSession.Cs2Running() || DateTime.UtcNow - started < TimeSpan.FromSeconds(90)) && TestSession.FindServer() == null, Refresh, App.Log);
        App.Log($"Your skins go along to the friend's server (their launcher must be up to date). If they don't show: console (~) → exec {ClientConsole.SkinsCfg}");
    }

    private void Remember(JoinTarget target)
    {
        var list = App.Settings.RecentServers;
        list.RemoveAll(r => r.Address == target.Address);
        list.Insert(0, target);
        if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
        App.SaveSettings();
        App.NotifyChanged();
    }

    [Call("friends.forget")]
    private void ForgetRecent(int index)
    {
        var list = App.Settings.RecentServers;
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        App.SaveSettings();
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Friends list: saved friends (online check) and friends hosting on this network
    // ---------------------------------------------------------------------------------------------

    public record FriendRow(int Index, string Name, string Address, bool Online, string Detail, bool Password);
    public record FriendsList(List<FriendRow> Friends, List<LanHost> Lan, string HostName);

    [Call("friends.list")]
    private async Task<FriendsList> FriendsListNow()
    {
        var saved = App.Settings.FriendsList;
        var infos = await Task.WhenAll(saved.Select(f => QueryFriendAsync(f.Address)));
        var rows = saved.Select((f, i) => infos[i] is { } info
                ? new FriendRow(i, f.Name, f.Address, true, $"Hosting {info.Map} · {info.Players}/{info.MaxPlayers} players" + (info.Bots > 0 ? $" + {info.Bots} bots" : "") + $" · {info.Ping} ms", info.Password)
                : new FriendRow(i, f.Name, f.Address, false, "Not hosting right now", false))
            .OrderByDescending(r => r.Online).ThenBy(r => r.Name)
            .ToList();
        return new FriendsList(rows, FriendFinder.LanHosts(), App.Settings.HostName);
    }

    private readonly Dictionary<string, (DateTime At, ServerInfo? Info)> _friendInfo = new();

    /// <summary>
    /// A saved friend's server, asked at most every 5 seconds: the list reloads with every change in the launcher,
    /// and each friend who isn't hosting costs a few seconds of waiting.
    /// </summary>
    private async Task<ServerInfo?> QueryFriendAsync(string address)
    {
        lock (_friendInfo)
            if (_friendInfo.TryGetValue(address, out var known) && DateTime.UtcNow - known.At < TimeSpan.FromSeconds(5)) return known.Info;
        var info = await FriendFinder.QueryAsync(address);
        lock (_friendInfo) _friendInfo[address] = (DateTime.UtcNow, info);
        return info;
    }

    public record AddFriendArgs(string? Name, string? Text);

    /// <summary>Saves a friend from their invite code or address.</summary>
    [Call("friends.add")]
    private void AddFriend(AddFriendArgs a)
    {
        var target = FriendsInvite.Parse(a.Text ?? "", "") ?? throw new ApiException("Paste the friend's invite code (LST-…) or address.");
        var name = (a.Name ?? "").Trim();
        if (name == "") name = target.Address;
        var list = App.Settings.FriendsList;
        list.RemoveAll(f => f.Address == target.Address);
        list.Add(new FriendContact { Name = name.Length > 32 ? name[..32] : name, Address = target.Address, Password = target.Password });
        App.SaveSettings();
        Toast($"{name} added to your friends.", "good");
        App.NotifyChanged();
    }

    [Call("friends.remove")]
    private void RemoveFriend(int index)
    {
        var list = App.Settings.FriendsList;
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        App.SaveSettings();
        App.NotifyChanged();
    }

    [Call("friends.joinSaved")]
    private void JoinSaved(int index)
    {
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first (Setup page).");
        var f = App.Settings.FriendsList.ElementAtOrDefault(index) ?? throw new ApiException("That friend isn't in the list any more.");
        JoinServer(game, new JoinTarget(f.Address, f.Password));
    }

    public record JoinLanArgs(string Address, string? Password);

    [Call("friends.joinLan")]
    private void JoinLan(JoinLanArgs a)
    {
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first (Setup page).");
        if (FriendFinder.LanHosts().All(h => h.Address != a.Address)) throw new ApiException("That server isn't announcing any more.");
        JoinServer(game, new JoinTarget(a.Address, (a.Password ?? "").Trim()));
    }

    [Call("friends.hostName")]
    private void SetHostName(string name)
    {
        name = (name ?? "").Trim();
        App.Settings.HostName = name == "" ? Environment.UserName : name.Length > 32 ? name[..32] : name;
        App.SaveSettings();
        App.NotifyChanged();
    }

    /// <summary>What this PC announces to friends' launchers on the same network while it hosts.</summary>
    private (string, string, int, int, bool)? HostBeacon()
    {
        if (!App.TestModeOn || App.ReadServerStatus() is not { } status) return null;
        return (App.Settings.HostName, status.Map, status.Humans, status.MaxPlayers > 0 ? status.MaxPlayers : 10, App.Settings.Server.Password != "");
    }
}
