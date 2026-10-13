using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;
using Microsoft.Win32;

namespace LanSkinTester.Launcher;

/// <summary>Install, update, repair and uninstall: a checklist of everything the skin test needs.</summary>
public class SetupPage : UserControl
{
    private readonly AppState _app;
    private bool _busy;
    private string? _latestMetamod;
    private string? _latestCss;

    private SettingRow _folderRow = null!;
    private readonly StepRow _metamod = new("Metamod:Source 2.x", "Install");
    private readonly StepRow _css = new("CounterStrikeSharp", "Install");
    private readonly StepRow _plugin = new("LAN Skin Tester plugin", "Install");
    private readonly StepRow _cssConfig = new("Skins allowed in CounterStrikeSharp", "Fix");
    private readonly StepRow _data = new("Skin, sticker and charm lists", "Download");
    private readonly PillButton _installAll = new("Install Everything", PillButton.Style.Primary) { AutoWidth = false };
    private readonly PillButton _checkUpdates = new("Check");
    private readonly PillButton _zip = new("Choose Zip…");
    private readonly PillButton _repair = new("Repair");
    private readonly PillButton _uninstall = new("Uninstall", PillButton.Style.Destructive);
    private readonly TextField _log = new(416, multiline: true, height: 560) { ReadOnly = true };
    private readonly System.Windows.Forms.Timer _spinner = new() { Interval = 40 };

    public SetupPage(AppState app)
    {
        _app = app;
        BackColor = Theme.Background;
        Padding = Padding.Empty;
        _log.Font = Theme.Mono;

        const int left = 560;
        const int right = 440;

        var browse = new PillButton("Browse…");
        var find = new PillButton("Find");
        browse.Click += (_, _) => Browse();
        find.Click += (_, _) => _app.SetGame(GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
        var folder = new Group(left);
        _folderRow = folder.AddRow("Counter-Strike 2", new HStack(browse, find), "Not found yet");
        browse.Margin = new Padding(0, 0, 8, 0);
        find.Margin = Padding.Empty;

        _metamod.Action.Click += async (_, _) => await Run(i => Step(_metamod, () => WithProgress(i, _metamod).InstallMetamodAsync()));
        _css.Action.Click += async (_, _) => await Run(i => Step(_css, async () => { await WithProgress(i, _css).InstallCounterStrikeSharpAsync(); _app.AllowSkinsInCss(); }));
        _plugin.Action.Click += async (_, _) => await Run(i => Step(_plugin, () => { i.InstallPlugin(); return Task.CompletedTask; }));
        _cssConfig.Action.Click += (_, _) => { _app.AllowSkinsInCss(); _app.NotifyChanged(); };
        _data.Action.Click += async (_, _) => await Run(_ => Step(_data, DownloadData));
        var steps = new Group(left);
        foreach (var row in new[] { _metamod, _css, _plugin, _cssConfig, _data }) steps.AddRaw(row);

        _installAll.Width = left - 28;
        _installAll.Height = 40;
        _installAll.Click += async (_, _) => await InstallEverything();
        _checkUpdates.Click += async (_, _) => await CheckUpdates();
        _zip.Click += async (_, _) => await InstallZip();
        _repair.Click += async (_, _) => await Repair();
        _uninstall.Click += (_, _) => Uninstall();
        var actions = new Group(left);
        actions.AddContent(_installAll, new Padding(14, 14, 14, 14));
        actions.AddRow("Check for Updates", _checkUpdates, "Compare with the newest Metamod and CounterStrikeSharp");
        actions.AddRow("Install from Zip", _zip, "If a download fails: get the zip from the links below");
        actions.AddRow("Repair", _repair, "Reinstall the plugin and fix leftovers after a crash");
        actions.AddRow("Uninstall Everything", _uninstall, "Remove all mods from your CS2 folder (loadout is backed up)");

        var folders = new Group(left);
        folders.AddRow("CS2 Folder", FolderButton(() => _app.Game?.Root));
        folders.AddRow("Plugin Folder", FolderButton(() => _app.Game?.PluginDir));
        folders.AddRow("Server Configs", FolderButton(() => _app.Game == null ? null : Path.Combine(_app.Game.Csgo, "cfg")));
        folders.AddRow("Launcher Data", FolderButton(() => AppState.LauncherDataDir));
        folders.AddRow("Metamod Downloads", LinkButton(Installer.MetamodPage));
        folders.AddRow("CounterStrikeSharp Downloads", LinkButton(Installer.CounterStrikeSharpPage));

        var leftColumn = new VStack(
            Theme.Caption("GAME"), folder,
            Theme.Caption("COMPONENTS"), steps,
            Theme.Caption("ACTIONS"), actions,
            Theme.Caption("FOLDERS AND LINKS"), folders) { Margin = new Padding(0, 0, 28, 0) };

        var log = new Group(right);
        log.AddContent(_log, new Padding(12, 12, 12, 12));
        var rightColumn = new VStack(Theme.Caption("LOG"), log);

        var page = new VStack(PlayPage.Header("Setup", "Install, update or remove everything the skin test needs."), new HStack(leftColumn, rightColumn));
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        _app.Changed += () => { if (!_busy) Refresh(); };
        _app.Logged += line => Ui(() => _log.AppendText(line + Environment.NewLine));
        _spinner.Tick += (_, _) => { foreach (var r in new[] { _metamod, _css, _plugin, _cssConfig, _data }) r.Invalidate(); };
    }

    // ---------------------------------------------------------------------------------------------
    // Status
    // ---------------------------------------------------------------------------------------------

    public new void Refresh()
    {
        var g = _app.Game;
        _folderRow.Subtitle = g == null ? "Not found - press Browse or Find" : ShortPath(g.Root);
        _folderRow.SubtitleColor = g == null ? Theme.Warning : Theme.Muted;

        if (g == null)
        {
            foreach (var r in new[] { _metamod, _css, _plugin, _cssConfig }) r.Set(StepState.Missing, "Pick your CS2 folder first");
        }
        else
        {
            var mm = Installer.InstalledMetamodVersion(g);
            _metamod.Set(g.MetamodInstalled ? Newer(mm, _latestMetamod) : StepState.Missing,
                g.MetamodInstalled ? Versions(mm, _latestMetamod) : "Not installed - loads plugins into the server");
            _metamod.Action.Text = g.MetamodInstalled ? "Update" : "Install";

            var css = Installer.InstalledCssVersion(g);
            _css.Set(g.CounterStrikeSharpInstalled ? Newer(css, _latestCss) : StepState.Missing,
                g.CounterStrikeSharpInstalled ? Versions(css, _latestCss) : "Not installed - runs C# plugins");
            _css.Action.Text = g.CounterStrikeSharpInstalled ? "Update" : "Install";

            var installed = Installer.InstalledPluginVersion(g);
            var bundled = Installer.BundledPluginVersion();
            var pluginOld = installed != null && bundled != null && installed < bundled;
            _plugin.Set(installed == null ? StepState.Missing : pluginOld ? StepState.Warning : StepState.Ok,
                installed == null ? $"Not installed (this launcher has v{V(bundled)})"
                : pluginOld ? $"v{V(installed)} installed - v{V(bundled)} available, press Update" : $"v{V(installed)} - up to date");
            _plugin.Action.Text = installed == null ? "Install" : "Update";

            var blocks = g.CounterStrikeSharpInstalled && CssCoreConfig.BlocksSkins(g);
            _cssConfig.Set(!g.CounterStrikeSharpInstalled ? StepState.Missing : blocks ? StepState.Warning : StepState.Ok,
                !g.CounterStrikeSharpInstalled ? "Needs CounterStrikeSharp"
                : blocks ? "FollowCS2ServerGuidelines is on - only the knife model would change. Press Fix." : "FollowCS2ServerGuidelines is off");
            _cssConfig.Action.Enabled = blocks;
        }

        _data.Set(_app.Db.IsEmpty ? StepState.Missing : StepState.Ok,
            _app.Db.IsEmpty ? "Not downloaded - needed for skin pictures, names, stickers and charms"
            : $"{_app.Db.Skins.Count:N0} skins · {_app.Db.Stickers.Count:N0} stickers · {_app.Db.Charms.Count} charms");
        _data.Action.Text = _app.Db.IsEmpty ? "Download" : "Update";

        var hasGame = g != null;
        foreach (var b in new[] { _metamod.Action, _css.Action, _plugin.Action, _installAll, _zip, _repair, _uninstall })
            b.Enabled = hasGame && !_busy;
        _data.Action.Enabled = _checkUpdates.Enabled = !_busy;
    }

    private static string V(Version? v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";

    private static string Versions(string? installed, string? latest)
    {
        var have = installed == null ? "Installed" : $"Installed: {installed}";
        return latest == null ? have : $"{have}   ·   Latest: {latest}";
    }

    // Version strings from different sources don't line up exactly, so only flag an update when the
    // installed one clearly doesn't contain the latest build number.
    private static StepState Newer(string? installed, string? latest)
    {
        if (installed == null || latest == null) return StepState.Ok;
        var build = System.Text.RegularExpressions.Regex.Match(latest, @"(\d+)$").Value;
        return build != "" && !installed.Contains(build) ? StepState.Warning : StepState.Ok;
    }

    // ---------------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------------

    private async Task Run(Func<Installer, Task> work)
    {
        if (_busy) return;
        if (_app.Game != null && TestSession.Cs2Running())
        {
            MessageBox.Show(this, "Close CS2 and the test server first (files are in use while they run).", "CS2 is running",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _busy = true;
        UseWaitCursor = true;
        _spinner.Start();
        Refresh();
        try
        {
            await work(new Installer(_app.Game ?? new GameFolder(""), _app.Http, _app.Log));
        }
        finally
        {
            _spinner.Stop();
            _busy = false;
            UseWaitCursor = false;
            _app.NotifyChanged();
            Refresh();
        }
    }

    /// <summary>Runs one install step with its row showing progress; logs and marks failures.</summary>
    private async Task<bool> Step(StepRow row, Func<Task> action)
    {
        row.SetProgress(-1);
        try
        {
            await action();
            row.Set(StepState.Ok, "Done");
            return true;
        }
        catch (Exception ex)
        {
            _app.Log($"FAILED: {ex.Message}");
            row.Set(StepState.Failed, "Failed: " + ex.Message);
            return false;
        }
    }

    private Installer WithProgress(Installer installer, StepRow row)
    {
        installer.Progress = p => Ui(() => row.SetProgress(p));
        return installer;
    }

    private async Task InstallEverything() => await Run(async i =>
    {
        if (!await Step(_metamod, () => WithProgress(i, _metamod).InstallMetamodAsync()))
            _app.Log($"   -> download Metamod from {Installer.MetamodPage} and use \"Install from zip...\"");
        if (!await Step(_css, () => WithProgress(i, _css).InstallCounterStrikeSharpAsync()))
            _app.Log($"   -> download the \"with-runtime\" Windows zip from {Installer.CounterStrikeSharpPage} and use \"Install from zip...\"");
        await Step(_plugin, () => { i.InstallPlugin(); return Task.CompletedTask; });
        await Step(_cssConfig, () => { _app.AllowSkinsInCss(); return Task.CompletedTask; });
        await Step(_data, DownloadData);
        _app.Log("All done. Go to PLAY and press START.");
    });

    private async Task DownloadData()
    {
        await ItemDatabase.DownloadAsync(_app.Http, ItemDatabase.DataDir, _app.Log);
        _app.ReloadDb();
    }

    private async Task CheckUpdates()
    {
        _checkUpdates.Enabled = false;
        _app.Log("Checking for updates...");
        var installer = new Installer(_app.Game ?? new GameFolder(""), _app.Http, _app.Log);
        (_latestMetamod, _latestCss) = await installer.LatestVersionsAsync();
        _app.Log($"Latest Metamod: {_latestMetamod ?? "unknown"} · latest CounterStrikeSharp: {_latestCss ?? "unknown"}");
        _checkUpdates.Enabled = true;
        Refresh();
    }

    private async Task InstallZip()
    {
        using var dialog = new OpenFileDialog { Filter = "Zip files (*.zip)|*.zip", Title = "Pick a Metamod or CounterStrikeSharp zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        await Run(async i =>
        {
            try
            {
                await i.InstallZipAsync(dialog.FileName);
                _app.AllowSkinsInCss();
            }
            catch (Exception ex)
            {
                _app.Log("FAILED: " + ex.Message);
            }
        });
    }

    private async Task Repair() => await Run(async i =>
    {
        _app.Log("Repairing...");
        if (!_app.TestModeOn) GameInfoPatcher.Disable(_app.Game!.GameInfo); // leftover line from a crash
        await Step(_plugin, () => { i.InstallPlugin(); return Task.CompletedTask; });
        _app.AllowSkinsInCss();
        if (!_app.Db.IsEmpty) _app.Db.WritePluginSkinNames(_app.Game!.SkinNamesDir);
        foreach (var leftover in new[] { "server_commands.txt", "server_commands.txt.tmp", "loadouts.json.tmp" })
            File.Delete(Path.Combine(_app.Game!.PluginDir, leftover));
        _app.Log("Repair finished.");
    });

    private void Uninstall()
    {
        if (_app.Game == null) return;
        if (MessageBox.Show(this,
                "This removes Metamod, CounterStrikeSharp and the LAN Skin Tester plugin from your CS2 folder and puts " +
                "gameinfo.gi back to normal. Your saved loadout is backed up to the launcher data folder.\n\nContinue?",
                "Uninstall everything", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        _ = Run(i =>
        {
            try
            {
                i.UninstallAll(Path.Combine(AppState.LauncherDataDir, "backups"));
            }
            catch (Exception ex)
            {
                _app.Log("FAILED: " + ex.Message);
            }
            return Task.CompletedTask;
        });
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Pick the \"Counter-Strike Global Offensive\" folder (Steam > CS2 > Manage > Browse local files)" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!GameFolder.IsValid(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder doesn't contain game\\csgo\\gameinfo.gi. Pick the \"Counter-Strike Global Offensive\" folder.",
                "Not a CS2 folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _app.SetGame(dialog.SelectedPath);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private void Ui(Action action)
    {
        if (IsHandleCreated && InvokeRequired) BeginInvoke(action);
        else action();
    }

    private static string ShortPath(string path) => path.Length <= 56 ? path : "…" + path[^55..];

    private PillButton FolderButton(Func<string?> path)
    {
        var b = new PillButton("Open", PillButton.Style.Plain);
        b.Click += (_, _) =>
        {
            var p = path();
            if (p == null) return;
            Directory.CreateDirectory(p);
            Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });
        };
        return b;
    }

    private static PillButton LinkButton(string url)
    {
        var b = new PillButton("Open in Browser", PillButton.Style.Plain);
        b.Click += (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return b;
    }
}
