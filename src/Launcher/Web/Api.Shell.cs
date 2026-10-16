using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using Microsoft.Win32;

namespace LanSkinTester.Launcher.Web;

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
        App.SetGame(GameFolder.IsValid(App.Settings.GamePath)
            ? App.Settings.GamePath
            : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
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
        var (kind, title, detail) = App.TestModeOn ? ("warning", "Test mode on", "VAC off · press Stop when done")
            : App.Ready ? ("good", "Ready", "CS2 is normal")
            : ("bad", "Setup needed", "Open Setup to install");
        // First run (or something missing): start on Setup; otherwise where you left off.
        var page = Program.StartPage ?? (!App.Ready || App.Db.IsEmpty ? "setup" : App.Settings.LastPage ?? "play");
        return new ShellState(UpdateCheck.ShortVersion, page, App.Ready, App.TestModeOn, kind, title, detail, _updateAvailable, _updateProgress,
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
        try { App.Settings.Save(); }
        catch (IOException) { /* not important */ }
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
            try { App.Settings.Save(); }
            catch (IOException) { /* not important */ }
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
        try { App.Settings.Save(); }
        catch (IOException) { /* not important */ }
        App.NotifyChanged();
    }

    private bool _updating;

    /// <summary>Downloads the new launcher, swaps it in after closing, and restarts.</summary>
    [Call("app.update")]
    private Task InstallUpdate() => InstallUpdateAsync(automatic: false);

    private async Task InstallUpdateAsync(bool automatic)
    {
        if (_updating) return;
        if (App.TestModeOn)
        {
            if (automatic) return;
            // The new launcher can't take over a running test: close CS2 and the server, restore CS2.
            App.Log("Updating: stopping the test first (closes CS2 and the server).");
            await Task.Run(StopFromClose);
        }
        if (!Updater.CanSelfUpdate())
        {
            App.Log("This folder isn't writable, so the launcher can't update itself - opening the download page.");
            OpenUrl(UpdateCheck.ReleaseUrl);
            return;
        }

        _updating = true;
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
            });
            _updateProgress = "Restarting";
            App.NotifyChanged();
            App.Log("Update downloaded - restarting the launcher.");
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
            if (automatic)
            {
                // Don't throw a browser at someone who just opened the launcher: the sidebar card stays.
                App.Log("Automatic update failed: " + ex.Message + " - click \"Update available\" to try again.");
                Toast("Update failed - click the card in the sidebar to try again.", "bad");
            }
            else
            {
                App.Log("Update failed: " + ex.Message + " - opening the download page instead.");
                OpenUrl(UpdateCheck.ReleaseUrl);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Every 5 seconds: when CS2 and the test server are both closed but test mode is still on (game
    /// closed with the X, a crash, or a previous launcher session), put CS2 back to normal.
    /// </summary>
    public void WatchGame()
    {
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
            "No = leave it on (only start CS2 from this launcher until you press Stop)",
            "Restore CS2?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes)
        {
            try { StopFromClose(); }
            catch (Exception ex) { MessageBox.Show(Window, "Could not restore gameinfo.gi: " + ex.Message); }
        }
        return true;
    }
}
