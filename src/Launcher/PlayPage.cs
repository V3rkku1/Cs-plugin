using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;
using static LanSkinTester.Launcher.SetupPage;

namespace LanSkinTester.Launcher;

/// <summary>Quick-start presets, server settings, start/stop and the live server panel with quick actions.</summary>
public class PlayPage : UserControl
{
    private readonly AppState _app;

    // What the running server was started with, to know if "Apply" needs a map change.
    private string? _runningMode;
    private string? _runningMap;

    // Server settings
    private readonly ComboBox _modeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _mapBox = new() { Width = 220 };
    private readonly TrackBar _botsBar = new() { Minimum = 0, Maximum = 20, TickFrequency = 2, Width = 230, Height = 30 };
    private readonly Label _botsLabel = Theme.Label("10 bots", Theme.Bold);
    private readonly Segmented _difficulty = new(ServerSettings.Difficulties, 72);
    private readonly ComboBox _botTeam = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _botWeapons = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _ffa = Check("Free-for-all (everyone is an enemy)");
    private readonly CheckBox _maxMoney = Check("Max money + buy anywhere");
    private readonly CheckBox _infiniteAmmo = Check("Infinite ammo");
    private readonly CheckBox _noRoundEnd = Check("Practice: rounds never end, respawn on death");
    private readonly CheckBox _botsDontShoot = Check("Bots don't shoot (look at your skins in peace)");
    private readonly CheckBox _cheats = Check("Allow cheats (thirdperson, noclip)");

    // More options
    private readonly LinkLabel _moreToggle = new() { Text = "Show more options", AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
    private readonly NumericUpDown _roundTime = new() { Minimum = 0, Maximum = 60, Width = 70 };
    private readonly CheckBox _friendlyFire = Check("Friendly fire");
    private readonly TextBox _workshop = new() { Width = 460, PlaceholderText = "Workshop map id or link (optional, needs internet)" };
    private readonly TextBox _gameArgs = new() { Width = 460, PlaceholderText = "-windowed -w 1920 -h 1080 -novid" };
    private readonly CheckBox _autoStartGame = Check("Start CS2 and join automatically");
    private readonly FlowLayoutPanel _more;

    // Play
    private readonly Button _startButton = Theme.MakeButton("START", Theme.ButtonKind.Primary, 120);
    private readonly Button _applyButton = Theme.MakeButton("Apply changes", Theme.ButtonKind.Normal, 128);
    private readonly Button _gameButton = Theme.MakeButton("Open CS2", Theme.ButtonKind.Normal, 106);
    private readonly Button _stopButton = Theme.MakeButton("Stop && restore", Theme.ButtonKind.Danger, 124);
    private readonly Label _modeStatus = new() { AutoSize = true, MaximumSize = new Size(500, 0), Font = Theme.Bold, Margin = new Padding(0, 10, 0, 0) };

    // Live
    private readonly Label _liveTitle = new() { AutoSize = true, Font = Theme.Big, Margin = new Padding(0, 0, 0, 2) };
    private readonly Label _liveDetail = new() { AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(500, 0) };
    private readonly FlowLayoutPanel _actions;
    private readonly ComboBox _giveBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 500, Height = 150, Font = Theme.Small };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };

    public PlayPage(AppState app)
    {
        _app = app;
        BackColor = Theme.Background;
        AutoScroll = true;
        Padding = new Padding(18);

        foreach (var m in ServerSettings.Modes) _modeBox.Items.Add(m);
        _botTeam.Items.AddRange(ServerSettings.BotTeams);
        _botWeapons.Items.AddRange(ServerSettings.BotWeaponChoices);
        foreach (var (def, name) in ItemData.DisplayNames.OrderBy(d => d.Value)) _giveBox.Items.Add(new GiveItem(ItemData.Weapons[def], name));
        _giveBox.Items.Add(new GiveItem("weapon_knife", "Knife (your equipped one)"));
        _giveBox.SelectedIndex = 0;

        // ---- Left: presets + server ----
        var presets = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(530, 0), BackColor = Theme.Surface };
        foreach (var preset in ServerSettings.Presets) presets.Controls.Add(PresetButton(preset));

        _more = Stack(
            Row(Theme.Label("Round time (minutes, 0 = mode default)"), _roundTime),
            _friendlyFire,
            Field("Workshop map", _workshop),
            Field("CS2 launch options", _gameArgs),
            _autoStartGame);
        _more.Visible = false;
        _moreToggle.LinkClicked += (_, _) =>
        {
            _more.Visible = !_more.Visible;
            _moreToggle.Text = _more.Visible ? "Hide more options" : "Show more options";
        };

        var server = Stack(
            Row(Field("Game mode", _modeBox), Field("Map", _mapBox)),
            Field("Bots", Row(_botsBar, _botsLabel)),
            Field("Bot skill", _difficulty),
            Row(Field("Bot team", _botTeam), Field("Bot weapons", _botWeapons)),
            Theme.Label("Options", Theme.Bold, Theme.Muted),
            _ffa, _maxMoney, _infiniteAmmo, _noRoundEnd, _botsDontShoot, _cheats,
            _moreToggle, _more);

        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 18, 0) };
        left.Controls.Add(Card("Quick start  ·  one click to play", presets, 560));
        left.Controls.Add(Card("Server", server, 560));

        // ---- Right: play + live + log ----
        foreach (var b in new[] { _startButton, _applyButton, _gameButton, _stopButton }) b.Height = 40;
        _startButton.Click += (_, _) => StartTest();
        _applyButton.Click += (_, _) => ApplyToServer();
        _gameButton.Click += (_, _) => OpenGame();
        _stopButton.Click += (_, _) => StopTest();
        var play = Stack(Row(_startButton, _applyButton, _gameButton, _stopButton), _modeStatus);

        var give = Theme.MakeButton("Give");
        give.Click += (_, _) => Send($"lst_give {((GiveItem)_giveBox.SelectedItem!).Classname}");
        _actions = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(500, 0), BackColor = Theme.Surface };
        _actions.Controls.AddRange([
            Action("Restart", "mp_restartgame 1"),
            Action("End warmup", "mp_warmup_end"),
            Action("Add bot", "bot_add"),
            Action("Kick bots", "bot_kick"),
            Action("Third person", "lst_view third"),
            Action("First person", "lst_view first"),
            Action("Respawn me", "lst_respawn"),
            Action("Refresh skins", "lst_refresh"),
        ]);
        var live = Stack(_liveTitle, _liveDetail, Theme.Label("Quick actions", Theme.Bold, Theme.Muted), _actions,
            Row(Theme.Label("Give weapon"), _giveBox, give));

        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        right.Controls.Add(Card("Play", play, 540));
        right.Controls.Add(Card("Live server", live, 540));
        right.Controls.Add(Card("Activity", _log, 540));

        var columns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Background };
        columns.Controls.AddRange([left, right]);
        Controls.Add(columns);
        Theme.Apply(this);

        LoadServerSettings();
        _modeBox.SelectedIndexChanged += (_, _) => OnModeChanged();
        _botsBar.ValueChanged += (_, _) => _botsLabel.Text = _botsBar.Value == 0 ? "No bots" : $"{_botsBar.Value} bots";

        _app.Changed += Refresh;
        _app.Logged += line =>
        {
            if (IsHandleCreated && InvokeRequired) BeginInvoke(() => _log.AppendText(line + Environment.NewLine));
            else _log.AppendText(line + Environment.NewLine);
        };
        _poll.Tick += (_, _) => RefreshLive();
        _poll.Start();
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
        _botsLabel.Text = _botsBar.Value == 0 ? "No bots" : $"{_botsBar.Value} bots";
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
        _mapBox.Items.Clear();
        _mapBox.Items.AddRange(((GameMode)_modeBox.SelectedItem!).Maps);
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

        _modeStatus.ForeColor = on ? Theme.Accent : ready ? Theme.Good : Theme.Bad;
        _modeStatus.Text = on
            ? "Test mode is ON. Only start CS2 from this launcher until you press Stop - starting it from Steam now could get you VAC-kicked."
            : ready ? "Ready. Pick a quick start or press START."
            : "Not installed yet - go to SETUP and press \"Install / update everything\".";
        RefreshLive();
    }

    private void RefreshLive()
    {
        var status = _app.ReadServerStatus();
        var running = status != null;
        foreach (Control c in _actions.Controls) c.Enabled = running;
        _giveBox.Enabled = running;

        if (status == null)
        {
            _liveTitle.Text = _app.TestModeOn ? "○  Server starting / not running" : "○  Server not running";
            _liveTitle.ForeColor = Theme.Muted;
            _liveDetail.Text = _app.TestModeOn
                ? "The server can take a minute to load the map. Quick actions unlock when it's up."
                : "Press START or pick a quick start.";
            return;
        }

        var mode = ServerSettings.Modes.FirstOrDefault(m => m.GameType == status.GameType && m.Mode == status.GameMode)?.Name
                   ?? $"mode {status.GameType}/{status.GameMode}";
        _liveTitle.Text = $"●  {status.Map}  ·  {mode}";
        _liveTitle.ForeColor = Theme.Good;
        _liveDetail.Text = $"{status.Humans} player{(status.Humans == 1 ? "" : "s")} · {status.Bots} bot{(status.Bots == 1 ? "" : "s")} · plugin v{status.Version}" +
                           (status.SkinsAllowed ? " · skins working" : $"\n⚠ {status.DisabledReason}");
        if (!status.SkinsAllowed) _liveDetail.ForeColor = Theme.Accent;
        else _liveDetail.ForeColor = Theme.Muted;
    }

    // ---------------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------------

    private Control PresetButton(ServerPreset preset)
    {
        var card = new PresetCard(preset.Title, preset.Subtitle);
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
        else _app.Log("Install first (SETUP page), then pick the quick start again.");
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

    private Button Action(string text, string command)
    {
        var b = Theme.MakeButton(text);
        b.AutoSize = false;
        b.Size = new Size(118, 32);
        b.Margin = new Padding(0, 0, 4, 4);
        b.Click += (_, _) => Send(command);
        return b;
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };

    private static Control Field(string label, Control input)
    {
        var caption = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 4, 0, 2) };
        var panel = Stack(caption, input);
        panel.Margin = new Padding(0, 0, 18, 6);
        return panel;
    }
}
