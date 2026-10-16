using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using Microsoft.Win32;

namespace LanSkinTester.Launcher.Web;

/// <summary>The window around the pages: status, navigation, links, launcher updates, auto-restore.</summary>
public partial class Api
{
    private bool _updateAvailable;
    private string? _updateProgress;
    private int _cs2GoneTicks;

    /// <summary>Called once the server is up: find CS2 and look for a launcher update.</summary>
    public void Started()
    {
        App.SetGame(GameFolder.IsValid(App.Settings.GamePath)
            ? App.Settings.GamePath
            : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
        if (App.Game == null) App.Log("CS2 not found automatically - press Browse on the Setup page.");
        _ = CheckForUpdateAsync();
        // Look again every few hours while the launcher stays open.
        var timer = new System.Windows.Forms.Timer { Interval = 4 * 60 * 60 * 1000 };
        timer.Tick += (_, _) => { if (!_updateAvailable) _ = CheckForUpdateAsync(); };
        timer.Start();
    }

    public record ShellState(string Version, string Page, bool Ready, bool TestMode, string StatusKind, string StatusTitle,
        string StatusDetail, bool UpdateAvailable, string? UpdateProgress);

    [Call("app.state")]
    private ShellState State()
    {
        var (kind, title, detail) = App.TestModeOn ? ("warning", "Test mode on", "VAC off · press Stop when done")
            : App.Ready ? ("good", "Ready", "CS2 is normal")
            : ("bad", "Setup needed", "Open Setup to install");
        // First run (or something missing): start on Setup; otherwise where you left off.
        var page = Program.StartPage ?? (!App.Ready || App.Db.IsEmpty ? "setup" : App.Settings.LastPage ?? "play");
        return new ShellState(UpdateCheck.ShortVersion, page, App.Ready, App.TestModeOn, kind, title, detail, _updateAvailable, _updateProgress);
    }

    [Call("app.fullscreen")]
    private Task ToggleFullscreen() => OnUi(() => (Window as ShellForm)?.ToggleFullscreen());

    [Call("app.page")]
    private void SavePage(string page)
    {
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

    private async Task CheckForUpdateAsync()
    {
        if (!await UpdateCheck.NewerAvailableAsync(App.Http)) return;
        _updateAvailable = true;
        App.Log("A newer launcher build is on GitHub - click \"Update available\" in the sidebar to install it.");
        Toast("Update available - click the card in the sidebar.");
        App.NotifyChanged();
    }

    private bool _updating;

    /// <summary>Downloads the new launcher, swaps it in after closing, and restarts.</summary>
    [Call("app.update")]
    private async Task InstallUpdate()
    {
        if (_updating) return;
        if (App.TestModeOn) throw new ApiException("Press Stop first (test mode is on), then update.");
        if (!Updater.CanSelfUpdate())
        {
            App.Log("This folder isn't writable, so the launcher can't update itself - opening the download page.");
            OpenUrl(UpdateCheck.ReleaseUrl);
            return;
        }

        _updating = true;
        try
        {
            var file = await Updater.DownloadAsync(App.Http, p =>
            {
                _updateProgress = $"Downloading {p:P0}";
                App.NotifyChanged();
            });
            _updateProgress = "Restarting";
            App.NotifyChanged();
            App.Log("Update downloaded - restarting the launcher.");
            Updater.ReplaceAfterExit(file);
            await OnUi(Window.Close);
        }
        catch (Exception ex)
        {
            _updateProgress = null;
            App.NotifyChanged();
            App.Log("Update failed: " + ex.Message + " - opening the download page instead.");
            OpenUrl(UpdateCheck.ReleaseUrl);
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
