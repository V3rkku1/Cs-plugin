using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;

namespace LanSkinTester.Launcher;

/// <summary>Quick-start presets, server settings, start/stop and the live server panel with quick actions.</summary>
public class PlayPage : UserControl
{
    private readonly AppState _app;

    // What the running server was started with, to know if "Apply" needs a map change.
    private string? _runningMode;
    private string? _runningMap;

    // Server settings
    private readonly Picker _modeBox = new(200);
    private readonly Picker _mapBox = new(200, editable: true);
    private readonly Slider _botsBar = new() { Minimum = 0, Maximum = 20, Width = 200 };
    private readonly Label _botsLabel = new() { AutoSize = false, Width = 64, Height = 28, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight };
    private readonly Segmented _difficulty = new(ServerSettings.Difficulties, 70);
    private readonly Picker _botTeam = new(200);
    private readonly Picker _botWeapons = new(200);
    private readonly Toggle _ffa = new();
    private readonly Toggle _maxMoney = new();
    private readonly Toggle _infiniteAmmo = new();
    private readonly Toggle _noRoundEnd = new();
    private readonly Toggle _botsDontShoot = new();
    private readonly Toggle _cheats = new();
    private readonly NumberField _roundTime = new(120) { Minimum = 0, Maximum = 60 };
    private readonly Toggle _friendlyFire = new();
    private readonly TextField _workshop = new(220) { PlaceholderText = "ID or link (optional)" };
    private readonly TextField _gameArgs = new(220) { PlaceholderText = "-windowed -w 1920 -h 1080" };
    private readonly Toggle _autoStartGame = new();

    // Play
    private readonly PillButton _startButton = new("Start", PillButton.Style.Primary) { AutoWidth = false };
    private readonly PillButton _applyButton = new("Apply Changes");
    private readonly PillButton _gameButton = new("Open CS2");
    private readonly PillButton _stopButton = new("Stop", PillButton.Style.Destructive);
    private readonly Label _modeTitle = new() { AutoSize = true, Font = Theme.Big, ForeColor = Theme.Text, BackColor = Color.Transparent };
    private readonly Label _modeStatus = new() { AutoSize = true, MaximumSize = new Size(440, 0), Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Color.Transparent, Margin = new Padding(0, 4, 0, 14) };

    // Live
    private readonly SettingRow _liveRow;
    private readonly FlowLayoutPanel _actions;
    private readonly Picker _giveBox = new(190);
    private readonly PillButton _giveButton = new("Give");
    private readonly TextField _log = new(476, multiline: true, height: 170) { ReadOnly = true };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };

    private static readonly Color[] PresetTints =
    [
        Theme.Accent, Color.FromArgb(191, 90, 242), Color.FromArgb(255, 55, 95),
        Theme.Warning, Theme.Good, Color.FromArgb(100, 210, 255),
    ];

    public PlayPage(AppState app)
    {
        _app = app;
        BackColor = Theme.Background;
        Padding = Padding.Empty;

        foreach (var m in ServerSettings.Modes) _modeBox.Items.Add(m);
        _botTeam.SetItems(ServerSettings.BotTeams);
        _botWeapons.SetItems(ServerSettings.BotWeaponChoices);
        foreach (var (def, name) in ItemData.DisplayNames.OrderBy(d => d.Value)) _giveBox.Items.Add(new GiveItem(ItemData.Weapons[def], name));
        _giveBox.Items.Add(new GiveItem("weapon_knife", "Knife (equipped)"));
        _giveBox.SelectedIndex = 0;
        _log.Font = Theme.Mono;

        const int left = 560;
        const int right = 500;

        // ---- Left column ----
        var presets = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(left - 24, 0), BackColor = Color.Transparent, Margin = Padding.Empty };
        for (var i = 0; i < ServerSettings.Presets.Length; i++)
            presets.Controls.Add(PresetButton(ServerSettings.Presets[i], PresetTints[i % PresetTints.Length]));
        foreach (Control c in presets.Controls) c.Width = 168;
        var quick = new Group(left);
        quick.AddContent(presets, new Padding(12, 12, 2, 2));

        var game = new Group(left);
        game.AddRow("Game Mode", _modeBox);
        game.AddRow("Map", _mapBox);
        game.AddRow("Workshop Map", _workshop, "Overrides the map · needs internet");

        var bots = new Group(left);
        bots.AddRow("Bots", new HStack(_botsBar, _botsLabel));
        bots.AddRow("Skill", _difficulty);
        bots.AddRow("Team", _botTeam);
        bots.AddRow("Weapons", _botWeapons);

        var options = new Group(left);
        options.AddRow("Free-for-All", _ffa, "Deathmatch: everyone is an enemy");
        options.AddRow("Max Money", _maxMoney, "And buy anywhere");
        options.AddRow("Infinite Ammo", _infiniteAmmo);
        options.AddRow("Endless Rounds", _noRoundEnd, "Rounds never end, respawn on death");
        options.AddRow("Peaceful Bots", _botsDontShoot, "Bots don't shoot - look at your skins in peace");
        options.AddRow("Allow Cheats", _cheats, "Needed for third person and noclip");
        options.AddRow("Friendly Fire", _friendlyFire);
        options.AddRow("Round Time", _roundTime, "Minutes · 0 = game mode default");

        var launch = new Group(left);
        launch.AddRow("Open CS2 Automatically", _autoStartGame, "Off = start only the server");
        launch.AddRow("Launch Options", _gameArgs, "Extra options for the game window");

        var leftColumn = new VStack(
            Theme.Caption("QUICK START"), quick,
            Theme.Caption("GAME"), game,
            Theme.Caption("BOTS"), bots,
            Theme.Caption("OPTIONS"), options,
            Theme.Caption("LAUNCH"), launch) { Margin = new Padding(0, 0, 28, 0) };

        // ---- Right column ----
        _startButton.Width = 120;
        foreach (var b in new[] { _startButton, _applyButton, _gameButton, _stopButton }) b.Height = 36;
        _startButton.Click += (_, _) => StartTest();
        _applyButton.Click += (_, _) => ApplyToServer();
        _gameButton.Click += (_, _) => OpenGame();
        _stopButton.Click += (_, _) => StopTest();
        var control = new Group(right);
        control.AddContent(new VStack(_modeTitle, _modeStatus, new HStack(_startButton, _applyButton, _gameButton, _stopButton)),
            new Padding(18, 16, 18, 18));

        _giveButton.Click += (_, _) => Send($"lst_give {((GiveItem)_giveBox.SelectedItem!).Classname}");
        _actions = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(right - 28, 0), BackColor = Color.Transparent, Margin = Padding.Empty };
        _actions.Controls.AddRange([
            Action("Restart", "mp_restartgame 1"),
            Action("End Warmup", "mp_warmup_end"),
            Action("Add Bot", "bot_add"),
            Action("Kick Bots", "bot_kick"),
            Action("Third Person", "lst_view third"),
            Action("First Person", "lst_view first"),
            Action("Respawn Me", "lst_respawn"),
            Action("Refresh Skins", "lst_refresh"),
        ]);
        var live = new Group(right);
        _liveRow = live.AddRow("Server not running", null, "Press Start or pick a quick start.");
        live.AddContent(_actions, new Padding(12, 12, 4, 6));
        live.AddRow("Give Weapon", new HStack(_giveBox, _giveButton));
        _giveBox.Margin = new Padding(0, 0, 8, 0);

        var activity = new Group(right);
        activity.AddContent(_log, new Padding(12, 12, 12, 12));

        var rightColumn = new VStack(
            Theme.Caption("SERVER"), control,
            Theme.Caption("LIVE"), live,
            Theme.Caption("ACTIVITY"), activity);

        var page = new VStack(Header("Play", "Start a local server with bots and try your skins."), new HStack(leftColumn, rightColumn));
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        LoadServerSettings();
        _modeBox.SelectedIndexChanged += (_, _) => OnModeChanged();
        _botsBar.ValueChanged += (_, _) => _botsLabel.Text = _botsBar.Value == 0 ? "None" : $"{_botsBar.Value}";

        _app.Changed += Refresh;
        _app.Logged += line =>
        {
            if (IsHandleCreated && InvokeRequired) BeginInvoke(() => _log.AppendText(line + Environment.NewLine));
            else _log.AppendText(line + Environment.NewLine);
        };
        _poll.Tick += (_, _) => RefreshLive();
        _poll.Start();
    }

    /// <summary>Large page title + subtitle, like a macOS settings pane.</summary>
    internal static Control Header(string title, string subtitle)
    {
        var t = new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 0) };
        var s = new Label { Text = subtitle, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(4, 2, 0, 16) };
        return new VStack(t, s) { Height = 86 };
    }

    private record GiveItem(string Classname, string Name)
    {
        public override string ToString() => Name;
    }

    // ---------------------------------------------------------------------------------------------
    // Settings <-> controls
    // ---------------------------------------------------------------------------------------------

    private void LoadServerSettings()
    {
        var s = _app.Settings.Server;
        _modeBox.SelectedItem = s.GetMode();
        FillMaps();
        _mapBox.Text = s.Map;
        _botsBar.Value = Math.Clamp(s.Bots, 0, 20);
        _botsLabel.Text = _botsBar.Value == 0 ? "None" : $"{_botsBar.Value}";
        _difficulty.SelectedIndex = Math.Clamp(s.Difficulty, 0, 3);
        _botTeam.SelectedIndex = Math.Clamp(s.BotTeam, 0, 2);
        _botWeapons.SelectedIndex = Math.Clamp(s.BotWeapons, 0, 3);
        _ffa.Checked = s.FreeForAll;
        _maxMoney.Checked = s.MaxMoney;
        _infiniteAmmo.Checked = s.InfiniteAmmo;
        _noRoundEnd.Checked = s.NoRoundEnd;
        _botsDontShoot.Checked = s.BotsDontShoot;
        _cheats.Checked = s.AllowCheats;
        _roundTime.Value = Math.Clamp(s.RoundTime, 0, 60);
        _friendlyFire.Checked = s.FriendlyFire;
        _workshop.Text = s.WorkshopMap;
        _gameArgs.Text = s.GameArgs;
        _autoStartGame.Checked = s.AutoStartGame;
        _ffa.Enabled = s.GetMode().Name == "Deathmatch";
    }

    private ServerSettings ReadServerSettings()
    {
        var s = _app.Settings.Server;
        s.Mode = ((GameMode)_modeBox.SelectedItem!).Name;
        s.Map = string.IsNullOrWhiteSpace(_mapBox.Text) ? s.GetMode().Maps[0] : _mapBox.Text.Trim();
        s.Bots = _botsBar.Value;
        s.Difficulty = _difficulty.SelectedIndex;
        s.BotTeam = _botTeam.SelectedIndex;
        s.BotWeapons = _botWeapons.SelectedIndex;
        s.FreeForAll = _ffa.Checked;
        s.MaxMoney = _maxMoney.Checked;
        s.InfiniteAmmo = _infiniteAmmo.Checked;
        s.NoRoundEnd = _noRoundEnd.Checked;
        s.BotsDontShoot = _botsDontShoot.Checked;
        s.AllowCheats = _cheats.Checked;
        s.RoundTime = (int)_roundTime.Value;
        s.FriendlyFire = _friendlyFire.Checked;
        s.WorkshopMap = _workshop.Text.Trim();
        s.GameArgs = _gameArgs.Text.Trim();
        s.AutoStartGame = _autoStartGame.Checked;
        _app.Settings.Save();
        return s;
    }

    private void OnModeChanged()
    {
        var mode = (GameMode)_modeBox.SelectedItem!;
        var keep = mode.Maps.Contains(_mapBox.Text) ? _mapBox.Text : mode.Maps[0];
        FillMaps();
        _mapBox.Text = keep;
        _ffa.Enabled = mode.Name == "Deathmatch";
    }

    private void FillMaps()
    {
        _mapBox.SetItems(((GameMode)_modeBox.SelectedItem!).Maps);
    }

    // ---------------------------------------------------------------------------------------------
    // Status
    // ---------------------------------------------------------------------------------------------

    public new void Refresh()
    {
        var on = _app.TestModeOn;
        var ready = _app.Ready;
        _startButton.Enabled = ready && !on;
        _applyButton.Enabled = _stopButton.Enabled = on;
        _gameButton.Enabled = on;

        _modeTitle.Text = on ? "Test mode is on" : ready ? "Ready to play" : "Setup needed";
        _modeTitle.ForeColor = on ? Theme.Warning : Theme.Text;
        _modeStatus.Text = on
            ? "VAC is off. Only start CS2 from this launcher until you press Stop - starting it from Steam now could get you VAC-kicked."
            : ready ? "Pick a quick start or press Start. The server opens in its own window and CS2 joins it."
            : "Open Setup and press \"Install Everything\" first.";
        RefreshLive();
    }

    private void RefreshLive()
    {
        var status = _app.ReadServerStatus();
        var running = status != null;
        foreach (Control c in _actions.Controls) c.Enabled = running;
        _giveBox.Enabled = _giveButton.Enabled = running;

        if (status == null)
        {
            _liveRow.Title = _app.TestModeOn ? "Server starting…" : "Server not running";
            _liveRow.Subtitle = _app.TestModeOn
                ? "Loading the map can take a minute. Actions unlock when it's up."
                : "Press Start or pick a quick start.";
            _liveRow.SubtitleColor = Theme.Muted;
            return;
        }

        var mode = ServerSettings.Modes.FirstOrDefault(m => m.GameType == status.GameType && m.Mode == status.GameMode)?.Name
                   ?? $"mode {status.GameType}/{status.GameMode}";
        _liveRow.Title = $"{status.Map} · {mode}";
        _liveRow.Subtitle = status.SkinsAllowed
            ? $"{status.Humans} player{(status.Humans == 1 ? "" : "s")} · {status.Bots} bot{(status.Bots == 1 ? "" : "s")} · skins working · plugin {status.Version}"
            : status.DisabledReason;
        _liveRow.SubtitleColor = status.SkinsAllowed ? Theme.Muted : Theme.Warning;
    }

    // ---------------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------------

    private Control PresetButton(ServerPreset preset, Color tint)
    {
        var card = new PresetCard(preset.Title, preset.Subtitle, tint);
        card.Click += (_, _) => RunPreset(preset);
        return card;
    }

    private void RunPreset(ServerPreset preset)
    {
        preset.Apply(_app.Settings.Server);
        LoadServerSettings();
        _app.Log($"Quick start: {preset.Title}");

        if (_app.TestModeOn) ApplyToServer();
        else if (_app.Ready) StartTest();
        else _app.Log("Install first (Setup page), then pick the quick start again.");
    }

    private void StartTest()
    {
        if (_app.Game == null) return;
        var server = ReadServerSettings();
        try
        {
            _app.AllowSkinsInCss();
            new TestSession(_app.Game).Start(server);
            _runningMode = server.Mode;
            _runningMap = server.WorkshopMapId ?? server.Map;
            var map = server.WorkshopMapId != null ? $"Workshop map {server.WorkshopMapId}" : server.Map;
            _app.Log($"Started {server.Mode} on {map} with {server.Bots} {ServerSettings.Difficulties[server.Difficulty].ToLowerInvariant()} bots.");
            if (server.PrivateServer) _app.Log($"Friends mode: {(server.Friends == ServerSettings.FriendsNetwork.Vpn ? "Radmin / VPN" : "internet (UPnP)")} (sv_lan 0, unlisted, VAC off).");
            _app.Log(server.AutoStartGame
                ? "CS2 joins when the server has loaded. If it doesn't: console (~) → connect localhost"
                : "Server only - press \"Open CS2\" when you want to join.");
        }
        catch (Exception ex)
        {
            _app.Log("Could not start: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Could not start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        _app.NotifyChanged();
    }

    private void ApplyToServer()
    {
        if (_app.Game == null) return;
        var server = ReadServerSettings();
        var map = server.WorkshopMapId ?? server.Map;
        var reload = _runningMode == null || _runningMode != server.Mode || _runningMap != map;

        try
        {
            server.ApplyToRunningServer(_app.Game, reload);
            _runningMode = server.Mode;
            _runningMap = map;
            _app.Log(reload ? $"Switching to {server.Mode} on {map}..." : "Applied bots and options to the running server.");
        }
        catch (IOException ex)
        {
            _app.Log("Could not apply: " + ex.Message);
        }
    }

    private void OpenGame()
    {
        if (_app.Game == null) return;
        try
        {
            new TestSession(_app.Game).StartGame(ReadServerSettings());
            _app.Log("Starting CS2 (VAC off)...");
        }
        catch (Exception ex)
        {
            _app.Log("Could not start CS2: " + ex.Message);
        }
    }

    private void StopTest()
    {
        if (_app.Game == null) return;
        if (TestSession.Cs2Running() &&
            MessageBox.Show(this, "This closes CS2 and the test server. Continue?", "Stop skin test",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        try
        {
            new TestSession(_app.Game).Stop();
            _runningMode = _runningMap = null;
            File.Delete(Path.Combine(_app.Game.PluginDir, ServerStatus.FileName));
            _app.Log("Stopped. CS2 is back to normal - you can play online again.");
        }
        catch (Exception ex)
        {
            _app.Log("Could not restore gameinfo.gi: " + ex.Message);
            _app.Log("   -> Steam > CS2 > Properties > Installed Files > Verify integrity also restores it.");
        }
        _app.NotifyChanged();
    }

    /// <summary>Start / Stop from the Friends page (uses the game settings from this page).</summary>
    public void StartServer() => StartTest();
    public void StopServer() => StopTest();

    /// <summary>Called when the window closes while test mode is on.</summary>
    public void StopFromClose()
    {
        if (_app.Game == null) return;
        new TestSession(_app.Game).Stop();
        _app.NotifyChanged();
    }

    private void Send(string command)
    {
        if (_app.Game == null) return;
        try
        {
            ServerSettings.SendCommands(_app.Game, command);
            _app.Log($"> {command}");
        }
        catch (IOException ex)
        {
            _app.Log("Could not send: " + ex.Message);
        }
    }

    private Control Action(string text, string command)
    {
        var b = new PillButton(text) { AutoWidth = false };
        b.Width = 112;
        b.Margin = new Padding(0, 0, 8, 8);
        b.Click += (_, _) => Send(command);
        return b;
    }
}
