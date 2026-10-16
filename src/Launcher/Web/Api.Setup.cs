using LanSkinTester.Launcher.Core;
using Microsoft.Win32;

namespace LanSkinTester.Launcher.Web;

/// <summary>Setup page: install, update, repair and uninstall everything the skin test needs.</summary>
public partial class Api
{
    private bool _setupBusy;
    private string? _latestMetamod, _latestCss;

    /// <summary>Steps whose last run failed or finished, until the next refresh of real state.</summary>
    private readonly Dictionary<string, (string State, string Detail)> _stepResults = new();

    public record SetupStep(string Id, string Title, string State, string Detail, string Action, bool Enabled);

    public record SetupState(string? Folder, bool Busy, List<SetupStep> Steps, bool HasGame, string TextSize,
        bool ReduceGlass, bool ReduceMotion, string MetamodPage, string CssPage);

    [Call("setup.state")]
    private SetupState SetupStateNow()
    {
        var g = App.Game;
        var steps = new List<SetupStep>();
        void Add(string id, string title, string state, string detail, string action, bool enabled)
        {
            if (_stepResults.TryGetValue(id, out var r)) (state, detail) = r;
            steps.Add(new SetupStep(id, title, state, detail, action, enabled && !_setupBusy));
        }

        if (g == null)
        {
            Add("metamod", "Metamod:Source 2.x", "missing", "Pick your CS2 folder first", "Install", false);
            Add("css", "CounterStrikeSharp", "missing", "Pick your CS2 folder first", "Install", false);
            Add("plugin", "LAN Skin Tester plugin", "missing", "Pick your CS2 folder first", "Install", false);
            Add("cssConfig", "Skins allowed in CounterStrikeSharp", "missing", "Pick your CS2 folder first", "Fix", false);
        }
        else
        {
            var mm = Installer.InstalledMetamodVersion(g);
            Add("metamod", "Metamod:Source 2.x", g.MetamodInstalled ? Newer(mm, _latestMetamod) : "missing",
                g.MetamodInstalled ? Versions(mm, _latestMetamod) : "Not installed - loads plugins into the server",
                g.MetamodInstalled ? "Update" : "Install", true);

            var css = Installer.InstalledCssVersion(g);
            Add("css", "CounterStrikeSharp", g.CounterStrikeSharpInstalled ? Newer(css, _latestCss) : "missing",
                g.CounterStrikeSharpInstalled ? Versions(css, _latestCss) : "Not installed - runs C# plugins",
                g.CounterStrikeSharpInstalled ? "Update" : "Install", true);

            var installed = Installer.InstalledPluginVersion(g);
            var bundled = Installer.BundledPluginVersion();
            var pluginOld = installed != null && bundled != null && installed < bundled;
            Add("plugin", "LAN Skin Tester plugin", installed == null ? "missing" : pluginOld ? "warning" : "ok",
                installed == null ? $"Not installed (this launcher has v{V(bundled)})"
                : pluginOld ? $"v{V(installed)} installed - v{V(bundled)} available, press Update" : $"v{V(installed)} - up to date",
                installed == null ? "Install" : "Update", true);

            var blocks = g.CounterStrikeSharpInstalled && CssCoreConfig.BlocksSkins(g);
            Add("cssConfig", "Skins allowed in CounterStrikeSharp", !g.CounterStrikeSharpInstalled ? "missing" : blocks ? "warning" : "ok",
                !g.CounterStrikeSharpInstalled ? "Needs CounterStrikeSharp"
                : blocks ? "FollowCS2ServerGuidelines is on - only the knife model would change. Press Fix." : "FollowCS2ServerGuidelines is off",
                "Fix", blocks);
        }

        Add("data", "Skin, sticker and charm lists", App.Db.IsEmpty ? "missing" : "ok",
            App.Db.IsEmpty ? "Not downloaded - needed for skin pictures, names, stickers and charms"
            : $"{App.Db.Skins.Count:N0} skins · {App.Db.Stickers.Count:N0} stickers · {App.Db.Charms.Count} charms",
            App.Db.IsEmpty ? "Download" : "Update", true);

        var s = App.Settings;
        return new SetupState(g?.Root, _setupBusy, steps, g != null, s.TextSize, s.ReduceGlass, s.ReduceMotion,
            Installer.MetamodPage, Installer.CounterStrikeSharpPage);
    }

    private static string V(Version? v) => v == null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";

    private static string Versions(string? installed, string? latest)
    {
        var have = installed == null ? "Installed" : $"Installed: {installed}";
        return latest == null ? have : $"{have}   ·   Latest: {latest}";
    }

    // Version strings from different sources don't line up exactly, so only flag an update when the
    // installed one clearly doesn't contain the latest build number.
    private static string Newer(string? installed, string? latest)
    {
        if (installed == null || latest == null) return "ok";
        var build = System.Text.RegularExpressions.Regex.Match(latest, @"(\d+)$").Value;
        return build != "" && !installed.Contains(build) ? "warning" : "ok";
    }

    // ---------------------------------------------------------------------------------------------
    // Actions
    // ---------------------------------------------------------------------------------------------

    [Call("setup.find")]
    private void FindGame() =>
        App.SetGame(GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));

    [Call("setup.browse")]
    private async Task Browse()
    {
        var path = await OnUi(() =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Pick the \"Counter-Strike Global Offensive\" folder (Steam > CS2 > Manage > Browse local files)" };
            return dialog.ShowDialog(Window) == DialogResult.OK ? dialog.SelectedPath : null;
        });
        if (path == null) return;
        if (!GameFolder.IsValid(path))
            throw new ApiException("That folder doesn't contain game\\csgo\\gameinfo.gi. Pick the \"Counter-Strike Global Offensive\" folder.");
        App.SetGame(path);
    }

    /// <summary>Runs one step by id: metamod, css, plugin, cssConfig or data.</summary>
    [Call("setup.step")]
    private Task RunOneStep(string id) => id switch
    {
        "cssConfig" => Task.Run(() => { App.AllowSkinsInCss(); App.NotifyChanged(); }),
        "data" => RunSetup(_ => Step("data", DownloadData), needsGame: false),
        _ => RunSetup(i => Step(id, () => StepWork(i, id))),
    };

    private Task StepWork(Installer i, string id)
    {
        i.Progress = p => Push("setup.progress", new { id, p });
        return id switch
        {
            "metamod" => i.InstallMetamodAsync(),
            "css" => InstallCss(i),
            "plugin" => Task.Run(i.InstallPlugin),
            _ => throw new ApiException("Unknown step"),
        };

        async Task InstallCss(Installer installer)
        {
            await installer.InstallCounterStrikeSharpAsync();
            App.AllowSkinsInCss();
        }
    }

    [Call("setup.all")]
    private Task InstallEverything() => RunSetup(async i =>
    {
        if (!await Step("metamod", () => StepWork(i, "metamod")))
            App.Log($"   -> download Metamod from {Installer.MetamodPage} and use \"Install from zip...\"");
        if (!await Step("css", () => StepWork(i, "css")))
            App.Log($"   -> download the \"with-runtime\" Windows zip from {Installer.CounterStrikeSharpPage} and use \"Install from zip...\"");
        await Step("plugin", () => StepWork(i, "plugin"));
        await Step("cssConfig", () => Task.Run(App.AllowSkinsInCss));
        await Step("data", DownloadData);
        App.Log("All done. Go to PLAY and press START.");
    });

    [Call("setup.checkUpdates")]
    private async Task CheckSetupUpdates()
    {
        App.Log("Checking for updates...");
        var installer = new Installer(App.Game ?? new GameFolder(""), App.Http, App.Log);
        (_latestMetamod, _latestCss) = await installer.LatestVersionsAsync();
        App.Log($"Latest Metamod: {_latestMetamod ?? "unknown"} · latest CounterStrikeSharp: {_latestCss ?? "unknown"}");
        App.NotifyChanged();
    }

    [Call("setup.zip")]
    private async Task InstallZip()
    {
        var file = await OnUi(() =>
        {
            using var dialog = new OpenFileDialog { Filter = "Zip files (*.zip)|*.zip", Title = "Pick a Metamod or CounterStrikeSharp zip" };
            return dialog.ShowDialog(Window) == DialogResult.OK ? dialog.FileName : null;
        });
        if (file == null) return;
        await RunSetup(async i =>
        {
            try
            {
                await i.InstallZipAsync(file);
                App.AllowSkinsInCss();
            }
            catch (Exception ex)
            {
                App.Log("FAILED: " + ex.Message);
            }
        });
    }

    [Call("setup.repair")]
    private Task Repair() => RunSetup(async i =>
    {
        App.Log("Repairing...");
        if (!App.TestModeOn) GameInfoPatcher.Disable(App.Game!.GameInfo); // leftover line from a crash
        await Step("plugin", () => StepWork(i, "plugin"));
        App.AllowSkinsInCss();
        if (!App.Db.IsEmpty) App.Db.WritePluginSkinNames(App.Game!.SkinNamesDir);
        foreach (var leftover in new[] { "server_commands.txt", "server_commands.txt.tmp", "loadouts.json.tmp" })
            File.Delete(Path.Combine(App.Game!.PluginDir, leftover));
        App.Log("Repair finished.");
    });

    /// <summary>The page asks "are you sure" first.</summary>
    [Call("setup.uninstall")]
    private Task Uninstall() => RunSetup(i =>
    {
        try
        {
            i.UninstallAll(Path.Combine(AppState.LauncherDataDir, "backups"));
        }
        catch (Exception ex)
        {
            App.Log("FAILED: " + ex.Message);
        }
        return Task.CompletedTask;
    });

    public record AppearanceArgs(string? TextSize, bool? ReduceGlass, bool? ReduceMotion);

    [Call("setup.appearance")]
    private void SetAppearance(AppearanceArgs a)
    {
        var s = App.Settings;
        if (a.TextSize is "normal" or "large") s.TextSize = a.TextSize;
        if (a.ReduceGlass is { } glass) s.ReduceGlass = glass;
        if (a.ReduceMotion is { } motion) s.ReduceMotion = motion;
        try { s.Save(); }
        catch (IOException) { /* saved next time */ }
        App.NotifyChanged();
    }

    private async Task RunSetup(Func<Installer, Task> work, bool needsGame = true)
    {
        if (_setupBusy) return;
        if (needsGame && App.Game == null) throw new ApiException("Pick your CS2 folder first.");
        if (App.Game != null && TestSession.Cs2Running())
            throw new ApiException("Close CS2 and the test server first (files are in use while they run).");

        _setupBusy = true;
        _stepResults.Clear();
        App.NotifyChanged();
        try
        {
            await Task.Run(() => work(new Installer(App.Game ?? new GameFolder(""), App.Http, App.Log)));
        }
        finally
        {
            _setupBusy = false;
            App.NotifyChanged();
        }
    }

    /// <summary>Runs one install step; logs and marks failures on its row.</summary>
    private async Task<bool> Step(string id, Func<Task> action)
    {
        _stepResults.Remove(id);
        Push("setup.progress", new { id, p = -1.0 });
        try
        {
            await action();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"FAILED: {ex.Message}");
            _stepResults[id] = ("failed", "Failed: " + ex.Message);
            return false;
        }
        finally
        {
            Push("setup.progress", new { id, p = (double?)null });
        }
    }

    private async Task DownloadData()
    {
        await ItemDatabase.DownloadAsync(App.Http, ItemDatabase.DataDir, App.Log);
        App.ReloadDb();
    }
}
