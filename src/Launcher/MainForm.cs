using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;
using Microsoft.Win32;

namespace LanSkinTester.Launcher;

public class MainForm : Form
{
    private readonly LauncherSettings _settings = LauncherSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly ImageCache _images;
    private ItemDatabase _db = ItemDatabase.LoadCached(ItemDatabase.DataDir);
    private GameFolder? _game;

    // What the running server was started with, to know if "Apply" needs a map change.
    private string? _runningMode;
    private string? _runningMap;

    // Top bar
    private readonly Button _navPlay = Theme.MakeButton("PLAY", Theme.ButtonKind.Ghost, 110);
    private readonly Button _navSkins = Theme.MakeButton("SKINS", Theme.ButtonKind.Ghost, 110);
    private readonly Label _modePill = new() { AutoSize = true, Font = Theme.Bold, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(0, 12, 16, 0) };
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly Panel _playPage = new() { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(18), AutoScroll = true };
    private readonly SkinsPage _skinsPage;

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
    private readonly Button _startButton = Theme.MakeButton("START", Theme.ButtonKind.Primary, 130);
    private readonly Button _applyButton = Theme.MakeButton("Apply changes", Theme.ButtonKind.Normal, 150);
    private readonly Button _stopButton = Theme.MakeButton("Stop && restore CS2", Theme.ButtonKind.Danger, 180);
    private readonly Label _modeStatus = new() { AutoSize = true, MaximumSize = new Size(470, 0), Font = Theme.Bold, Margin = new Padding(0, 10, 0, 0) };

    // Setup
    private readonly TextBox _pathBox = new() { ReadOnly = true, Width = 330 };
    private readonly Label _metamodStatus = Theme.Label("");
    private readonly Label _cssStatus = Theme.Label("");
    private readonly Label _pluginStatus = Theme.Label("");
    private readonly Label _dataStatus = Theme.Label("");
    private readonly Button _installButton = Theme.MakeButton("Install / update everything", Theme.ButtonKind.Normal);
    private readonly Button _zipButton = Theme.MakeButton("Install from zip...", Theme.ButtonKind.Normal);
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 150, Width = 430, Font = Theme.Small };

    public MainForm()
    {
        Text = "LAN Skin Tester";
        Font = Theme.Body;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Size = new Size(1320, 860);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        _images = new ImageCache(_http);
        _skinsPage = new SkinsPage(_images) { Dock = DockStyle.Fill };

        BuildPlayPage();
        _pages.Controls.Add(_playPage);
        _pages.Controls.Add(_skinsPage);
        Controls.Add(_pages);
        Controls.Add(BuildTopBar());

        Theme.Apply(this);
        ShowPage(_playPage);

        Load += (_, _) =>
        {
            SetGameFolder(GameFolder.IsValid(_settings.GamePath)
                ? _settings.GamePath
                : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
            if (_db.IsEmpty) Log("Skin, sticker and charm lists are not downloaded yet - press \"Install / update everything\".");
        };
        FormClosing += OnClosing;
    }

    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    private Control BuildTopBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Surface };
        var title = new Label { Text = "LAN SKIN TESTER", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(20, 14) };
        var nav = new FlowLayoutPanel { AutoSize = true, Location = new Point(260, 12), BackColor = Theme.Surface };
        _navPlay.Height = _navSkins.Height = 34;
        nav.Controls.AddRange([_navPlay, _navSkins]);
        _navPlay.Click += (_, _) => ShowPage(_playPage);
        _navSkins.Click += (_, _) => ShowPage(_skinsPage);

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, BackColor = Theme.Surface, FlowDirection = FlowDirection.RightToLeft };
        right.Controls.Add(_modePill);

        bar.Controls.Add(right);
        bar.Controls.Add(nav);
        bar.Controls.Add(title);
        return bar;
    }

    private void ShowPage(Control page)
    {
        page.BringToFront();
        page.Visible = true;
        foreach (Control other in _pages.Controls) if (other != page) other.Visible = false;

        foreach (var (button, target) in new[] { (_navPlay, (Control)_playPage), (_navSkins, _skinsPage) })
        {
            var on = target == page;
            button.ForeColor = on ? Theme.Accent : Theme.Muted;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = on ? Theme.Card : Theme.Surface;
        }
        if (page == _skinsPage) _skinsPage.Reload();
    }

    private void BuildPlayPage()
    {
        // ---- Server column ----
        foreach (var m in ServerSettings.Modes) _modeBox.Items.Add(m);
        _botTeam.Items.AddRange(ServerSettings.BotTeams);
        _botWeapons.Items.AddRange(ServerSettings.BotWeaponChoices);
        LoadServerSettings();

        _modeBox.SelectedIndexChanged += (_, _) => OnModeChanged();
        _botsBar.ValueChanged += (_, _) => _botsLabel.Text = _botsBar.Value == 0 ? "No bots" : $"{_botsBar.Value} bots";

        var server = Stack(
            Row(Field("Game mode", _modeBox), Field("Map", _mapBox)),
            Field("Bots", Row(_botsBar, _botsLabel)),
            Field("Bot skill", _difficulty),
            Row(Field("Bot team", _botTeam), Field("Bot weapons", _botWeapons)),
            Theme.Label("Options", Theme.Bold, Theme.Muted),
            _ffa, _maxMoney, _infiniteAmmo, _noRoundEnd, _botsDontShoot, _cheats);

        _startButton.Height = _applyButton.Height = _stopButton.Height = 40;
        _startButton.Click += (_, _) => StartTest();
        _applyButton.Click += (_, _) => ApplyToServer();
        _stopButton.Click += (_, _) => StopTest();

        var play = Stack(
            Row(_startButton, _applyButton, _stopButton),
            _modeStatus,
            new Label
            {
                AutoSize = true, MaximumSize = new Size(470, 0), ForeColor = Theme.Muted, Margin = new Padding(0, 8, 0, 0),
                Text = "START opens a local server window and CS2 (VAC off) that joins it. Change bots or options any time and " +
                       "press \"Apply changes\" - no restart needed. Pick skins on the SKINS page - they change in game right away. " +
                       "When you're done, press Stop: it closes both and puts CS2 back to normal for matchmaking.",
            });

        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 18, 0) };
        left.Controls.Add(Card("Server", server, 520));
        left.Controls.Add(Card("Play", play, 520));

        // ---- Setup column ----
        var browse = Theme.MakeButton("Browse...");
        browse.Click += (_, _) => BrowseForGame();
        _installButton.Click += async (_, _) => await InstallEverything();
        _zipButton.Click += async (_, _) => await InstallFromZip();

        var setup = Stack(
            Field("CS2 folder", Row(_pathBox, browse)),
            _metamodStatus, _cssStatus, _pluginStatus, _dataStatus,
            Row(_installButton, _zipButton),
            Row(Link("Metamod downloads", Installer.MetamodPage), Link("CounterStrikeSharp downloads", Installer.CounterStrikeSharpPage)));

        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        right.Controls.Add(Card("Setup", setup, 470));
        right.Controls.Add(Card("Log", _log, 470));

        var columns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Background };
        columns.Controls.AddRange([left, right]);
        _playPage.Controls.Add(columns);
    }

    private void LoadServerSettings()
    {
        var s = _settings.Server;
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
        _ffa.Enabled = s.GetMode().Name == "Deathmatch";
    }

    private ServerSettings ReadServerSettings()
    {
        var s = _settings.Server;
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
        _settings.Save();
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
    // Game folder + status
    // ---------------------------------------------------------------------------------------------

    private void SetGameFolder(string? path)
    {
        if (!GameFolder.IsValid(path))
        {
            _game = null;
            _pathBox.Text = "CS2 not found - press Browse";
            RefreshStatus();
            return;
        }

        _game = new GameFolder(path!);
        _pathBox.Text = _game.Root;
        _settings.GamePath = _game.Root;
        _settings.Save();

        // Keep the plugin's skin names in sync with the launcher's data (e.g. after picking another folder).
        if (!_db.IsEmpty && _game.PluginInstalled && !File.Exists(Path.Combine(_game.SkinNamesDir, "skins.json")))
            _db.WritePluginSkinNames(_game.SkinNamesDir);

        RefreshStatus();
    }

    private void BrowseForGame()
    {
        using var dialog = new FolderBrowserDialog { Description = "Pick the \"Counter-Strike Global Offensive\" folder (Steam > CS2 > Manage > Browse local files)" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (!GameFolder.IsValid(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder doesn't contain game\\csgo\\gameinfo.gi. Pick the \"Counter-Strike Global Offensive\" folder.",
                "Not a CS2 folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SetGameFolder(dialog.SelectedPath);
    }

    private void RefreshStatus()
    {
        static void Mark(Label label, bool ok, string name, string missing)
        {
            label.Text = ok ? $"●  {name}" : $"○  {name} - {missing}";
            label.ForeColor = ok ? Theme.Good : Theme.Bad;
        }

        var g = _game;
        Mark(_metamodStatus, g?.MetamodInstalled == true, "Metamod:Source", "not installed");
        Mark(_cssStatus, g?.CounterStrikeSharpInstalled == true, "CounterStrikeSharp", "not installed");
        Mark(_pluginStatus, g?.PluginInstalled == true, "LAN Skin Tester plugin", "not installed");
        Mark(_dataStatus, !_db.IsEmpty, _db.IsEmpty ? "Skin / sticker lists" : $"{_db.Skins.Count:N0} skins, {_db.Stickers.Count:N0} stickers, {_db.Charms.Count} charms",
            "not downloaded");

        var ready = g is { MetamodInstalled: true, CounterStrikeSharpInstalled: true, PluginInstalled: true };
        var on = g != null && new TestSession(g).IsOn;

        _installButton.Enabled = _zipButton.Enabled = g != null;
        _startButton.Enabled = ready && !on;
        _applyButton.Enabled = on;
        _stopButton.Enabled = on;

        _modePill.Text = on ? "TEST MODE ON" : "CS2 NORMAL";
        _modePill.BackColor = on ? Theme.Accent : Theme.Card;
        _modePill.ForeColor = on ? Color.FromArgb(20, 16, 8) : Theme.Good;

        _modeStatus.ForeColor = on ? Theme.Accent : ready ? Theme.Good : Theme.Muted;
        _modeStatus.Text = on
            ? "Test mode is ON. Only start CS2 from this launcher until you press Stop - starting it from Steam now could get you VAC-kicked."
            : ready ? "Ready. Press START." : "Install the missing parts first (Setup).";

        _skinsPage.SetData(g, _db);
    }

    // ---------------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------------

    private async Task InstallEverything()
    {
        if (_game == null || !EnsureCs2Closed()) return;

        SetBusy(true);
        var installer = new Installer(_game, _http, Log);
        await Step("Metamod", installer.InstallMetamodAsync, $"download it from {Installer.MetamodPage} and use \"Install from zip\"");
        await Step("CounterStrikeSharp", installer.InstallCounterStrikeSharpAsync,
            $"download the \"with-runtime\" Windows zip from {Installer.CounterStrikeSharpPage} and use \"Install from zip\"");
        await Step("Plugin", () => { installer.InstallPlugin(); return Task.CompletedTask; }, "");
        await Step("CounterStrikeSharp settings", () => { AllowSkinsInCss(); return Task.CompletedTask; }, "");
        await Step("Skin / sticker lists", DownloadItemData, "skins can still be imported with gen codes");
        Log("Done.");
        SetBusy(false);
        RefreshStatus();
    }

    private async Task DownloadItemData()
    {
        await ItemDatabase.DownloadAsync(_http, ItemDatabase.DataDir, Log);
        _db = ItemDatabase.LoadCached(ItemDatabase.DataDir);
        if (_game != null && Directory.Exists(_game.PluginDir)) _db.WritePluginSkinNames(_game.SkinNamesDir);
    }

    private async Task InstallFromZip()
    {
        if (_game == null || !EnsureCs2Closed()) return;

        using var dialog = new OpenFileDialog { Filter = "Zip files (*.zip)|*.zip", Title = "Pick a Metamod or CounterStrikeSharp zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetBusy(true);
        await Step(Path.GetFileName(dialog.FileName), () => new Installer(_game, _http, Log).InstallZipAsync(dialog.FileName), "");
        AllowSkinsInCss();
        SetBusy(false);
        RefreshStatus();
    }

    private async Task Step(string name, Func<Task> action, string fallback)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log($"FAILED: {name}: {ex.Message}");
            if (fallback != "") Log($"   -> {fallback}");
        }
    }

    private void StartTest()
    {
        if (_game == null) return;
        var server = ReadServerSettings();

        try
        {
            AllowSkinsInCss();
            new TestSession(_game).Start(server);
            _runningMode = server.Mode;
            _runningMap = server.Map;
            Log($"Started {server.Mode} on {server.Map} with {server.Bots} {ServerSettings.Difficulties[server.Difficulty].ToLowerInvariant()} bots. CS2 joins when the server has loaded.");
            Log("If it doesn't: open the console (~) and type  connect localhost");
        }
        catch (Exception ex)
        {
            Log("Could not start: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Could not start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshStatus();
    }

    private void ApplyToServer()
    {
        if (_game == null) return;
        var server = ReadServerSettings();
        var reload = _runningMode != null && (_runningMode != server.Mode || _runningMap != server.Map);

        try
        {
            server.ApplyToRunningServer(_game, reload);
            _runningMode = server.Mode;
            _runningMap = server.Map;
            Log(reload ? $"Switching to {server.Mode} on {server.Map}..." : "Applied bots and options to the running server.");
        }
        catch (IOException ex)
        {
            Log("Could not apply: " + ex.Message);
        }
    }

    private void AllowSkinsInCss()
    {
        if (_game?.CounterStrikeSharpInstalled == true && CssCoreConfig.AllowSkins(_game))
            Log("Set FollowCS2ServerGuidelines to false in CounterStrikeSharp's core.json (otherwise only the knife model changes, not the skin).");
    }

    private void StopTest()
    {
        if (_game == null) return;
        if (TestSession.Cs2Running() &&
            MessageBox.Show(this, "This closes CS2 and the test server. Continue?", "Stop skin test",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        try
        {
            new TestSession(_game).Stop();
            _runningMode = _runningMap = null;
            Log("Stopped. CS2 is back to normal - you can play online again.");
        }
        catch (Exception ex)
        {
            Log("Could not restore gameinfo.gi: " + ex.Message);
            Log("   -> Steam > CS2 > Properties > Installed Files > Verify integrity also restores it.");
        }
        RefreshStatus();
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_game == null || !new TestSession(_game).IsOn) return;

        var answer = MessageBox.Show(this,
            "Skin test mode is still ON.\n\nYes = close CS2 + server and restore CS2 now (recommended)\n" +
            "No = leave it on (only start CS2 from this launcher until you press Stop)",
            "Restore CS2?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        if (answer == DialogResult.Cancel) e.Cancel = true;
        else if (answer == DialogResult.Yes) StopTest();
    }

    private bool EnsureCs2Closed()
    {
        if (!TestSession.Cs2Running()) return true;
        MessageBox.Show(this, "Close CS2 (and the test server) first.", "CS2 is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void SetBusy(bool busy)
    {
        UseWaitCursor = busy;
        _installButton.Enabled = _zipButton.Enabled = _startButton.Enabled = _stopButton.Enabled = _applyButton.Enabled = !busy;
    }

    private void Log(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(line));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }

    // ---------------------------------------------------------------------------------------------
    // Layout helpers
    // ---------------------------------------------------------------------------------------------

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 3, 0, 3) };

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Theme.Surface };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 2, 0, 4) };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Control Field(string label, Control input)
    {
        var caption = new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Small, Margin = new Padding(0, 4, 0, 2) };
        var panel = Stack(caption, input);
        panel.Margin = new Padding(0, 0, 18, 6);
        return panel;
    }

    private static Panel Card(string title, Control content, int width)
    {
        var panel = Theme.Section(title, content);
        content.Dock = DockStyle.None;
        content.Location = new Point(16, 40);
        panel.Controls.Remove(content);
        panel.Controls.Add(content);
        panel.Width = width;
        panel.Height = content.PreferredSize.Height + 58;
        content.SizeChanged += (_, _) => panel.Height = content.Height + 58;
        return panel;
    }

    private static LinkLabel Link(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Margin = new Padding(0, 6, 14, 0) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return link;
    }
}
