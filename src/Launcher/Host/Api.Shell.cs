using System.Diagnostics;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>The window around the pages: status, navigation, links, launcher updates, auto-restore.</summary>
public partial class Api
{
    private bool _updateAvailable;
    private string? _updateProgress;
    private UpdateCheck.Update? _update;
    private bool _checkingUpdate;
    private DateTime? _lastUpdateCheck;
    private int _cs2GoneTicks;

    /// <summary>Called once the server is up: find CS2 and look for a launcher update.</summary>
    public void Started()
    {
        App.SetGame(GameFolder.IsValid(App.Settings.GamePath) ? App.Settings.GamePath : GameFolder.Find(AppState.SteamPath()));
        if (App.Game == null) App.Log("CS2 not found automatically - press Browse on the Setup page.");
        ReportLastUpdate();
        _ = CheckForUpdateAsync(startup: true);
        // Look again every 15 minutes while the launcher stays open, so a new build shows up without a restart.
        var timer = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
        timer.Tick += (_, _) => { if (!_updating) _ = CheckForUpdateAsync(startup: false); };
        timer.Start();
    }

    public record ShellState(string Version, string Page, bool Ready, bool TestMode, string StatusKind, string StatusTitle,
        string StatusDetail, bool UpdateAvailable, string? UpdateProgress, string? UpdateCommit, List<string> UpdateNotes,
        bool AutoUpdate, bool CheckingUpdate, string? LastUpdateCheck);

    [Call("app.state")]
    private ShellState State()
    {
        var g = Legacy;
        var ready = g is { SourceModInstalled: true, PtahInstalled: true } && g.PluginInstalled("weapons") && g.PluginInstalled("gloves");
        var (kind, title, detail) = LegacyGame.Running() ? ("warning", "CS:GO running", "LAN game · VAC off")
            : ready ? ("good", "Ready", "Press Start on Play")
            : ("bad", "Setup needed", "Open Setup to install");
        // First run (or something missing): start on Setup; otherwise where you left off.
        var page = Program.StartPage ?? (!ready || App.Db.IsEmpty ? "setup" : App.Settings.LastPage ?? "play");
        return new ShellState(UpdateCheck.ShortVersion, page, ready, LegacyGame.Running(), kind, title, detail, _updateAvailable, _updateProgress,
            _update?.Commit[..7], _update?.Notes ?? new List<string>(), App.Settings.AutoUpdate, _checkingUpdate,
            _lastUpdateCheck?.ToString("HH:mm"));
    }

    [Call("app.fullscreen")]
    private Task ToggleFullscreen() => OnUi(() => (Window as ShellForm)?.ToggleFullscreen());

    [Call("app.page")]
    private void SavePage(string page)
    {
        _ = OnUi(() => (Window as ShellForm)?.ShowPage(page));
        App.Settings.LastPage = page;
        App.SaveSettings();
    }

    [Call("app.openUrl")]
    private void OpenUrl(string url)
    {
        if (!url.StartsWith("https://") && !url.StartsWith("steam://")) throw new ApiException("Not a web link");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>Opens one of the launcher's known folders in Explorer: game, plugin, cfg or data.</summary>
    [Call("app.openFolder")]
    private void OpenFolder(string which)
    {
        var path = which switch
        {
            "game" => App.Game?.Root,
            "plugin" => App.Game?.PluginDir,
            "cfg" => App.Game == null ? null : Path.Combine(App.Game.Csgo, "cfg"),
            "data" => AppState.LauncherDataDir,
            "csgo" => App.Settings.LegacyPath,
            _ => null,
        } ?? throw new ApiException("Pick your CS2 folder first.");
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>
    /// Looks for a newer build. At start-up (nothing running yet) it installs it right away when automatic
    /// updates are on; while the launcher is open it only shows it, so nothing restarts under you.
    /// </summary>
    private async Task<string?> CheckForUpdateAsync(bool startup)
    {
        if (_checkingUpdate) return null;
        _checkingUpdate = true;
        App.NotifyChanged();
        UpdateCheck.Update? found;
        string? error;
        try
        {
            (found, error) = await UpdateCheck.CheckAsync(App.Http);
            _lastUpdateCheck = DateTime.Now;
        }
        finally
        {
            _checkingUpdate = false;
        }
        if (found == null)
        {
            if (error == null)
            {
                // The newest (again): forget an update that was shown earlier.
                _updateAvailable = false;
                _update = null;
            }
            App.NotifyChanged();
            return error;
        }
        var isNew = _update?.Commit != found.Commit;
        _update = found;
        _updateAvailable = true;
        var recently = App.Settings.LastAutoUpdate is { } last && DateTime.UtcNow - last < TimeSpan.FromMinutes(30);
        if (startup && App.Settings.AutoUpdate && !recently && !App.TestModeOn && Updater.CanSelfUpdate())
        {
            App.Settings.LastAutoUpdate = DateTime.UtcNow;
            App.SaveSettings();
            App.Log($"Newer launcher build {found.Commit[..7]} on GitHub - updating automatically.");
            App.NotifyChanged();
            await InstallUpdateAsync(automatic: true);
            return null;
        }
        if (isNew)
        {
            App.Log($"Newer launcher build {found.Commit[..7]} on GitHub - click \"Update available\" in the sidebar to install it.");
            Toast("Launcher update available.");
        }
        App.NotifyChanged();
        return null;
    }

    /// <summary>Says how the last self-update went (the swap script leaves a note for this start).</summary>
    private void ReportLastUpdate()
    {
        if (Updater.TakeResult() is not { } result) return;
        if (result == "ok")
        {
            App.Log($"Launcher updated to {UpdateCheck.ShortVersion}.");
            Toast($"Updated to {UpdateCheck.ShortVersion}.", "good");
        }
        else
        {
            App.Log("The update could not replace the launcher file (" + result + "). Download it from " + UpdateCheck.ReleaseUrl);
            Toast("Update failed - see the log on the Setup page.", "bad");
        }
    }

    /// <summary>The "Check for updates" button: looks now and says if this is already the newest.</summary>
    [Call("app.checkUpdate")]
    private async Task<bool> CheckUpdateNow()
    {
        if (_updating) return true;
        var error = await CheckForUpdateAsync(startup: false);
        if (error != null)
        {
            App.Log("Could not check for a launcher update: " + error + ".");
            throw new ApiException("Couldn't check for updates: " + error + ".");
        }
        if (_updateAvailable) Toast($"Build {_update?.Commit[..7]} is out - click Update.", "info");
        else Toast($"You have the newest launcher ({UpdateCheck.ShortVersion}).", "good");
        return _updateAvailable;
    }

    [Call("app.autoUpdate")]
    private void SetAutoUpdate(bool on)
    {
        App.Settings.AutoUpdate = on;
        App.SaveSettings();
        App.NotifyChanged();
    }

    private bool _updating;

    /// <summary>Downloads the new launcher, swaps it in after closing, and restarts.</summary>
    [Call("app.update")]
    private Task InstallUpdate() => InstallUpdateAsync(automatic: false);

    private async Task InstallUpdateAsync(bool automatic)
    {
        if (_updating) return;
        _updating = true; // before the test is stopped: a second click mustn't start a second update meanwhile
        try
        {
            await InstallUpdateCoreAsync(automatic);
        }
        finally
        {
            _updating = false;
        }
    }

    private async Task InstallUpdateCoreAsync(bool automatic)
    {
        if (App.TestModeOn)
        {
            if (automatic) return;
            // The new launcher can't take over a running test: close CS2 and the server, restore CS2.
            App.Log("Updating: stopping the test first (closes CS2 and the server).");
            await Task.Run(StopFromClose);
        }
        if (!Updater.CanSelfUpdate())
        {
            App.Log($"The launcher's folder ({Path.GetDirectoryName(Updater.CurrentExe)}) isn't writable, so it can't update itself. " +
                    "Move SkinTesterCSGO.exe to a folder of your own (e.g. Documents) or install it with SkinTesterCSGOSetup.exe.");
            if (!automatic) Toast("Can't update: the launcher's folder isn't writable - see the log on Setup.", "bad");
            return;
        }

        try
        {
            _updateProgress = "Downloading 0%";
            App.NotifyChanged();
            var shown = 0;
            var file = await Updater.DownloadAsync(App.Http, p =>
            {
                // Only whole percents: every chunk would refresh all pages hundreds of times.
                var percent = (int)(p * 100);
                if (percent == shown) return;
                shown = percent;
                _updateProgress = $"Downloading {percent}%";
                App.NotifyChanged();
            }, why =>
            {
                // A new build is being published (its files are replaced for a minute) or the connection dropped.
                App.Log($"Update download failed ({why}) - trying again shortly.");
                _updateProgress = "Waiting for the download…";
                App.NotifyChanged();
            });
            _updateProgress = "Restarting";
            App.NotifyChanged();
            App.Log("Update downloaded - restarting the launcher.");
            BackupUserData();
            Updater.ReplaceAfterExit(file);
            await OnUi(() =>
            {
                (Window as ShellForm)?.CloseForUpdate();
                // Whatever is still running (a dialog, a stuck thread) must not keep the old exe in use.
                var exit = new System.Windows.Forms.Timer { Interval = 4000 };
                exit.Tick += (_, _) => Environment.Exit(0);
                exit.Start();
            });
        }
        catch (Exception ex)
        {
            _updateProgress = null;
            App.NotifyChanged();
            // No browser: the sidebar card stays, so trying again is one click.
            App.Log("Update failed: " + ex.Message + " - click \"Update available\" to try again.");
            Toast("Update failed - click the card in the sidebar to try again (details in Setup › Log).", "bad");
        }
    }

    /// <summary>
    /// Every 5 seconds: when CS2 and the test server are both closed but test mode is still on (game
    /// closed with the X, a crash, or a previous launcher session), put CS2 back to normal.
    /// </summary>
    private int _lineNoServerTicks;

    public void WatchGame()
    {
        // CS:GO: kgns' plugins make their tables on the first start; put the loadout in as soon as they're there.
        if (LegacyGame.Running() && InventoryGate.Wait(0))
        {
            try { SyncKgns(); }
            finally { InventoryGate.Release(); }
        }

        // No server for 10 seconds but the Metamod line is still in gameinfo.gi (a server that never started, a
        // crash): take it out, so CS2 started from Steam doesn't try to load Metamod.
        if (App.Game is { } g && TestSession.FindServer() == null && GameInfoPatcher.IsEnabled(g.GameInfo))
        {
            if (++_lineNoServerTicks >= 2)
            {
                _lineNoServerTicks = 0;
                try { GameInfoPatcher.Disable(g.GameInfo); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log("Could not update gameinfo.gi: " + ex.Message); }
            }
        }
        else _lineNoServerTicks = 0;

        if (!App.TestModeOn || TestSession.Cs2Running())
        {
            _cs2GoneTicks = 0;
            return;
        }
        // Two checks in a row (about 10 seconds), so a restarting game isn't caught in between.
        if (++_cs2GoneTicks < 2) return;
        _cs2GoneTicks = 0;
        try
        {
            AutoRestore();
            Toast("CS2 closed - restored to normal. You can play online again.");
        }
        catch (Exception ex)
        {
            App.Log("Could not restore gameinfo.gi automatically: " + ex.Message);
        }
    }

    /// <summary>Closing with test mode on asks whether to restore CS2 first. False keeps the window open.</summary>
    public bool ConfirmClose()
    {
        if (!App.TestModeOn) return true;
        var answer = MessageBox.Show(Window,
            "Skin test mode is still ON.\n\nYes = close CS2 + server and restore CS2 now (recommended)\n" +
            "No = leave the server and game running (CS2 from Steam works normally meanwhile)",
            "Restore CS2?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes)
        {
            try { StopFromClose(); }
            catch (Exception ex) { MessageBox.Show(Window, "Could not restore gameinfo.gi: " + ex.Message); }
        }
        else
        {
            // The running server read gameinfo.gi long ago; without the launcher nothing would take the line out.
            try { if (App.Game is { } g) GameInfoPatcher.Disable(g.GameInfo); }
            catch (Exception) { /* next launcher start takes it out */ }
        }
        return true;
    }

    /// <summary>
    /// Setup › Reset Launcher (the page asks first): deletes everything the launcher keeps (settings, inventory,
    /// case history, item data, pictures, theme, backups) and the loadout in the CS2 folder, then starts again fresh.
    /// The mods in the CS2 folder stay: Uninstall Everything removes those.
    /// </summary>
    [Call("app.resetAll")]
    private async Task ResetLauncher()
    {
        if (App.TestModeOn) throw new ApiException("Press Stop first: the test server is running.");
        if (App.Game?.LoadoutFile is { } loadout) DeleteQuietly(loadout);
        foreach (var dir in new[] { Path.GetDirectoryName(LauncherSettings.FilePath)!, AppState.LauncherDataDir })
            if (Directory.Exists(dir))
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) DeleteQuietly(file);
        _inventory = null;
        Process.Start(new ProcessStartInfo(Updater.CurrentExe) { UseShellExecute = true });
        await OnUi(() =>
        {
            // No window position is saved on the way out: launcher.json is gone and stays gone.
            (Window as ShellForm)?.CloseForUpdate(saveWindow: false);
            var exit = new System.Windows.Forms.Timer { Interval = 4000 };
            exit.Tick += (_, _) => Environment.Exit(0);
            exit.Start();
        });
    }

    /// <summary>Files in use (the window's own browser cache) stay; the rest goes.</summary>
    private static void DeleteQuietly(string file)
    {
        try { File.Delete(file); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* in use */ }
    }

    /// <summary>Copies inventory.json and loadouts.json to a dated folder before an update; keeps the last 5.</summary>
    private void BackupUserData()
    {
        try
        {
            var root = Path.Combine(AppState.LauncherDataDir, "backups");
            var dir = Directory.CreateDirectory(Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss"))).FullName;
            foreach (var file in new[] { InventoryFile, App.Game?.LoadoutFile })
                if (file != null && File.Exists(file)) File.Copy(file, Path.Combine(dir, Path.GetFileName(file)));
            foreach (var old in Directory.GetDirectories(root).OrderByDescending(d => d).Skip(5)) Directory.Delete(old, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log("Could not back up your data: " + ex.Message); }
    }
}
