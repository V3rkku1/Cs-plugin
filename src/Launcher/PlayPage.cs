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
    private readonly Toggle _showDamage = new();
    private readonly PillButton _hackerButton = new("Open Hacker Page");
    private SettingRow _hackerRow = null!;

    /// <summary>The "Open Hacker Page" button was pressed.</summary>
    public event Action? OpenHackerPage;

    // Play
    private readonly PillButton _startButton = new("Start", PillButton.Style.Primary) { AutoWidth = false };
    private readonly PillButton _applyButton = new("Apply Changes");
    private readonly PillButton _gameButton = new("Open CS2");
    private readonly PillButton _stopButton = new("Stop", PillButton.Style.Destructive);
    private readonly PillButton _copyIp = new("Copy IP");
    private readonly PillButton _copyInvite = new("Copy Code", PillButton.Style.Primary);
    private readonly Hero _hero = new(1088, 176);
    private readonly Label _modeTitle = new SmoothLabel { AutoSize = true, Font = Theme.Title, ForeColor = Theme.Text, BackColor = Color.Transparent };
    private readonly Label _modeStatus = new SmoothLabel { AutoSize = true, MaximumSize = new Size(600, 0), Font = Theme.Body, ForeColor = Color.FromArgb(200, 205, 225), BackColor = Color.Transparent };
    private readonly Label _heroLive = new SmoothLabel { AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent };
    private readonly Label _inviteTitle = new SmoothLabel { AutoSize = true, Text = "Invite Friends", Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent };
    private readonly Label _inviteText = new SmoothLabel { AutoSize = true, MaximumSize = new Size(330, 0), Font = Theme.Small, ForeColor = Color.FromArgb(200, 205, 225), BackColor = Color.Transparent };
    // Two rows so six tabs stay readable. Order is fixed: saved PlayTab 3 is still Launch.
    private readonly Segmented _tabs = new(["Game", "Bots", "Rules", "Launch", "Retakes", "Live"], 178, columns: 3);

    private readonly Segmented _retakeStyle = new(ServerSettings.RetakeStyles, 96);
    private readonly NumberField _retakePlayers = new(90) { Minimum = 2, Maximum = 10 };
    private readonly NumberField _retakeRatio = new(90) { Minimum = 10, Maximum = 80 };
    private readonly Toggle _retakeScramble = new();
    private readonly NumberField _retakeScrambleRounds = new(90) { Minimum = 1, Maximum = 30 };
    private readonly Toggle _retakeBalance = new();
    private readonly Toggle _retakePlant = new();
    private readonly Toggle _retakeAnnounce = new();
    private readonly Toggle _retakeJoin = new();
    private readonly Toggle _retakeBuy = new();
    private readonly NumberField _retakeBuySeconds = new(90) { Minimum = 4, Maximum = 20 };
    private readonly NumberField _retakeBomb = new(90) { Minimum = 15, Maximum = 60 };
    private readonly List<SettingRow> _pluginRows = new();
    private SettingRow _retakeStyleRow = null!;
    private SettingRow _valveNote = null!;
    private SettingRow _retakeOff = null!;
    private SettingRow _botTeamRow = null!;
    private SettingRow _moneyRow = null!;
    private SettingRow _roundsRow = null!;
    private SettingRow _timeRow = null!;
    private readonly ToolTip _tips = new();

    // Live
    private readonly SettingRow _liveRow;
    private readonly List<Control> _liveControls = new();
    private string? _runningRetakeKey;
    private readonly Picker _giveBox = new(190);
    private readonly PillButton _giveButton = new("Give");
    private readonly TextField _command = new(300) { PlaceholderText = "Any server command" };
    private readonly PillButton _runCommand = new("Run", PillButton.Style.Primary);
    private readonly TextField _log = new(476, multiline: true, height: 280) { ReadOnly = true };
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = 2000 };

    private FlowLayoutPanel _presets = null!;
    private int _presetCardWidth;

    private static readonly Color[] PresetTints =
    [
        Theme.Accent, Color.FromArgb(191, 90, 242), Color.FromArgb(255, 55, 95),
        Theme.Warning, Theme.Good, Color.FromArgb(100, 210, 255),
        Color.FromArgb(57, 255, 20), Color.FromArgb(255, 100, 60), Color.FromArgb(255, 214, 10),
    ];
    private static readonly Color SavedTint = Color.FromArgb(120, 150, 255);

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
        const int full = left + 28 + right;

        // ---- Hero: status, start/stop and invite ----
        _startButton.Width = 140;
        _startButton.Height = 42;
        foreach (var b in new[] { _applyButton, _gameButton, _stopButton }) b.Height = 42;
        _startButton.Click += async (_, _) => await StartServerAsync();
        _applyButton.Click += (_, _) => ApplyToServer();
        _gameButton.Click += (_, _) => OpenGame();
        _stopButton.Click += (_, _) => StopTest();
        _tips.SetToolTip(_startButton, "Start the server and CS2 (F5)");
        _tips.SetToolTip(_applyButton, "Send the settings below to the running server (F5)");
        _tips.SetToolTip(_stopButton, "Close the server and CS2, back to normal (Shift+F5)");
        _copyIp.Click += (_, _) => CopyToClipboard(FriendsInvite.HostAddress(_app.Settings.Server), "Address copied - send it to your friend.");
        _copyInvite.Click += (_, _) =>
        {
            var address = FriendsInvite.HostAddress(_app.Settings.Server);
            if (address != null) CopyToClipboard(FriendsInvite.Code(address, _app.Settings.Server.Password), "Code copied - your friend pastes it in Join a Friend.");
        };
        _hero.Controls.AddRange([_modeTitle, _modeStatus, _heroLive, _startButton, _applyButton, _gameButton, _stopButton, _inviteTitle, _inviteText, _copyIp, _copyInvite]);
        _hero.Layout += (_, _) => LayoutHero();

        // ---- Quick start: built-in tiles, plus any setups you saved ----
        _presetCardWidth = (full - 24 - 4 * 10) / 5;
        _presets = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(full - 14, 0), BackColor = Color.Transparent, Margin = Padding.Empty };
        RebuildQuickStarts();
        var saveSetup = new PillButton("Save setup");
        saveSetup.Click += (_, _) => SaveSetup();
        var quick = new Group(full);
        quick.AddContent(_presets, new Padding(12, 12, 2, 2));
        quick.AddRow("Your setups", saveSetup, "Saves the settings below as a quick start · click × on a saved tile to delete it");

        // ---- Settings, one tab at a time ----
        var game = new Group(left);
        game.AddRow("Game Mode", _modeBox);
        game.AddRow("Map", _mapBox);
        _workshop.Margin = new Padding(0, 0, 8, 0);
        _workshopRow = game.AddRow("Workshop Map", new HStack(_workshop, _subscribe), "Overrides the map");
        _workshop.TextChanged += (_, _) => UpdateWorkshopRow();
        _subscribe.Click += (_, _) => SubscribeWorkshop();
        _hackerRow = game.AddRow("Hacker Mode", _hackerButton, "");
        _hackerButton.Click += (_, _) => OpenHackerPage?.Invoke();

        var bots = new Group(left);
        bots.AddRow("Bots", new HStack(_botsBar, _botsLabel));
        bots.AddRow("Skill", _difficulty);
        _botTeamRow = bots.AddRow("Team", _botTeam, "0 bots = friends only");
        bots.AddRow("Weapons", _botWeapons);
        bots.AddRow("Peaceful Bots", _botsDontShoot, "Bots don't shoot - look at your skins in peace");

        var options = new Group(left);
        options.AddRow("Free-for-All", _ffa, "Deathmatch: everyone is an enemy");
        _moneyRow = options.AddRow("Max Money", _maxMoney, "And buy anywhere");
        options.AddRow("Infinite Ammo", _infiniteAmmo);
        _roundsRow = options.AddRow("Endless Rounds", _noRoundEnd, "Rounds never end, respawn on death");
        options.AddRow("Allow Cheats", _cheats, "Needed for third person and noclip");
        options.AddRow("Friendly Fire", _friendlyFire);
        _timeRow = options.AddRow("Round Time", _roundTime, "Minutes · 0 = game mode default");
        options.AddRow("Damage Report", _showDamage, "Chat shows the damage you deal");

        var launch = new Group(left);
        launch.AddRow("Open CS2 Automatically", _autoStartGame, "Off = start only the server");
        launch.AddRow("Launch Options", _gameArgs, "Extra options for the game window");
        var reset = new PillButton("Reset");
        reset.Click += (_, _) => ResetSettings();
        launch.AddRow("Reset Settings", reset, "Back to the defaults (keeps friends and launch options)");

        var retakes = new Group(left);
        _retakeOff = retakes.AddRow("Not in this mode", null, "Choose Retakes on the Game tab to use these.");
        _retakeStyleRow = retakes.AddRow("Style", _retakeStyle, "Valve's cards, or the cs2-retakes plugin");
        _valveNote = retakes.AddRow("Valve", null, "3 terrorists vs 4 counter-terrorists. Loadout cards each round.");
        _pluginRows.Add(retakes.AddRow("Max Players", _retakePlayers, "Seats in the match, including you (2-10)"));
        _pluginRows.Add(retakes.AddRow("Terrorists", _retakeRatio, "Percent of the players on T"));
        _pluginRows.Add(retakes.AddRow("Scramble", _retakeScramble, "Mix the teams after a win streak"));
        _pluginRows.Add(retakes.AddRow("Scramble After", _retakeScrambleRounds, "Rounds won in a row"));
        _pluginRows.Add(retakes.AddRow("Balance Teams", _retakeBalance, "Keep the T and CT counts even"));
        _pluginRows.Add(retakes.AddRow("Auto Plant", _retakePlant, "The bomb is planted as the round starts"));
        _pluginRows.Add(retakes.AddRow("Announce Site", _retakeAnnounce, "Show which site the bomb is on"));
        _pluginRows.Add(retakes.AddRow("Join Straight In", _retakeJoin, "Skip the team menu"));
        _pluginRows.Add(retakes.AddRow("Buy Before Round", _retakeBuy, "You buy once per side. Later rounds equip those guns"));
        _pluginRows.Add(retakes.AddRow("Buy Time", _retakeBuySeconds, "Seconds to buy before you can move"));
        _pluginRows.Add(retakes.AddRow("Bomb Timer", _retakeBomb, "Seconds until the bomb explodes"));
        _retakeStyle.SelectedChanged += (_, _) => UpdateRetakeEditor();
        _retakeScramble.CheckedChanged += (_, _) => UpdateRetakeEditor();
        _retakeBuy.CheckedChanged += (_, _) => UpdateRetakeEditor();

        var damage = new PillButton("Damage") { AutoWidth = false, Width = 118, Margin = new Padding(0, 0, 8, 8) };
        damage.Click += (_, _) =>
        {
            _showDamage.Checked = !_showDamage.Checked;
            var settings = ReadServerSettings();
            if (_app.Game == null) return;
            settings.WriteCfg(_app.Game);
            Send($"lst_show_damage {(settings.ShowDamage ? 1 : 0)}");
        };
        _liveControls.Add(damage);
        var roundButtons = ButtonRow(left,
            Action("Restart", "mp_restartgame 1"),
            Action("End Warmup", "mp_warmup_end"),
            Action("Pause", "lst_pause on"),
            Action("Resume", "lst_pause off"),
            Action("Swap", "lst_swap"),
            Action("CT Win", "lst_endround ct"),
            Action("T Win", "lst_endround t"));
        var playerButtons = ButtonRow(left,
            Action("Add CT", "bot_add_ct"),
            Action("Add T", "bot_add_t"),
            Action("Kick Bots", "bot_kick"),
            Action("Respawn", "lst_respawn"),
            Action("God", "lst_god"));
        var practiceButtons = ButtonRow(left,
            Action("Third Person", "lst_view third"),
            Action("First Person", "lst_view first"),
            Action("Noclip", "lst_noclip"),
            Action("Armor", "lst_armor"),
            Action("Nades", "lst_nades"),
            Action("Clear", "lst_clear"),
            Action("Rethrow", "lst_rethrow"),
            Action("Save Spot", "lst_spot save"),
            Action("Load Spot", "lst_spot load"),
            Action("Refresh", "lst_refresh"),
            damage);

        _giveButton.Click += (_, _) => Send($"lst_give {((GiveItem)_giveBox.SelectedItem!).Classname}");
        _command.Margin = new Padding(0, 0, 8, 0);
        _runCommand.Click += (_, _) => RunCommand();
        _command.Box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            RunCommand();
        };
        _liveControls.Add(_giveBox);
        _liveControls.Add(_giveButton);
        _liveControls.Add(_command);
        _liveControls.Add(_runCommand);
        foreach (var control in _liveControls) control.Enabled = false;
        var roundGroup = new Group(left);
        roundGroup.AddContent(roundButtons, new Padding(12, 12, 4, 4));
        var playerGroup = new Group(left);
        playerGroup.AddContent(playerButtons, new Padding(12, 12, 4, 4));
        var practiceGroup = new Group(left);
        practiceGroup.AddContent(practiceButtons, new Padding(12, 12, 4, 4));
        var giveGroup = new Group(left);
        _giveBox.Margin = new Padding(0, 0, 8, 0);
        giveGroup.AddRow("Give Weapon", new HStack(_giveBox, _giveButton));
        giveGroup.AddRow("Command", new HStack(_command, _runCommand), "Server console · Enter sends it");
        var liveHint = new SmoothLabel
        {
            Text = "Round, players and practice. They light up when the server is up.",
            Font = Theme.Small,
            ForeColor = Theme.Muted,
            AutoSize = true,
            MaximumSize = new Size(left - 4, 0),
            Margin = new Padding(2, 0, 0, 10),
            BackColor = Color.Transparent,
        };
        var liveTab = new VStack(
            liveHint,
            Theme.Caption("ROUND"), roundGroup,
            Theme.Caption("PLAYERS"), playerGroup,
            Theme.Caption("PRACTICE"), practiceGroup,
            giveGroup);

        var tabPages = new Control[] { game, bots, options, launch, retakes, liveTab };
        var tabHost = new VStack(tabPages) { MinimumSize = new Size(left, 0) };
        void ShowTab(int index)
        {
            // Swap without repainting in between, and keep the height fixed so the page doesn't jump.
            using (Theme.FreezePainting(this))
            {
                tabHost.SuspendLayout();
                for (var i = 0; i < tabPages.Length; i++) tabPages[i].Visible = i == index;
                tabHost.ResumeLayout(true);
            }
        }
        _tabs.SelectedIndex = Math.Clamp(_app.Settings.PlayTab, 0, tabPages.Length - 1);
        ShowTab(_tabs.SelectedIndex);
        _tabs.SelectedChanged += (_, _) =>
        {
            ShowTab(_tabs.SelectedIndex);
            _app.Settings.PlayTab = _tabs.SelectedIndex;
        };
        _tabs.Margin = new Padding(0, 0, 0, 10);

        var leftColumn = new VStack(Theme.Caption("SERVER"), _tabs, tabHost) { Margin = new Padding(0, 0, 28, 0) };

        var live = new Group(right);
        _liveRow = live.AddRow("Server not running", null, "Press Start, or open the Live tab once it's up.");
        var activity = new Group(right);
        activity.AddContent(_log, new Padding(12, 12, 12, 12));
        var rightColumn = new VStack(
            Theme.Caption("STATUS"), live,
            Theme.Caption("ACTIVITY"), activity);

        var page = new VStack(
            Header("Play", "Start a local server, then run the match from the Live tab."),
            _hero,
            Theme.Caption("QUICK START"), quick,
            new HStack(leftColumn, rightColumn));
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        LoadServerSettings();
        _modeBox.SelectedIndexChanged += (_, _) => OnModeChanged();
        VisibleChanged += (_, _) => { if (Visible) UpdateHackerRow(); };
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
        var t = new SmoothLabel { Text = title, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 0) };
        var s = new SmoothLabel { Text = subtitle, Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(4, 2, 0, 16) };
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
        // Style first: picking the mode refreshes the Retakes tab, and plugin style must already be on.
        _retakeStyle.SelectedIndex = s.RetakeStyle == 1 ? 1 : 0;
        _retakePlayers.Value = Math.Clamp(s.RetakeMaxPlayers, 2, 10);
        _retakeRatio.Value = Math.Clamp(s.RetakeTerroristPercent, 10, 80);
        _retakeScramble.Checked = s.RetakeScramble;
        _retakeScrambleRounds.Value = Math.Clamp(s.RetakeScrambleRounds, 1, 30);
        _retakeBalance.Checked = s.RetakeBalance;
        _retakePlant.Checked = s.RetakeAutoPlant;
        _retakeAnnounce.Checked = s.RetakeAnnounce;
        _retakeJoin.Checked = s.RetakeAutoJoin;
        _retakeBuy.Checked = s.RetakeBuy;
        _retakeBuySeconds.Value = Math.Clamp(s.RetakeBuySeconds, 4, 20);
        _retakeBomb.Value = Math.Clamp(s.RetakeBombTimer, 15, 60);
        // Match by name. GetMode() returns a copy for plugin retakes, and that copy is not in the list.
        SelectCatalogMode(s.Mode);
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
        _showDamage.Checked = s.ShowDamage;
        _workshop.Text = s.WorkshopMap;
        _gameArgs.Text = s.GameArgs;
        _autoStartGame.Checked = s.AutoStartGame;
        _ffa.Enabled = s.GetMode().Name == "Deathmatch";
        UpdateHackerRow();
        UpdateRetakeEditor();
    }

    private void UpdateRetakeEditor()
    {
        var mode = ((GameMode?)_modeBox.SelectedItem)?.Name;
        var on = mode == ServerSettings.RetakesMode;
        var plugin = on && _retakeStyle.SelectedIndex == 1;
        _retakeOff.Visible = !on;
        _retakeStyleRow.Visible = on;
        _valveNote.Visible = on && !plugin;
        foreach (var row in _pluginRows) row.Visible = plugin;
        _retakeScrambleRounds.Enabled = _retakeScramble.Checked;
        _retakeBuySeconds.Enabled = _retakeBuy.Checked;
        _botTeam.Enabled = !on;
        _botTeamRow.Subtitle = plugin
            ? "The plugin splits the teams. This picker is ignored."
            : on ? "Valve fills both sides (3 T vs 4 CT)."
            : "0 bots = friends only";
        // These rules are skipped on both retake styles, so the switches go quiet.
        _maxMoney.Enabled = !on;
        _noRoundEnd.Enabled = !on;
        _roundTime.Enabled = !on;
        _ffa.Enabled = mode == "Deathmatch";
        _moneyRow.Subtitle = on ? "Retakes handles money itself" : "And buy anywhere";
        _roundsRow.Subtitle = on ? "Retakes ends the round on its own" : "Rounds never end, respawn on death";
        _timeRow.Subtitle = on ? "Retakes keeps its own round length" : "Minutes · 0 = game mode default";
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
        s.ShowDamage = _showDamage.Checked;
        s.RetakeStyle = _retakeStyle.SelectedIndex == 1 ? 1 : 0;
        s.RetakeMaxPlayers = (int)_retakePlayers.Value;
        s.RetakeTerroristPercent = (int)_retakeRatio.Value;
        s.RetakeScramble = _retakeScramble.Checked;
        s.RetakeScrambleRounds = (int)_retakeScrambleRounds.Value;
        s.RetakeBalance = _retakeBalance.Checked;
        s.RetakeAutoPlant = _retakePlant.Checked;
        s.RetakeAnnounce = _retakeAnnounce.Checked;
        s.RetakeAutoJoin = _retakeJoin.Checked;
        s.RetakeBuy = _retakeBuy.Checked;
        s.RetakeBuySeconds = (int)_retakeBuySeconds.Value;
        s.RetakeBombTimer = (int)_retakeBomb.Value;
        s.WorkshopMap = _workshop.Text.Trim();
        s.GameArgs = _gameArgs.Text.Trim();
        s.AutoStartGame = _autoStartGame.Checked;
        _app.Settings.Save();
        return s;
    }

    public void UpdateHackerRow()
    {
        var s = _app.Settings.Server;
        _hackerRow.Subtitle = s.HackerMode switch
        {
            1 => "On · everyone hacks",
            2 => "On · one random hacker per round",
            _ => "Off · aimbot, wallhack, radar and more",
        };
        _hackerRow.SubtitleColor = s.HackerMode != 0 ? Theme.Good : Theme.Muted;
    }

    /// <summary>Selects the mode row by name. A copied <see cref="GameMode"/> would miss the list and clear the picker.</summary>
    private void SelectCatalogMode(string name)
    {
        for (var i = 0; i < _modeBox.Items.Count; i++)
        {
            if (_modeBox.Items[i] is GameMode mode && mode.Name == name)
            {
                _modeBox.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnModeChanged()
    {
        if (_modeBox.SelectedItem is not GameMode mode || mode.Maps is not { Length: > 0 }) return;
        var keep = mode.Maps.Contains(_mapBox.Text) ? _mapBox.Text : mode.Maps[0];
        FillMaps();
        _mapBox.Text = keep;
        UpdateRetakeEditor();
    }

    private void FillMaps()
    {
        if (_modeBox.SelectedItem is not GameMode mode) return;
        _mapBox.SetItems(mode.Maps);
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
        if (_modeBox.Items.Count != ServerSettings.Modes.Length)
        {
            // New modes found in the game files (Rush).
            _modeBox.Items.Clear();
            foreach (var m in ServerSettings.Modes) _modeBox.Items.Add(m);
            SelectCatalogMode(_app.Settings.Server.Mode);
        }
        var on = _app.TestModeOn;
        var ready = _app.Ready;
        _startButton.Enabled = ready && !on;
        _applyButton.Enabled = _stopButton.Enabled = on;
        _gameButton.Enabled = on;

        _modeTitle.Text = on ? "Server running" : ready ? "Ready to play" : "Setup needed";
        _modeTitle.ForeColor = !ready ? Theme.Warning : Theme.Text;
        if (_hero.Running != on)
        {
            _hero.Running = on;
            _hero.Invalidate(true);
        }
        _modeStatus.Text = on
            ? "VAC is off until you press Stop. Only start CS2 from here until then - starting it from Steam could get you VAC-kicked."
            : ready ? "Pick a quick start or press Start. The server opens in its own window and CS2 joins it."
            : "Open Setup and press \"Install Everything\" first.";
        RefreshLive();
        UpdateWorkshopRow();
    }

    /// <summary>Places the hero's text and buttons (in real pixels, so it follows screen scaling).</summary>
    private void LayoutHero()
    {
        int S(int v) => Theme.S(v);
        var w = _hero.Width;
        var rightX = w - S(352);
        _modeTitle.Location = new Point(S(24), S(18));
        _inviteTitle.Location = new Point(rightX, S(22));
        _inviteText.MaximumSize = new Size(w - rightX - S(24), 0);
        _inviteText.Location = new Point(rightX, _inviteTitle.Bottom + S(4));
        _modeStatus.MaximumSize = new Size(Math.Max(S(200), rightX - S(48)), 0);
        _modeStatus.Location = new Point(S(26), _modeTitle.Bottom + S(4));
        _heroLive.Location = new Point(S(26), _modeStatus.Bottom + S(4));
        var textBottom = _heroLive.Text != "" ? _heroLive.Bottom : _modeStatus.Bottom;
        var inviteBottom = _inviteText.Bottom;

        // Grow the banner when the text needs more room, so it never runs into the buttons.
        var needed = Math.Max(textBottom, inviteBottom) + S(18) + _startButton.Height + S(20);
        var height = Math.Max(S(176), needed);
        if (_hero.Height != height)
        {
            _hero.Height = height;
            return; // the resize lays it out again
        }
        var x = S(24);
        var y = _hero.Height - _startButton.Height - S(20);
        foreach (var b in new Control[] { _startButton, _applyButton, _gameButton, _stopButton })
        {
            b.Location = new Point(x, y);
            x += b.Width + S(10);
        }

        _copyIp.Location = new Point(rightX, _hero.Height - _copyIp.Height - S(20));
        _copyInvite.Location = new Point(_copyIp.Right + S(10), _copyIp.Top);
    }

    private void ResetSettings()
    {
        if (MessageBox.Show(this, "Put all game, bot and rule settings back to the defaults?", "Reset Settings",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        var old = _app.Settings.Server;
        _app.Settings.Server = new ServerSettings
        {
            Friends = old.Friends, Password = old.Password, SharedSkins = old.SharedSkins,
            GameArgs = old.GameArgs, AutoStartGame = old.AutoStartGame, AimBindKey = old.AimBindKey,
        };
        _app.Settings.Save();
        LoadServerSettings();
        Toast.Show("Settings reset.");
    }

    private void RefreshInviteRow()
    {
        var s = _app.Settings.Server;
        var address = FriendsInvite.HostAddress(s);
        _copyIp.Enabled = _copyInvite.Enabled = address != null;
        var how = s.Friends switch
        {
            ServerSettings.FriendsNetwork.Internet => "Internet",
            ServerSettings.FriendsNetwork.Vpn => "Radmin VPN",
            _ => "same Wi-Fi",
        };
        _inviteText.Text = address != null ? $"{address} · {how} · change on the Friends page"
            : s.Friends == ServerSettings.FriendsNetwork.Internet ? "Router address shows after Start"
            : $"No {how} address found · see the Friends page";
    }

    private void CopyToClipboard(string? text, string message)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            Clipboard.SetText(text);
            Toast.Show(message);
        }
        catch (Exception ex)
        {
            _app.Log("Could not copy: " + ex.Message);
        }
    }

    /// <summary>Plugin retakes runs as competitive (0/1). Show Retakes while that style is the one we started.</summary>
    private string ModeName(ServerStatus status, bool withFallback)
    {
        var pluginSession = _runningRetakeKey is { } key && key is not ("valve" or "off")
            || (_runningRetakeKey == null && _app.Game != null
                && Installer.ThirdPartyRetakesLoaded(_app.Game) && _app.Settings.Server.IsPluginRetakes);
        if (status.GameType == 0 && status.GameMode == 1 && pluginSession)
            return "Retakes";
        var known = ServerSettings.Modes.FirstOrDefault(m => m.GameType == status.GameType && m.Mode == status.GameMode)?.Name;
        if (!string.IsNullOrEmpty(known)) return known;
        return withFallback ? $"mode {status.GameType}/{status.GameMode}" : "";
    }

    private string HeroLine(ServerStatus status)
    {
        var mode = ModeName(status, withFallback: false);
        return $"● {status.Map}{(mode != "" ? " · " + mode : "")} · {status.Humans} player{(status.Humans == 1 ? "" : "s")} · {status.Bots} bot{(status.Bots == 1 ? "" : "s")}";
    }

    private void RefreshLive()
    {
        RefreshInviteRow();
        var status = _app.ReadServerStatus();
        HandleWorkshopStartup(status);
        var running = status != null;
        foreach (var control in _liveControls) control.Enabled = running;

        _heroLive.Text = status == null ? "" : HeroLine(status);
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

        var mode = ModeName(status, withFallback: true);
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

    private Control PresetButton(ServerPreset preset, Color tint, bool canDelete = false)
    {
        var card = new PresetCard(preset.Title, preset.Subtitle, tint) { CanDelete = canDelete, Width = _presetCardWidth };
        card.Click += (_, _) => RunPreset(preset);
        if (canDelete) card.DeleteRequested += (_, _) => DeleteSetup(preset.Title);
        return card;
    }

    private void RebuildQuickStarts()
    {
        _app.Settings.SavedSetups ??= [];
        _presets.SuspendLayout();
        var old = _presets.Controls.Cast<Control>().ToArray();
        _presets.Controls.Clear();
        foreach (var control in old) control.Dispose();
        for (var i = 0; i < ServerSettings.Presets.Length; i++)
        {
            var card = PresetButton(ServerSettings.Presets[i], PresetTints[i % PresetTints.Length]);
            _tips.SetToolTip(card, ServerSettings.Presets[i].Subtitle);
            _presets.Controls.Add(card);
        }
        foreach (var setup in _app.Settings.SavedSetups.Where(s => !string.IsNullOrWhiteSpace(s.Name) && s.Settings != null))
        {
            var preset = new ServerPreset(setup.Name, setup.Settings.Summary(), s => s.CopyPracticeFrom(setup.Settings));
            var card = PresetButton(preset, SavedTint, canDelete: true);
            _tips.SetToolTip(card, "Your setup · " + preset.Subtitle + " · click × to delete");
            _presets.Controls.Add(card);
        }
        _presets.ResumeLayout(true);
    }

    private void SaveSetup()
    {
        using var prompt = new Prompt("Save setup", "Name this quick start");
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        var name = prompt.Value.Trim();
        if (name.Length > 24) name = name[..24];
        if (ServerSettings.Presets.Any(p => p.Title.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            Toast.Show("That name is already a built-in quick start.", Toast.Kind.Warning);
            return;
        }

        var snap = ReadServerSettings().Clone();
        _app.Settings.SavedSetups ??= [];
        var list = _app.Settings.SavedSetups;
        var existing = list.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            if (MessageBox.Show(this, $"Replace the saved setup \"{name}\"?", "Save setup",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            list[existing] = new SavedSetup { Name = name, Settings = snap };
        }
        else list.Add(new SavedSetup { Name = name, Settings = snap });

        _app.Settings.Save();
        RebuildQuickStarts();
        _app.Log($"Saved setup \"{name}\".");
        Toast.Show($"Saved setup \"{name}\".");
    }

    private void DeleteSetup(string name)
    {
        if (MessageBox.Show(this, $"Delete the saved setup \"{name}\"?", "Delete setup",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        _app.Settings.SavedSetups ??= [];
        _app.Settings.SavedSetups.RemoveAll(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        _app.Settings.Save();
        RebuildQuickStarts();
        Toast.Show($"Deleted \"{name}\".");
    }

    private void RunPreset(ServerPreset preset)
    {
        if (preset.Title.StartsWith(ServerSettings.RushMode) && ServerSettings.Modes.All(m => m.Name != ServerSettings.RushMode))
        {
            MessageBox.Show(this, "Rush wasn't found in your CS2 files. Update CS2 in Steam (Rush came with the Rush Hour update), " +
                                  "then restart the launcher.", "Rush", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        preset.Apply(_app.Settings.Server);
        LoadServerSettings();
        _app.Log($"Quick start: {preset.Title}");

        if (_app.TestModeOn) ApplyToServer();
        else if (_app.Ready) _ = StartServerAsync();
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
            _runningRetakeKey = server.PluginRetakeKey();
            var map = server.WorkshopMapId != null ? $"Workshop map {server.WorkshopMapId}" : server.Map;
            if (server.WorkshopNeedsDownload)
                _app.Log("Workshop map: the server goes online (unlisted) and loads it from the Workshop - the first time takes " +
                         "longer while it downloads. Players' games fetch it automatically when they join.");
            var startedAs = server.IsPluginRetakes ? "Plugin retakes" : server.Mode;
            _app.Log($"Started {startedAs} on {map} with {server.Bots} {ServerSettings.Difficulties[server.Difficulty].ToLowerInvariant()} bots.");
            Toast.Show($"Starting {startedAs} on {map}…", Toast.Kind.Info);
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
        var game = _app.Game;
        if (game == null) return;
        var server = ReadServerSettings();
        var map = server.WorkshopMapId ?? server.Map;
        var reload = _runningMode == null || _runningMode != server.Mode || _runningMap != map;
        if (RetakesNeedRestart(game, server))
        {
            var why = Installer.ThirdPartyRetakesLoaded(game) != server.IsPluginRetakes
                ? "The retakes plugin has to load or unload, so the server needs a restart."
                : "These retakes settings are read when the plugin loads, so the server needs a restart.";
            if (MessageBox.Show(this, why + " Continue?", "Retakes",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            _app.Log("Restarting so the retakes plugin matches these settings.");
            _ = RestartServerAsync();
            return;
        }
        if (_runningRetakeKey == null) _runningRetakeKey = server.PluginRetakeKey();

        try
        {
            var warning = server.ApplyToRunningServer(game, reload, _runningOnline, _runningForFriends);
            if (warning != null)
            {
                _app.Log(warning);
                Toast.Show("Workshop map not downloaded - see Activity.", Toast.Kind.Warning);
                return;
            }
            _runningMode = server.Mode;
            _runningMap = map;
            _runningRetakeKey = server.PluginRetakeKey();
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
            _runningMode = _runningMap = _runningRetakeKey = null;
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
    /// <summary>
    /// Every way of starting the server goes through here (Start, F5, quick starts, the Friends page).
    /// In "Internet" friends mode it first opens the port on the router (UPnP) and makes sure there's a
    /// password - before, only the Friends page's Start Hosting did that, so starting from here left
    /// the router closed and friends couldn't connect. Any friends mode also counts as hosting friends.
    /// </summary>
    public async Task StartServerAsync(bool forFriends = false)
    {
        if (_app.Game == null) return;
        var server = ReadServerSettings();
        if (server.Friends != ServerSettings.FriendsNetwork.Lan) forFriends = true;

        if (server.Friends == ServerSettings.FriendsNetwork.Internet)
        {
            // Open to the whole internet: always use a password.
            if (string.IsNullOrWhiteSpace(server.Password))
            {
                server.Password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
                _app.Settings.Save();
                _app.Log($"Internet mode: set password \"{server.Password}\" (it's in the Friends page invite).");
            }

            _startButton.Enabled = false;
            var error = await RouterPort.OpenAsync(_app.Log);
            _startButton.Enabled = true;
            if (error != null &&
                MessageBox.Show(this, error + "\n\nStart the server anyway? (Friends can only join if you forward UDP port " +
                                      $"{ServerSettings.Port} to this PC in your router yourself.)",
                    "Couldn't open the port", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }
        if (!await PrepareRetakesAsync(server)) return;
        StartTest(forFriends);
    }

    /// <summary>
    /// Downloads B3none's cs2-retakes the first time Plugin style is used, then loads or parks it.
    /// The plugin reads its JSON when it loads, so the file is written before the server starts.
    /// </summary>
    private async Task<bool> PrepareRetakesAsync(ServerSettings server)
    {
        var game = _app.Game;
        if (game == null) return false;
        if (server.IsPluginRetakes && !Installer.RetakesInstalled(game))
        {
            if (TestSession.Cs2Running())
            {
                MessageBox.Show(this, "Close CS2 first, then start again. The retakes plugin installs into the game folder.",
                    "Retakes plugin", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            _startButton.Enabled = false;
            try
            {
                await new Installer(game, _app.Http, _app.Log).InstallRetakesAsync();
            }
            catch (Exception ex)
            {
                _app.Log("Could not download the retakes plugin: " + ex.Message);
                MessageBox.Show(this, "The retakes plugin could not be downloaded.\n\n" + ex.Message +
                                      "\n\nCheck your internet connection, or switch Style to Valve.",
                    "Retakes plugin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            finally
            {
                _startButton.Enabled = _app.Ready && !_app.TestModeOn;
            }
        }

        // A running CS2 refuses to start anyway. Leave its plugin folder alone until it is closed.
        if (TestSession.Cs2Running()) return true;
        try
        {
            Installer.SetRetakesActive(game, server.IsPluginRetakes);
            if (server.IsPluginRetakes) server.WriteRetakesConfig(game);
        }
        catch (Exception ex)
        {
            _app.Log("Could not set up the retakes plugin: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Retakes plugin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    /// <summary>
    /// True when the running server must restart. Loading or unloading the plugin, and the plugin's
    /// JSON settings, only take effect on the next start. Buy time and the bomb timer do not.
    /// A launcher that just opened onto an already-running server does not restart on its own.
    /// </summary>
    private bool RetakesNeedRestart(GameFolder game, ServerSettings server)
    {
        var loaded = Installer.ThirdPartyRetakesLoaded(game);
        var want = server.IsPluginRetakes;
        var key = server.PluginRetakeKey();
        if (_runningRetakeKey == null && loaded == want) return false;
        var pluginSettingsChanged = _runningRetakeKey != null && _runningRetakeKey != key
            && (want || _runningRetakeKey is not ("valve" or "off"));
        return loaded != want || pluginSettingsChanged;
    }

    /// <summary>Stops the running server, waits for CS2 to close, then starts again with the current settings.</summary>
    private async Task RestartServerAsync()
    {
        if (_app.Game == null) return;
        new TestSession(_app.Game).Stop();
        _runningMode = _runningMap = null;
        _app.NotifyChanged();
        for (var i = 0; i < 20 && TestSession.Cs2Running(); i++) await Task.Delay(500);
        await StartServerAsync(_runningForFriends);
    }

    /// <summary>F5: start, or apply the settings to the running server.</summary>
    public void StartOrApply()
    {
        if (_app.TestModeOn) ApplyToServer();
        else if (_app.Ready) _ = StartServerAsync();
        else Toast.Show("Install first (Setup page).", Toast.Kind.Warning);
    }

    /// <summary>CS2 and the server were closed without Stop: restore quietly.</summary>
    public void AutoRestore()
    {
        if (_app.Game == null) return;
        new TestSession(_app.Game).Stop();
        _runningMode = _runningMap = _runningRetakeKey = null;
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

    private void RunCommand()
    {
        var command = (_command.Text ?? "").Replace("\r", "").Split('\n')[0].Trim();
        if (command.Length == 0) return;
        _command.Text = "";
        Send(command);
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
        var b = new PillButton(text) { AutoWidth = false, Width = 118, Margin = new Padding(0, 0, 8, 8) };
        b.Click += (_, _) => Send(command);
        _liveControls.Add(b);
        return b;
    }

    /// <summary>A wrapping row of live-server buttons, sized to the settings column.</summary>
    private static FlowLayoutPanel ButtonRow(int width, params Control[] buttons)
    {
        const int innerPad = 24;
        var inner = Math.Max(120, width - innerPad);
        var sample = buttons[0];
        var cellW = Math.Max(1, sample.Width + sample.Margin.Horizontal);
        var cellH = Math.Max(1, sample.Height + sample.Margin.Vertical);
        var perRow = Math.Max(1, inner / cellW);
        var rows = (buttons.Length + perRow - 1) / perRow;
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false,
            Width = inner,
            Height = rows * cellH,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        panel.Controls.AddRange(buttons);
        return panel;
    }
}
