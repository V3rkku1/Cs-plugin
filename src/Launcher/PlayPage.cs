using System.Diagnostics;
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
    private bool _runningOnline;   // started with sv_lan 0 (can download Workshop maps)
    private bool _runningForFriends;

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
    private readonly TextField _workshop = new(170) { PlaceholderText = "ID or link (optional)" };
    private readonly PillButton _subscribe = new("Subscribe");
    private SettingRow _workshopRow = null!;
    private readonly TextField _gameArgs = new(220) { PlaceholderText = "-windowed -w 1920 -h 1080" };
    private readonly Toggle _autoStartGame = new();
    private readonly Segmented _hackerMode = new(ServerSettings.HackerModes, 100);
    private readonly Toggle _hackWallhack = new();
    private readonly Toggle _hackAimbot = new();
    private readonly Toggle _hackOneTap = new();
    private readonly Toggle _hackAmmo = new();
    private readonly Toggle _hackSpeed = new();
    private readonly Toggle _hackBhop = new();
    private readonly Toggle _hackNoFlash = new();
    private readonly Toggle _hackEspInfo = new();
    private readonly Slider _hackAggression = new() { Minimum = 1, Maximum = 10, Width = 200 };
    private readonly Label _hackAggressionLabel = new() { AutoSize = false, Width = 64, Height = 28, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight };

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
        Color.FromArgb(57, 255, 20), Color.FromArgb(255, 214, 10),
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
        _workshop.Margin = new Padding(0, 0, 8, 0);
        _workshopRow = game.AddRow("Workshop Map", new HStack(_workshop, _subscribe), "Overrides the map");
        _workshop.TextChanged += (_, _) => UpdateWorkshopRow();
        _subscribe.Click += (_, _) => SubscribeWorkshop();

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

        var hacker = new Group(left);
        hacker.AddRow("Who Hacks", _hackerMode, "One random: new hacker each round, glows green");
        hacker.AddRow("Wallhack", _hackWallhack, "See enemies glowing through walls");
        hacker.AddRow("Aimbot", _hackAimbot, "Hold fire: smoothly pulls onto visible enemies near your crosshair");
        hacker.AddRow("Aim Aggression", new HStack(_hackAggression, _hackAggressionLabel), "1 gentle and loose · 10 fast hard head lock");
        hacker.AddRow("Damage Boost", _hackOneTap, "2× damage");
        hacker.AddRow("Infinite Ammo", _hackAmmo, "Never reload");
        hacker.AddRow("Speedhack", _hackSpeed, "Run 35% faster");
        hacker.AddRow("Bunnyhop", _hackBhop, "Hold jump to bhop perfectly");
        hacker.AddRow("No Flash", _hackNoFlash, "Flashbangs don't blind you");
        hacker.AddRow("Enemy Info", _hackEspInfo, "Nearest enemy's name, HP, distance and direction on screen");
        hacker.AddRow("In-Game Cheat Menu", null, "Type !cheats in game, or bind a key: bind F3 css_cheats");

        var launch = new Group(left);
        launch.AddRow("Open CS2 Automatically", _autoStartGame, "Off = start only the server");
        launch.AddRow("Launch Options", _gameArgs, "Extra options for the game window");

        var leftColumn = new VStack(
            Theme.Caption("QUICK START"), quick,
            Theme.Caption("GAME"), game,
            Theme.Caption("BOTS"), bots,
            Theme.Caption("OPTIONS"), options,
            Theme.Caption("HACKER MODE (FUN)"), hacker,
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
        _hackAggression.ValueChanged += (_, _) => _hackAggressionLabel.Text = AggressionText(_hackAggression.Value);
        _botsBar.ValueChanged += (_, _) => _botsLabel.Text = _botsBar.Value == 0 ? "None" : $"{_botsBar.Value}";

        _app.Changed += Refresh;
        _app.Logged += line =>
        {
            if (IsHandleCreated && InvokeRequired) BeginInvoke(() => _log.AppendText(line + Environment.NewLine));
            else _log.AppendText(line + Environment.NewLine);
        };
        _poll.Tick += (_, _) =>
        {
            RefreshLive();
            CheckServerCrash();
            if (Visible && WorkshopId() != null) UpdateWorkshopRow(); // turns green when Steam finishes downloading
        };
        _poll.Start();
    }

    /// <summary>Large page title + subtitle, like a macOS settings pane.</summary>
    internal static Control Header(string title, string subtitle)
    {
        var t = new Label { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 0) };
        var s = new Label { Text = subtitle, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(4, 2, 0, 16) };
        return new VStack(t, s) { Height = 86 };
    }

    private static string AggressionText(int level) => level switch { <= 3 => $"{level} legit", <= 7 => $"{level}", _ => $"{level} rage" };

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
        _botWeapons.SelectedIndex = Math.Clamp(s.BotWeapons, 0, ServerSettings.BotWeaponChoices.Length - 1);
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
        _hackerMode.SelectedIndex = Math.Clamp(s.HackerMode, 0, 2);
        _hackWallhack.Checked = s.HackWallhack;
        _hackAimbot.Checked = s.HackAimbot;
        _hackOneTap.Checked = s.HackOneTap;
        _hackAmmo.Checked = s.HackAmmo;
        _hackSpeed.Checked = s.HackSpeed;
        _hackBhop.Checked = s.HackBhop;
        _hackNoFlash.Checked = s.HackNoFlash;
        _hackEspInfo.Checked = s.HackEspInfo;
        _hackAggression.Value = Math.Clamp(s.HackAggression, 1, 10);
        _hackAggressionLabel.Text = AggressionText(_hackAggression.Value);
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
        s.HackerMode = _hackerMode.SelectedIndex;
        s.HackWallhack = _hackWallhack.Checked;
        s.HackAimbot = _hackAimbot.Checked;
        s.HackOneTap = _hackOneTap.Checked;
        s.HackAmmo = _hackAmmo.Checked;
        s.HackSpeed = _hackSpeed.Checked;
        s.HackBhop = _hackBhop.Checked;
        s.HackNoFlash = _hackNoFlash.Checked;
        s.HackEspInfo = _hackEspInfo.Checked;
        s.HackAggression = _hackAggression.Value;
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

    private Process? _reportedServer;

    // Workshop start: the server boots on a normal map, and once it's up (and logged on to Steam) it
    // is switched to the Workshop map. Asking for it on the command line happens before Steam is
    // ready, and the server then sits idle with no map ("Host activate: Idle"). CS2 starts once the
    // Workshop map is actually loaded.
    private string? _pendingWorkshopId;
    private string? _bootstrapMap;
    private bool _startGameAfterWorkshop;
    private DateTime? _serverUpSince;
    private DateTime? _workshopSentAt;

    private bool WaitingForWorkshop => _pendingWorkshopId != null;

    private void ClearWorkshopWait()
    {
        _pendingWorkshopId = _bootstrapMap = null;
        _serverUpSince = _workshopSentAt = null;
    }

    private void HandleWorkshopStartup(ServerStatus? status)
    {
        if (_pendingWorkshopId is not { } id || _app.Game == null) return;
        if (!_app.TestModeOn)
        {
            ClearWorkshopWait(); // stopped meanwhile
            return;
        }

        var now = DateTime.UtcNow;
        if (_workshopSentAt == null)
        {
            if (status == null)
            {
                _serverUpSince = null;
                return;
            }
            // Give it a few seconds on the first map to finish logging on to Steam.
            _serverUpSince ??= now;
            if (now - _serverUpSince < TimeSpan.FromSeconds(6)) return;
            try
            {
                ServerSettings.SendCommands(_app.Game, $"host_workshop_map {id}");
                _workshopSentAt = now;
                _app.Log($"Server is up - loading Workshop map {id} (it downloads first, this can take a while)...");
            }
            catch (IOException ex)
            {
                _app.Log("Could not send the Workshop map command: " + ex.Message);
            }
            return;
        }

        if (status != null && !string.Equals(status.Map, _bootstrapMap, StringComparison.OrdinalIgnoreCase))
        {
            _app.Log($"Workshop map loaded: {status.Map}.");
            Toast.Show($"{status.Map} loaded" + (_startGameAfterWorkshop ? " - starting CS2…" : "."), Toast.Kind.Info);
            if (_startGameAfterWorkshop)
            {
                try { new TestSession(_app.Game).StartGame(_app.Settings.Server); }
                catch (Exception ex) { _app.Log("Could not start CS2: " + ex.Message); }
            }
            ClearWorkshopWait();
        }
        else if (now - _workshopSentAt > TimeSpan.FromMinutes(15))
        {
            ClearWorkshopWait();
            _app.Log("The Workshop map still hasn't loaded after 15 minutes. Check the server window (is it downloading?), " +
                     "or press \"Open CS2\" to join the server on its first map.");
        }
    }

    /// <summary>The server we started is gone without Stop: say so and show its last console lines.</summary>
    private void CheckServerCrash()
    {
        var server = TestSession.Server;
        if (_app.Game == null || server == null || TestSession.StoppedOnPurpose || server == _reportedServer) return;
        bool exited;
        try { exited = server.HasExited; }
        catch (Exception) { return; }
        if (!exited) return;

        _reportedServer = server;
        int? code = null;
        try { code = server.ExitCode; } catch (Exception) { /* unknown */ }
        _app.Log($"The server closed unexpectedly{(code is { } c && c != 0 ? $" (exit code 0x{c:X8})" : "")}. Its last console lines:");
        var tail = new TestSession(_app.Game).ConsoleTail();
        if (tail.Length == 0) _app.Log("   (no console log - it closed before writing one)");
        foreach (var line in tail) _app.Log("   " + line);
        Toast.Show("The server closed unexpectedly - see Activity on the Play page.", Toast.Kind.Error);
    }

    // ---------------------------------------------------------------------------------------------
    // Workshop map
    // ---------------------------------------------------------------------------------------------

    private string? WorkshopId()
    {
        var m = System.Text.RegularExpressions.Regex.Match(_workshop.Text, @"(\d{6,})");
        return m.Success ? m.Groups[1].Value : null;
    }

    private void UpdateWorkshopRow()
    {
        var id = WorkshopId();
        _subscribe.Enabled = id != null;
        if (id == null)
        {
            _workshopRow.Subtitle = _workshop.Text.Trim() == "" ? "Overrides the map" : "Paste the map's ID or Workshop link";
            _workshopRow.SubtitleColor = _workshop.Text.Trim() == "" ? Theme.Muted : Theme.Warning;
            return;
        }
        var subscribed = _app.Game != null && WorkshopMaps.FindDownloaded(_app.Game, id) != null;
        _workshopRow.Subtitle = subscribed ? "Subscribed · the server loads it from the Workshop" : "The server downloads it (needs internet)";
        _workshopRow.SubtitleColor = subscribed ? Theme.Good : Theme.Muted;
    }

    private void SubscribeWorkshop()
    {
        if (WorkshopId() is not { } id) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WorkshopMaps.SubscribeUrl(id)) { UseShellExecute = true });
            _app.Log("Opened the map in Steam: press Subscribe there, wait for the download, then Start (or Apply).");
        }
        catch (Exception ex)
        {
            _app.Log("Could not open Steam: " + ex.Message);
        }
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
        UpdateWorkshopRow();
    }

    private void RefreshLive()
    {
        var status = _app.ReadServerStatus();
        HandleWorkshopStartup(status);
        var running = status != null;
        foreach (Control c in _actions.Controls) c.Enabled = running;
        _giveBox.Enabled = _giveButton.Enabled = running;

        if (status == null)
        {
            _liveRow.Title = !_app.TestModeOn ? "Server not running"
                : WaitingForWorkshop ? "Starting the server for the Workshop map…" : "Server starting…";
            _liveRow.Subtitle = !_app.TestModeOn ? "Press Start or pick a quick start."
                : WaitingForWorkshop ? "Then it downloads the map. CS2 starts by itself when it's loaded."
                : "Loading the map can take a minute. Actions unlock when it's up.";
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
        if (WaitingForWorkshop)
        {
            _liveRow.Subtitle = _workshopSentAt == null ? "Server is up - loading the Workshop map next…"
                : "Downloading and loading the Workshop map… CS2 starts by itself when it's ready.";
            _liveRow.SubtitleColor = Theme.Accent;
        }
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

    private void StartTest(bool forFriends = false)
    {
        if (_app.Game == null) return;
        var server = ReadServerSettings();
        try
        {
            _app.AllowSkinsInCss();
            // A newer launcher carries a newer plugin: put it in before the server loads it.
            if (!Installer.PluginMatchesBundled(_app.Game))
            {
                new Installer(_app.Game, _app.Http, _app.Log).InstallPlugin();
                _app.Log($"Updated the plugin to v{Installer.BundledPluginVersion()?.ToString(3) ?? "?"} (the one in this launcher).");
            }
            var gameStarted = new TestSession(_app.Game).Start(server, forFriends);
            ClearWorkshopWait();
            if (server.WorkshopNeedsDownload)
            {
                _pendingWorkshopId = server.WorkshopMapId;
                _bootstrapMap = server.Map;
                _startGameAfterWorkshop = server.AutoStartGame;
            }
            _runningMode = server.Mode;
            _runningMap = server.WorkshopMapId ?? server.Map;
            _runningOnline = server.PrivateServer;
            _runningForFriends = forFriends;
            var map = server.WorkshopMapId != null ? $"Workshop map {server.WorkshopMapId}" : server.Map;
            if (server.WorkshopNeedsDownload)
                _app.Log("Workshop map: the server goes online (unlisted) and loads it from the Workshop - the first time takes " +
                         "longer while it downloads. Players' games fetch it automatically when they join.");
            _app.Log($"Started {server.Mode} on {map} with {server.Bots} {ServerSettings.Difficulties[server.Difficulty].ToLowerInvariant()} bots.");
            Toast.Show($"Starting {server.Mode} on {map}…", Toast.Kind.Info);
            if (server.Friends != ServerSettings.FriendsNetwork.Lan) _app.Log($"Friends mode: {(server.Friends == ServerSettings.FriendsNetwork.Vpn ? "Radmin / VPN" : "internet (UPnP)")} (sv_lan 0, unlisted, VAC off).");
            _app.Log(!server.AutoStartGame ? "Server only - press \"Open CS2\" when you want to join."
                : gameStarted ? $"CS2 joins when the server has loaded. If it doesn't: console (~) → connect 127.0.0.1:{ServerSettings.Port}"
                : "CS2 starts by itself as soon as the server has downloaded and loaded the Workshop map.");
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
            var warning = server.ApplyToRunningServer(_app.Game, reload, _runningOnline, _runningForFriends);
            if (warning != null)
            {
                _app.Log(warning);
                Toast.Show("Workshop map not downloaded - see Activity.", Toast.Kind.Warning);
                return;
            }
            _runningMode = server.Mode;
            _runningMap = map;
            _app.Log(reload ? $"Switching to {server.Mode} on {map}..." : "Applied bots and options to the running server.");
            Toast.Show(reload ? $"Switching to {server.Mode} on {map}…" : "Applied to the running server.");
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
            Toast.Show("Stopped. CS2 is back to normal.");
        }
        catch (Exception ex)
        {
            _app.Log("Could not restore gameinfo.gi: " + ex.Message);
            _app.Log("   -> Steam > CS2 > Properties > Installed Files > Verify integrity also restores it.");
        }
        _app.NotifyChanged();
    }

    /// <summary>Start / Stop from the Friends page (uses the game settings from this page).</summary>
    public void StartServer(bool forFriends = false) => StartTest(forFriends);

    /// <summary>F5: start, or apply the settings to the running server.</summary>
    public void StartOrApply()
    {
        if (_app.TestModeOn) ApplyToServer();
        else if (_app.Ready) StartTest();
        else Toast.Show("Install first (Setup page).", Toast.Kind.Warning);
    }

    /// <summary>CS2 and the server were closed without Stop: restore quietly.</summary>
    public void AutoRestore()
    {
        if (_app.Game == null) return;
        new TestSession(_app.Game).Stop();
        _runningMode = _runningMap = null;
        try { File.Delete(Path.Combine(_app.Game.PluginDir, ServerStatus.FileName)); }
        catch (IOException) { /* stale file is ignored anyway */ }
        _app.Log("CS2 was closed - restored gameinfo.gi automatically. You can play online again.");
        _app.NotifyChanged();
    }
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
            Toast.Show("Sent to the server.", Toast.Kind.Info);
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
