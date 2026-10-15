using System.Diagnostics;
using System.Net;
using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;

namespace LanSkinTester.Launcher;

/// <summary>Play with friends: host (address, firewall, skins mode, invite) or join a friend's server.</summary>
public class FriendsPage : UserControl
{

    private readonly AppState _app;
    private readonly PlayPage _play;

    // Host
    private readonly Segmented _network = new(["Same Wi-Fi", "Radmin / Hamachi", "Internet (UPnP)"], 152);
    private readonly SettingRow _addressRow;
    private readonly Picker _address = new(170);
    private readonly PillButton _copyAddress = new("Copy");
    private readonly Segmented _skins = new(["Everyone uses mine", "Each picks own"], 150);
    private readonly TextField _password = new(170) { PlaceholderText = "None" };
    private readonly SettingRow _firewallRow;
    private readonly PillButton _firewallButton = new("Allow");
    private readonly PillButton _hostButton = new("Start Hosting", PillButton.Style.Primary);
    private readonly PillButton _inviteButton = new("Copy Code");
    private readonly PillButton _stopButton = new("Stop", PillButton.Style.Destructive);
    private readonly SettingRow _serverRow;

    // Join
    private readonly TextField _joinAddress = new(200) { PlaceholderText = "Invite code or address" };
    private Control _recentRow = null!;
    private readonly FlowLayoutPanel _recent = new() { AutoSize = true, MaximumSize = new Size(470, 0), Margin = Padding.Empty };
    private readonly TextField _joinPassword = new(200) { PlaceholderText = "If they set one" };
    private readonly PillButton _joinButton = new("Join Game", PillButton.Style.Primary);

    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private List<LocalAddress> _addresses = new();

    // Internet mode: router search state lives in RouterPort (shared with the Play page's Start).
    private static UpnpRouter? Router => RouterPort.Router;
    private static IPAddress? External => RouterPort.External;

    public FriendsPage(AppState app, PlayPage play)
    {
        _app = app;
        _play = play;
        BackColor = Theme.Background;

        const int left = 560;
        const int right = 500;

        // ---- Host ----
        var connection = new Group(left);
        connection.AddContent(new VStack(_network,
            Hint("Same Wi-Fi: your friend is at your place.  Radmin: free VPN, both join the same Radmin network first.  " +
                 "Internet: the launcher opens the port on your router (UPnP), nothing else to install.", left)),
            new Padding(14, 14, 14, 10));

        var address = new Group(left);
        _addressRow = address.AddRow("Your Address", new HStack(_address, _copyAddress), "");
        _address.Margin = new Padding(0, 0, 8, 0);
        _addressRow.Height = 64;

        var options = new Group(left);
        options.AddRow("Skins", _skins, "Own: friends use !skin in chat");
        options.AddRow("Password", _password, "Optional");
        _firewallRow = options.AddRow("Windows Firewall", _firewallButton, "Lets friends reach your server (asks for admin once)");

        foreach (var b in new[] { _hostButton, _inviteButton, _stopButton }) b.Height = 36;
        var host = new Group(left);
        host.AddContent(new HStack(_hostButton, _inviteButton, _stopButton), new Padding(14, 14, 14, 8));
        _serverRow = host.AddRow("Not hosting", null, "Uses the mode, map and bots from the Play page.");

        var leftColumn = new VStack(
            Theme.Caption("1 · HOW ARE YOU CONNECTED?"), connection,
            Theme.Caption("2 · SEND THIS TO YOUR FRIEND"), address,
            Theme.Caption("3 · OPTIONS"), options,
            Theme.Caption("4 · PLAY"), host) { Margin = new Padding(0, 0, 28, 0) };

        // ---- Join ----
        var join = new Group(right);
        join.AddRow("Invite Code or Address", _joinAddress, "Paste what your friend sent");
        join.AddRow("Password", _joinPassword);
        _joinButton.Height = 36;
        _recentRow = join.AddContent(new VStack(Theme.Caption("RECENT"), _recent), new Padding(0, 0, 14, 0));
        join.AddContent(new VStack(_joinButton,
            Hint("Nothing to install. CS2 starts with VAC off for this one game (normal again next time). " +
                 "If CS2 is already open, it just connects.", right)),
            new Padding(14, 12, 14, 12));

        var safe = new Group(right);
        safe.AddRow("Your inventory never changes", null, "Skins only exist on this private server");
        safe.AddRow("VAC is off, not tricked", null, "-insecure is Valve's own switch for offline/LAN play");
        safe.AddRow("Your friend's game isn't modified", null, "Joining only starts CS2 with launch options");
        safe.AddRow("Not public", null, "No server token, not in the server browser");
        safe.AddRow("Stop puts everything back", null, "Then play matchmaking as normal");

        var skins = new Group(right);
        skins.AddContent(new Label
        {
            Text = "!skin <name> [wear]   skin the gun in your hands\n!knife <name>   !gloves <type> <name>\n" +
                   "!gen <code>   copy a real item (float, pattern, stickers)\n!skinsearch <text>   !skinhelp",
            Font = Theme.Mono, ForeColor = Theme.Text, BackColor = Color.Transparent, AutoSize = true,
        }, new Padding(14, 12, 14, 12));

        var rightColumn = new VStack(
            Theme.Caption("JOIN A FRIEND"), join,
            Theme.Caption("STAYING SAFE"), safe,
            Theme.Caption("FRIENDS PICK SKINS IN CHAT"), skins);

        var page = new VStack(PlayPage.Header("Friends", "Play together on the same Wi-Fi, over Radmin VPN or over the internet."),
            new HStack(leftColumn, rightColumn));
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        LoadSettings();
        _network.SelectedChanged += (_, _) =>
        {
            SaveSettings();
            if (Mode != ServerSettings.FriendsNetwork.Internet && Upnp.Active != null) _ = CloseRouterPortAsync();
            RouterPort.ForgetSearch(); // look again when Internet is picked
            FillAddresses();
            NoteRestart();
        };
        _skins.SelectedChanged += (_, _) => { SaveSettings(); SendIfRunning($"lst_shared_loadout {(_skins.SelectedIndex == 0 ? 1 : 0)}"); };
        _password.Box.Leave += (_, _) => { SaveSettings(); SendIfRunning($"sv_password \"{_app.Settings.Server.Password}\""); };
        _address.SelectedIndexChanged += (_, _) => UpdateAddressRow();
        _copyAddress.Click += (_, _) =>
        {
            if (_address.SelectedItem == null && Mode == ServerSettings.FriendsNetwork.Internet) _ = FindRouterAsync();
            else Copy(SelectedAddress(), "Address copied.");
        };
        _firewallButton.Click += (_, _) => AllowFirewall();
        _hostButton.Click += async (_, _) => await StartHostingAsync();
        _stopButton.Click += (_, _) => _play.StopServer();
        _inviteButton.Click += (_, _) => Copy(Invite(), "Code copied - your friend pastes it in Join a Friend.");
        _joinButton.Click += (_, _) => Join();
        _joinAddress.Box.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Join(); } };
        FillRecent();

        _app.Changed += Refresh;
        RouterPort.Changed += () =>
        {
            if (IsDisposed) return;
            if (Mode == ServerSettings.FriendsNetwork.Internet) FillAddresses();
            else UpdateAddressRow();
        };
        VisibleChanged += (_, _) => { if (Visible) { FillAddresses(); RefreshFirewall(); } };
        _poll.Tick += (_, _) =>
        {
            if (Visible) RefreshServer();
            _ = RenewRouterPortAsync();
        };
        _poll.Start();
    }

    private static Label Hint(string text, int groupWidth)
    {
        var label = Theme.Label(text, Theme.Small, Theme.Muted);
        label.MaximumSize = new Size(groupWidth - 40, 0);
        label.Margin = new Padding(2, 10, 0, 0);
        return label;
    }

    // ---------------------------------------------------------------------------------------------
    // Settings
    // ---------------------------------------------------------------------------------------------

    private void LoadSettings()
    {
        var s = _app.Settings.Server;
        _network.SelectedIndex = (int)s.Friends;
        _skins.SelectedIndex = s.SharedSkins ? 0 : 1;
        _password.Text = s.Password;
        FillAddresses();
    }

    private void SaveSettings()
    {
        var s = _app.Settings.Server;
        s.Friends = Mode;
        s.SharedSkins = _skins.SelectedIndex == 0;
        s.Password = _password.Text.Replace("\"", "").Trim();
        _app.Settings.Save();
    }

    // ---------------------------------------------------------------------------------------------
    // Address + firewall
    // ---------------------------------------------------------------------------------------------

    private ServerSettings.FriendsNetwork Mode => (ServerSettings.FriendsNetwork)Math.Max(_network.SelectedIndex, 0);

    private void FillAddresses()
    {
        if (Mode == ServerSettings.FriendsNetwork.Internet)
        {
            _address.SetItems(External != null ? [new LocalAddress(External, "Internet", Router?.Name ?? "router")] : []);
            if (_address.Items.Count > 0) _address.SelectedIndex = 0;
            UpdateAddressRow();
            if (!RouterPort.Checked && !RouterPort.Searching) _ = FindRouterAsync();
            return;
        }

        _addresses = Network.Addresses();
        var vpn = Mode == ServerSettings.FriendsNetwork.Vpn;
        var matching = _addresses.Where(a => a.IsVpn == vpn).ToList();
        _address.SetItems(matching.Count > 0 ? matching : _addresses);
        if (_address.Items.Count > 0) _address.SelectedIndex = 0;
        UpdateAddressRow();
    }

    private string SelectedAddress() => _address.SelectedItem?.ToString() ?? "";

    private void SetAddressNote(string text, Color color)
    {
        _addressRow.Subtitle = text;
        _addressRow.SubtitleColor = color;
    }

    private void UpdateAddressRow()
    {
        var selected = _address.SelectedItem as LocalAddress;
        var internet = Mode == ServerSettings.FriendsNetwork.Internet;
        _address.Enabled = _inviteButton.Enabled = selected != null;
        _copyAddress.Text = internet && selected == null ? "Retry" : "Copy";
        _copyAddress.Enabled = selected != null || (internet && !RouterPort.Searching);

        if (Mode == ServerSettings.FriendsNetwork.Internet)
        {
            if (RouterPort.Searching) SetAddressNote("Looking for your router…", Theme.Muted);
            else if (Router == null) SetAddressNote("No UPnP router - turn it on or use Radmin", Theme.Warning);
            else if (External == null) SetAddressNote($"{Router.Name} didn't say its internet address", Theme.Warning);
            else if (Upnp.IsBehindAnotherNat(External))
                SetAddressNote("Your provider shares one address between homes (CGNAT): friends can't reach you - use Radmin", Theme.Warning);
            else if (Upnp.Active != null) SetAddressNote($"Port {ServerSettings.Port} is open on {Router.Name}", Theme.Good);
            else SetAddressNote($"{Router.Name} · the port opens when you start hosting", Theme.Muted);
            return;
        }

        var vpn = Mode == ServerSettings.FriendsNetwork.Vpn;
        if (selected == null)
            SetAddressNote(vpn ? "No Radmin address yet - open Radmin VPN and join a network" : "No network found - connect to Wi-Fi", Theme.Warning);
        else if (selected.IsVpn != vpn)
            SetAddressNote(vpn
                ? "No Radmin address yet - open Radmin VPN and join a network with your friend"
                : $"Only a {selected.Kind} address found - switch to Radmin above", Theme.Warning);
        else
            SetAddressNote($"{selected.Kind} · {selected.Adapter}", Theme.Muted);
    }

    // ---------------------------------------------------------------------------------------------
    // Internet (UPnP)
    // ---------------------------------------------------------------------------------------------

    private Task FindRouterAsync() => RouterPort.FindAsync(_app.Log);

    private Task CloseRouterPortAsync() => RouterPort.CloseAsync(_app.Log);

    private async Task RenewRouterPortAsync()
    {
        try { await Upnp.RenewIfNeededAsync(); }
        catch (Exception) { /* try again on the next tick */ }
    }

    private async Task StartHostingAsync()
    {
        SaveSettings();
        _hostButton.Enabled = false;
        // Same start as the Play page's: opens the router port in Internet mode, sets a password...
        await _play.StartServerAsync(forFriends: true);
        _hostButton.Enabled = !_app.TestModeOn && _app.Ready;
    }

    private void RefreshFirewall()
    {
        var ok = Network.FirewallRuleExists();
        _firewallButton.Text = ok ? "Allowed" : "Allow";
        _firewallButton.Enabled = !ok && _app.Game != null;
    }

    private void AllowFirewall()
    {
        if (_app.Game == null) return;
        if (Network.AllowThroughFirewall(_app.Game)) _app.Log("Windows Firewall: CS2 allowed (inbound).");
        else _app.Log("Firewall rule not added (admin prompt declined?). Windows also asks the first time the server starts - tick Private and Public.");
        RefreshFirewall();
    }

    // ---------------------------------------------------------------------------------------------
    // Hosting
    // ---------------------------------------------------------------------------------------------

    public new void Refresh()
    {
        if (InvokeRequired)
        {
            BeginInvoke(Refresh);
            return;
        }
        var on = _app.TestModeOn;
        _hostButton.Enabled = _app.Ready && !on;
        if (!_password.Box.Focused && _password.Text != _app.Settings.Server.Password) _password.Text = _app.Settings.Server.Password;
        _stopButton.Enabled = on;
        _joinButton.Enabled = _app.Game != null;
        UpdateAddressRow();
        RefreshServer();
    }

    private void RefreshServer()
    {
        var status = _app.ReadServerStatus();
        if (status == null)
        {
            _serverRow.Title = _app.TestModeOn ? "Server starting…" : _app.Ready ? "Not hosting" : "Install first (Setup page) to host";
            _serverRow.Subtitle = "Uses the mode, map and bots from the Play page.";
            _serverRow.SubtitleColor = Theme.Muted;
            return;
        }

        var others = Math.Max(status.Humans - 1, 0);
        _serverRow.Title = $"Hosting {status.Map} · {status.Humans} player{(status.Humans == 1 ? "" : "s")}";
        _serverRow.Subtitle = !status.SkinsAllowed ? status.DisabledReason
            : status.Players.Count > 0 ? string.Join(", ", status.Players)
            : "Waiting for players…";
        _serverRow.SubtitleColor = status.SkinsAllowed ? (others > 0 ? Theme.Good : Theme.Muted) : Theme.Warning;
    }

    private void NoteRestart()
    {
        if (_app.TestModeOn) _app.Log("Connection type changes on the next Start (press Stop, then Start Hosting).");
    }

    private void SendIfRunning(string command)
    {
        if (_app.Game == null || !_app.TestModeOn) return;
        try
        {
            ServerSettings.SendCommands(_app.Game, command);
        }
        catch (IOException ex)
        {
            _app.Log("Could not send: " + ex.Message);
        }
    }

    private string Invite() => FriendsInvite.Code(SelectedAddress(), _app.Settings.Server.Password);

    private void Copy(string text, string message)
    {
        if (text == "") return;
        try
        {
            Clipboard.SetText(text);
            _app.Log(message);
            Toast.Show(message);
        }
        catch (Exception ex)
        {
            _app.Log("Could not copy: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Joining
    // ---------------------------------------------------------------------------------------------

    private void Join() => Join(FriendsInvite.Parse(_joinAddress.Text, _joinPassword.Text));

    private void Join(JoinTarget? target)
    {
        if (_app.Game == null) return;
        if (target == null)
        {
            MessageBox.Show(this, "Paste the invite code your friend sent (LST-…), or type their address.", "Join",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            if (TestSession.Cs2Running())
            {
                // No restart: Steam hands the connect link to the open game.
                Process.Start(new ProcessStartInfo(FriendsInvite.SteamConnectUrl(target)) { UseShellExecute = true });
                _app.Log($"Joining {target.Address} in your open CS2... If it says the server is insecure, close CS2 and press Join again " +
                         "(then it restarts with VAC off for this game).");
            }
            else
            {
                Process.Start(new ProcessStartInfo(_app.Game.Exe)
                {
                    Arguments = _app.Settings.Server.JoinArguments(_app.Game, target.Address, target.Password),
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(_app.Game.Exe)!,
                });
                _app.Log($"Joining {target.Address} (VAC off for this game). If it doesn't connect: console (~) → connect {target.Address}");
            }
            Toast.Show($"Joining {target.Address}…", Toast.Kind.Info);
            Remember(target);
        }
        catch (Exception ex)
        {
            _app.Log("Could not join: " + ex.Message);
        }
    }

    private void Remember(JoinTarget target)
    {
        var list = _app.Settings.RecentServers;
        list.RemoveAll(r => r.Address == target.Address);
        list.Insert(0, target);
        if (list.Count > 4) list.RemoveRange(4, list.Count - 4);
        _app.Settings.Save();
        FillRecent();
    }

    /// <summary>One-click rejoin buttons for the last servers.</summary>
    private void FillRecent()
    {
        _recent.SuspendLayout();
        foreach (Control c in _recent.Controls.Cast<Control>().ToList()) c.Dispose();
        _recent.Controls.Clear();
        foreach (var server in _app.Settings.RecentServers)
        {
            var b = new PillButton(server.Address) { Margin = new Padding(0, 0, 8, 8) };
            b.Click += (_, _) => Join(server);
            _recent.Controls.Add(b);
        }
        _recentRow.Visible = _recent.Controls.Count > 0;
        _recent.Margin = new Padding(14, 0, 0, 0);
        _recent.ResumeLayout();
    }
}
