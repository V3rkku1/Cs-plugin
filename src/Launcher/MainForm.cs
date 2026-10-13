using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using Microsoft.Win32;

namespace LanSkinTester.Launcher;

public class MainForm : Form
{
    private static readonly string[] Maps =
        ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_vertigo", "de_overpass", "de_train"];

    private readonly LauncherSettings _settings = LauncherSettings.Load();
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private GameFolder? _game;

    private readonly TextBox _pathBox = new() { ReadOnly = true, Width = 560 };
    private readonly Label _metamodStatus = new() { AutoSize = true };
    private readonly Label _cssStatus = new() { AutoSize = true };
    private readonly Label _pluginStatus = new() { AutoSize = true };
    private readonly Label _namesStatus = new() { AutoSize = true };
    private readonly Label _modeStatus = new() { AutoSize = true, Font = new Font("Segoe UI", 10.5f, FontStyle.Bold), MaximumSize = new Size(820, 0) };
    private readonly ComboBox _mapBox = new() { Width = 160 };
    private readonly Button _installButton = new() { Text = "Install / update everything", AutoSize = true };
    private readonly Button _zipButton = new() { Text = "Install from zip file...", AutoSize = true };
    private readonly Button _startButton = new() { Text = "Start skin test", AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) };
    private readonly Button _stopButton = new() { Text = "Stop and restore CS2", AutoSize = true, Font = new Font("Segoe UI", 11f) };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly SkinsTab _skinsTab = new() { Dock = DockStyle.Fill };

    public MainForm()
    {
        Text = "LAN Skin Tester";
        Font = new Font("Segoe UI", 9.5f);
        Size = new Size(940, 700);
        MinimumSize = new Size(860, 640);
        StartPosition = FormStartPosition.CenterScreen;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var setup = new TabPage("Setup & Play");
        var skins = new TabPage("Skins");
        setup.Controls.Add(BuildSetupPage());
        skins.Controls.Add(_skinsTab);
        tabs.TabPages.Add(setup);
        tabs.TabPages.Add(skins);
        tabs.SelectedIndexChanged += (_, _) => { if (tabs.SelectedTab == skins) _skinsTab.Reload(); };
        Controls.Add(tabs);

        _mapBox.Items.AddRange(Maps);
        _mapBox.Text = _settings.Map;

        Load += (_, _) => SetGameFolder(GameFolder.IsValid(_settings.GamePath)
            ? _settings.GamePath
            : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
        FormClosing += OnClosing;
    }

    private Control BuildSetupPage()
    {
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) => BrowseForGame();
        _installButton.Click += async (_, _) => await InstallEverything();
        _zipButton.Click += async (_, _) => await InstallFromZip();
        _startButton.Click += (_, _) => StartTest();
        _stopButton.Click += (_, _) => StopTest();

        var openMetamod = Link("Metamod downloads", Installer.MetamodPage);
        var openCss = Link("CounterStrikeSharp downloads", Installer.CounterStrikeSharpPage);

        var install = new GroupBox { Text = "1. Install", Dock = DockStyle.Top, Height = 160, Padding = new Padding(10) };
        install.Controls.Add(Fill(Flow(FlowDirection.TopDown,
            _metamodStatus, _cssStatus, _pluginStatus, _namesStatus,
            Flow(FlowDirection.LeftToRight, _installButton, _zipButton, openMetamod, openCss))));

        var play = new GroupBox { Text = "2. Play", Dock = DockStyle.Top, Height = 150, Padding = new Padding(10) };
        play.Controls.Add(Fill(Flow(FlowDirection.TopDown,
            Flow(FlowDirection.LeftToRight, new Label { Text = "Map:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _mapBox, _startButton, _stopButton),
            _modeStatus,
            new Label
            {
                AutoSize = true,
                MaximumSize = new Size(820, 0),
                Text = "Start opens a local server window and CS2 (VAC off) that joins it. Pick skins in the Skins tab - " +
                       "they change in game right away. When you are done, press Stop: it closes both and puts CS2 back to normal.",
            })));

        var top = Flow(FlowDirection.LeftToRight, new Label { Text = "CS2 folder:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _pathBox, browse);
        top.Dock = DockStyle.Top;

        var logBox = new GroupBox { Text = "Log", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logBox.Controls.Add(_log);

        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        // Docked controls stack in reverse order of adding.
        page.Controls.Add(logBox);
        page.Controls.Add(play);
        page.Controls.Add(install);
        page.Controls.Add(top);
        return page;
    }

    private void SetGameFolder(string? path)
    {
        if (!GameFolder.IsValid(path))
        {
            _game = null;
            _pathBox.Text = "CS2 not found - press Browse and pick the \"Counter-Strike Global Offensive\" folder";
            RefreshStatus();
            return;
        }

        _game = new GameFolder(path!);
        _pathBox.Text = _game.Root;
        _settings.GamePath = _game.Root;
        _settings.Save();
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
            label.Text = ok ? $"{name}: installed" : $"{name}: {missing}";
            label.ForeColor = ok ? Color.ForestGreen : Color.Firebrick;
        }

        var g = _game;
        Mark(_metamodStatus, g?.MetamodInstalled == true, "Metamod:Source", "not installed");
        Mark(_cssStatus, g?.CounterStrikeSharpInstalled == true, "CounterStrikeSharp", "not installed");
        Mark(_pluginStatus, g?.PluginInstalled == true, "LAN Skin Tester plugin", "not installed");
        Mark(_namesStatus, g?.SkinNamesInstalled == true, "Skin names (search + pictures)", "not installed (optional)");

        var ready = g is { MetamodInstalled: true, CounterStrikeSharpInstalled: true, PluginInstalled: true };
        var on = g != null && new TestSession(g).IsOn;

        _installButton.Enabled = _zipButton.Enabled = g != null;
        _startButton.Enabled = ready && !on;
        _stopButton.Enabled = on;

        if (on)
        {
            _modeStatus.ForeColor = Color.DarkOrange;
            _modeStatus.Text = "Skin test mode is ON. Only start CS2 from this launcher until you press Stop - " +
                               "starting it from Steam now could get you VAC-kicked.";
        }
        else
        {
            _modeStatus.ForeColor = Color.ForestGreen;
            _modeStatus.Text = ready ? "CS2 is normal. Ready to start a skin test." : "CS2 is normal. Install the missing parts first.";
        }

        _skinsTab.SetGame(g);
    }

    private async Task InstallEverything()
    {
        if (_game == null || !EnsureCs2Closed()) return;

        SetBusy(true);
        var installer = new Installer(_game, _http, Log);
        await Step("Metamod", installer.InstallMetamodAsync, $"download it from {Installer.MetamodPage} and use \"Install from zip file\"");
        await Step("CounterStrikeSharp", installer.InstallCounterStrikeSharpAsync,
            $"download the \"with-runtime\" Windows zip from {Installer.CounterStrikeSharpPage} and use \"Install from zip file\"");
        await Step("Plugin", () => { installer.InstallPlugin(); return Task.CompletedTask; }, "");
        await Step("CounterStrikeSharp settings", () => { AllowSkinsInCss(); return Task.CompletedTask; }, "");
        await Step("Skin names", installer.InstallSkinNamesAsync, "skins can still be picked by ID");
        Log("Done.");
        SetBusy(false);
        RefreshStatus();
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
        _settings.Map = string.IsNullOrWhiteSpace(_mapBox.Text) ? "de_dust2" : _mapBox.Text.Trim();
        _settings.Save();

        try
        {
            AllowSkinsInCss();
            new TestSession(_game).Start(_settings.Map);
            Log($"Started server on {_settings.Map} and CS2 (-insecure). CS2 joins the server when it has loaded.");
            Log("If it doesn't: open the console (~) and type  connect localhost");
        }
        catch (Exception ex)
        {
            Log("Could not start: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Could not start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshStatus();
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
        _installButton.Enabled = _zipButton.Enabled = _startButton.Enabled = _stopButton.Enabled = !busy;
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

    private static FlowLayoutPanel Flow(FlowDirection direction, params Control[] controls)
    {
        var panel = new FlowLayoutPanel { FlowDirection = direction, AutoSize = true, WrapContents = false };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static Control Fill(Control control)
    {
        control.Dock = DockStyle.Fill;
        return control;
    }

    private static LinkLabel Link(string text, string url)
    {
        var link = new LinkLabel { Text = text, AutoSize = true, Padding = new Padding(4, 7, 0, 0) };
        link.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return link;
    }
}
