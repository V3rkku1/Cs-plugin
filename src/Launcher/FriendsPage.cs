using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;

namespace LanSkinTester.Launcher;

/// <summary>Play with friends: host (address, firewall, skins mode, invite) or join a friend's server.</summary>
public class FriendsPage : UserControl
{
    private const string DownloadUrl = "https://github.com/V3rkku1/Cs-plugin/releases/tag/latest";

    private readonly AppState _app;
    private readonly PlayPage _play;

    // Host
    private readonly Segmented _network = new(["Same Wi-Fi / LAN", "Radmin VPN / Hamachi"], 170);
    private readonly SettingRow _addressRow;
    private readonly Picker _address = new(170);
    private readonly PillButton _copyAddress = new("Copy");
    private readonly Segmented _skins = new(["Everyone uses mine", "Each picks own"], 150);
    private readonly TextField _password = new(170) { PlaceholderText = "None" };
    private readonly SettingRow _firewallRow;
    private readonly PillButton _firewallButton = new("Allow");
    private readonly PillButton _hostButton = new("Start Hosting", PillButton.Style.Primary);
    private readonly PillButton _inviteButton = new("Copy Invite");
    private readonly PillButton _stopButton = new("Stop", PillButton.Style.Destructive);
    private readonly SettingRow _serverRow;

    // Join
    private readonly TextField _joinAddress = new(200) { PlaceholderText = "26.12.34.56" };
    private readonly TextField _joinPassword = new(200) { PlaceholderText = "If they set one" };
    private readonly PillButton _joinButton = new("Join Game", PillButton.Style.Primary);

    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };
    private List<LocalAddress> _addresses = new();

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
            Hint("Same Wi-Fi: your friend is at your place.  Radmin VPN: free, both join the same Radmin network first.", left)),
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
        join.AddRow("Friend's Address", _joinAddress);
        join.AddRow("Password", _joinPassword);
        _joinButton.Height = 36;
        join.AddContent(new VStack(_joinButton,
            Hint("You don't need to install anything to join. CS2 starts with VAC off for this one game; " +
                 "next time you start it from Steam it's completely normal.", right)),
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

        var page = new VStack(PlayPage.Header("Friends", "Play together on the same Wi-Fi or over Radmin VPN."),
            new HStack(leftColumn, rightColumn));
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        LoadSettings();
        _network.SelectedChanged += (_, _) => { SaveSettings(); FillAddresses(); NoteRestart(); };
        _skins.SelectedChanged += (_, _) => { SaveSettings(); SendIfRunning($"lst_shared_loadout {(_skins.SelectedIndex == 0 ? 1 : 0)}"); };
        _password.Box.Leave += (_, _) => { SaveSettings(); SendIfRunning($"sv_password \"{_app.Settings.Server.Password}\""); };
        _address.SelectedIndexChanged += (_, _) => UpdateAddressRow();
        _copyAddress.Click += (_, _) => Copy(SelectedAddress(), "Address copied.");
        _firewallButton.Click += (_, _) => AllowFirewall();
        _hostButton.Click += (_, _) => { SaveSettings(); _play.StartServer(); };
        _stopButton.Click += (_, _) => _play.StopServer();
        _inviteButton.Click += (_, _) => Copy(Invite(), "Invite copied - paste it to your friend.");
        _joinButton.Click += (_, _) => Join();

        _app.Changed += Refresh;
        VisibleChanged += (_, _) => { if (Visible) { FillAddresses(); RefreshFirewall(); } };
        _poll.Tick += (_, _) => { if (Visible) RefreshServer(); };
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
        _network.SelectedIndex = s.FriendsVpn ? 1 : 0;
        _skins.SelectedIndex = s.SharedSkins ? 0 : 1;
        _password.Text = s.Password;
        FillAddresses();
    }

    private void SaveSettings()
    {
        var s = _app.Settings.Server;
        s.FriendsVpn = _network.SelectedIndex == 1;
        s.SharedSkins = _skins.SelectedIndex == 0;
        s.Password = _password.Text.Replace("\"", "").Trim();
        _app.Settings.Save();
    }

    // ---------------------------------------------------------------------------------------------
    // Address + firewall
    // ---------------------------------------------------------------------------------------------

    private void FillAddresses()
    {
        _addresses = Network.Addresses();
        var vpn = _network.SelectedIndex == 1;
        var matching = _addresses.Where(a => a.IsVpn == vpn).ToList();
        _address.SetItems(matching.Count > 0 ? matching : _addresses);
        if (_address.Items.Count > 0) _address.SelectedIndex = 0;
        UpdateAddressRow();
    }

    private string SelectedAddress() => _address.SelectedItem?.ToString() ?? "";

    private void UpdateAddressRow()
    {
        var vpn = _network.SelectedIndex == 1;
        var selected = _address.SelectedItem as LocalAddress;
        _address.Enabled = _copyAddress.Enabled = _inviteButton.Enabled = selected != null;

        if (selected == null)
        {
            _addressRow.Subtitle = vpn ? "No Radmin address yet - open Radmin VPN and join a network" : "No network found - connect to Wi-Fi";
            _addressRow.SubtitleColor = Theme.Warning;
        }
        else if (selected.IsVpn != vpn)
        {
            _addressRow.Subtitle = vpn
                ? "No Radmin address yet - open Radmin VPN and join a network with your friend"
                : $"Only a {selected.Kind} address found - switch to Radmin above";
            _addressRow.SubtitleColor = Theme.Warning;
        }
        else
        {
            _addressRow.Subtitle = $"{selected.Kind} · {selected.Adapter}";
            _addressRow.SubtitleColor = Theme.Muted;
        }
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
        _stopButton.Enabled = on;
        _joinButton.Enabled = _app.Game != null;
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

    private string Invite()
    {
        var s = _app.Settings.Server;
        var lines = new List<string>
        {
            "Join my CS2 skin server!",
            $"1. Download LAN Skin Tester: {DownloadUrl}",
            "2. Open it → Friends → Join a Friend",
            $"3. Address: {SelectedAddress()}" + (s.Password != "" ? $"   Password: {s.Password}" : ""),
        };
        if (s.FriendsVpn) lines.Insert(1, "0. Be in the same Radmin VPN network as me");
        if (!s.SharedSkins) lines.Add("Pick skins in chat: !skin <name>, !knife <name>, !gen <code>");
        return string.Join(Environment.NewLine, lines);
    }

    private void Copy(string text, string message)
    {
        if (text == "") return;
        try
        {
            Clipboard.SetText(text);
            _app.Log(message);
        }
        catch (Exception ex)
        {
            _app.Log("Could not copy: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Joining
    // ---------------------------------------------------------------------------------------------

    private void Join()
    {
        if (_app.Game == null) return;
        var address = _joinAddress.Text.Trim().Replace("connect ", "");
        if (address == "")
        {
            MessageBox.Show(this, "Type your friend's address (they find it on their Friends page).", "Join", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!address.Contains(':')) address += ":" + ServerSettings.Port;

        if (TestSession.Cs2Running())
        {
            MessageBox.Show(this, "Close CS2 first - it has to restart to join with VAC off.", "Join", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_app.Game.Exe)
            {
                Arguments = ServerSettings.JoinArguments(_app.Settings.Server.GameArgs, address, _joinPassword.Text),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_app.Game.Exe)!,
            });
            _app.Log($"Joining {address} (VAC off for this game). If it doesn't connect: console (~) → connect {address}");
        }
        catch (Exception ex)
        {
            _app.Log("Could not start CS2: " + ex.Message);
        }
    }
}
