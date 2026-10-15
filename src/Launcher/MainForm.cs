using System.Diagnostics;
using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;
using Microsoft.Win32;

namespace LanSkinTester.Launcher;

/// <summary>Window shell: macOS-style sidebar (Play / Skins / Setup + status) and the page area.</summary>
public class MainForm : Form
{
    private readonly AppState _app = new();
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly PlayPage _play;
    private readonly LiveServerPage _live;
    private readonly SkinsPage _skins;
    private readonly HackerPage _hacker;
    private readonly FriendsPage _friends;
    private readonly SetupPage _setup;
    private readonly SidebarItem _navPlay = new("Play", SidebarItem.Icon.Play);
    private readonly SidebarItem _navLive = new("Live Server", SidebarItem.Icon.Live);
    private readonly SidebarItem _navSkins = new("Skins", SidebarItem.Icon.Skins);
    private readonly SidebarItem _navHacker = new("Hacker Mode", SidebarItem.Icon.Hacker);
    private readonly SidebarItem _navFriends = new("Friends", SidebarItem.Icon.Friends);
    private readonly SidebarItem _navSetup = new("Setup", SidebarItem.Icon.Setup);
    private readonly StatusBadge _status = new();
    private readonly UpdateCard _update = new();
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 5000 };
    private int _cs2GoneTicks;

    public MainForm()
    {
        // Laid out for 96 DPI; WinForms scales everything up on 125% / 150% screens so text stays
        // sharp (the app is DPI aware) and still fits its boxes.
        SuspendLayout();
        Text = "LAN Skin Tester";
        Size = new Size(1400, 920);
        MinimumSize = new Size(1180, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.StyleWindow(this);
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); }
        catch (Exception) { /* default icon */ }

        _play = new PlayPage(_app) { Dock = DockStyle.Fill };
        _live = new LiveServerPage(_app, _play) { Dock = DockStyle.Fill };
        _skins = new SkinsPage(_app.Images) { Dock = DockStyle.Fill };
        _hacker = new HackerPage(_app) { Dock = DockStyle.Fill };
        _friends = new FriendsPage(_app, _play) { Dock = DockStyle.Fill };
        _play.OpenHackerPage += () => ShowPage(_hacker);
        _play.OpenLiveServer += () => ShowPage(_live);
        _setup = new SetupPage(_app) { Dock = DockStyle.Fill };
        _pages.Controls.AddRange([_play, _live, _skins, _hacker, _friends, _setup]);

        Controls.Add(_pages);
        Controls.Add(BuildSidebar());
        Theme.Apply(this);
        Toast.Attach(_pages);
        KeyPreview = true;
        RestoreWindow();

        _app.Changed += OnChanged;
        Load += (_, _) =>
        {
            _app.SetGame(GameFolder.IsValid(_app.Settings.GamePath)
                ? _app.Settings.GamePath
                : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
            // First run (or something missing): start on Setup; otherwise where you left off.
            ShowPage(!_app.Ready || _app.Db.IsEmpty ? _setup : PageByName(_app.Settings.LastPage) ?? _play);
            _watch.Tick += (_, _) => WatchGame();
            _watch.Start();
            WatchGame();
            _ = CheckForUpdateAsync();
            // Look again every few hours while the launcher stays open.
            var updateTimer = new System.Windows.Forms.Timer { Interval = 4 * 60 * 60 * 1000 };
            updateTimer.Tick += (_, _) => { if (!_update.Visible) _ = CheckForUpdateAsync(); };
            updateTimer.Start();
            if (_app.Game == null) _app.Log("CS2 not found automatically - press Browse on the Setup page.");
        };
        FormClosing += OnClosing;
        ResumeLayout(true);
    }

    private Control BuildSidebar()
    {
        var bar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar, Padding = new Padding(16, 22, 16, 16) };

        var logo = new PictureBox { Size = new Size(40, 40), Location = new Point(16, 20), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        using (var png = typeof(MainForm).Assembly.GetManifestResourceStream("app.png"))
            if (png != null) logo.Image = Image.FromStream(png);
        var app = new SmoothLabel { Text = "Skin Tester", Font = Theme.Big, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Location = new Point(62, 19) };
        var sub = new SmoothLabel { Text = $"CS2 · {UpdateCheck.ShortVersion}", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.Transparent, Location = new Point(63, 45) };

        var nav = new VStack(_navPlay, _navLive, _navSkins, _navHacker, _navFriends, _navSetup) { Location = new Point(16, 92) };
        _navPlay.Click += (_, _) => ShowPage(_play);
        _navLive.Click += (_, _) => ShowPage(_live);
        _navSkins.Click += (_, _) => ShowPage(_skins);
        _navHacker.Click += (_, _) => ShowPage(_hacker);
        _navFriends.Click += (_, _) => ShowPage(_friends);
        _navSetup.Click += (_, _) => ShowPage(_setup);

        _status.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        _update.Click += async (_, _) => await InstallUpdateAsync();
        bar.Controls.AddRange([logo, app, sub, nav, _update, _status]);
        bar.Layout += (_, _) =>
        {
            _status.Location = new Point(16, bar.Height - _status.Height - 18);
            _update.Location = new Point(16, _status.Top - _update.Height - 8);
        };

        // hairline between sidebar and content
        bar.Paint += (_, e) => { using var pen = new Pen(Theme.Separator); e.Graphics.DrawLine(pen, bar.Width - 1, 0, bar.Width - 1, bar.Height); };
        return bar;
    }

    private void ShowPage(Control page)
    {
        page.Visible = true;
        page.BringToFront();
        foreach (Control other in _pages.Controls) if (other != page) other.Visible = false;

        _navPlay.Selected = page == _play;
        _navLive.Selected = page == _live;
        _navSkins.Selected = page == _skins;
        _navHacker.Selected = page == _hacker;
        _navFriends.Selected = page == _friends;
        _navSetup.Selected = page == _setup;
        if (page == _skins) _skins.Reload();
        if (page == _live) _play.UpdateHackerRow();
        _app.Settings.LastPage = page == _play ? "play" : page == _live ? "live" : page == _skins ? "skins" : page == _hacker ? "hacker" : page == _friends ? "friends" : "setup";
        try { _app.Settings.Save(); }
        catch (IOException) { /* not important */ }
    }

    private Control? PageByName(string? name) => name switch
    {
        "play" => _play,
        "live" => _live,
        "skins" => _skins,
        "hacker" => _hacker,
        "friends" => _friends,
        "setup" => _setup,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------
    // Keyboard shortcuts
    // ---------------------------------------------------------------------------------------------

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.D1: ShowPage(_play); return true;
            case Keys.Control | Keys.D2: ShowPage(_live); return true;
            case Keys.Control | Keys.D3: ShowPage(_skins); return true;
            case Keys.Control | Keys.D4: ShowPage(_hacker); return true;
            case Keys.Control | Keys.D5: ShowPage(_friends); return true;
            case Keys.Control | Keys.D6: ShowPage(_setup); return true;
            case Keys.Control | Keys.F:
                ShowPage(_skins);
                _skins.FocusSearch();
                return true;
            case Keys.F5:
                _play.StartOrApply();
                return true;
            case Keys.Shift | Keys.F5:
                if (_app.TestModeOn) _play.StopServer();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------------------------------------
    // Window position, update check, auto-restore
    // ---------------------------------------------------------------------------------------------

    private void RestoreWindow()
    {
        if (_app.Settings.Window is not [var x, var y, var w, var h]) return;
        var bounds = new Rectangle(x, y, w, h);
        // Only if it's still on a connected screen (monitors change).
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds) && s.WorkingArea.Contains(bounds.Location + new Size(40, 20)))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(bounds.Location, new Size(Math.Max(bounds.Width, MinimumSize.Width), Math.Max(bounds.Height, MinimumSize.Height)));
        if (_app.Settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveWindow()
    {
        _app.Settings.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _app.Settings.Window = [b.X, b.Y, b.Width, b.Height];
        try { _app.Settings.Save(); }
        catch (IOException) { /* not important */ }
    }

    private async Task CheckForUpdateAsync()
    {
        if (!await UpdateCheck.NewerAvailableAsync(_app.Http)) return;
        _update.Visible = true;
        _app.Log("A newer launcher build is on GitHub - click \"Update available\" in the sidebar to install it.");
        Toast.Show("Update available - click the blue card in the sidebar.", Toast.Kind.Info);
    }

    private bool _updating;

    /// <summary>Downloads the new launcher, swaps it in after closing, and restarts.</summary>
    private async Task InstallUpdateAsync()
    {
        if (_updating) return;
        if (_app.TestModeOn)
        {
            MessageBox.Show(this, "Press Stop first (test mode is on), then update.", "Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Updater.CanSelfUpdate())
        {
            _app.Log("This folder isn't writable, so the launcher can't update itself - opening the download page.");
            OpenUrl(UpdateCheck.ReleaseUrl);
            return;
        }

        _updating = true;
        _update.Set("Updating…", "Downloading 0%");
        try
        {
            var file = await Updater.DownloadAsync(_app.Http, p => BeginInvoke(() => _update.Set("Updating…", $"Downloading {p:P0}")));
            _update.Set("Updating…", "Restarting");
            _app.Log("Update downloaded - restarting the launcher.");
            Updater.ReplaceAfterExit(file);
            Close();
        }
        catch (Exception ex)
        {
            _app.Log("Update failed: " + ex.Message + " - opening the download page instead.");
            _update.Set("Update available", "Click to try again");
            OpenUrl(UpdateCheck.ReleaseUrl);
        }
        finally
        {
            _updating = false;
        }
    }


    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser */ }
    }

    /// <summary>
    /// When CS2 and the test server are both closed but test mode is still on (game closed with the
    /// X, a crash, or a previous launcher session), put CS2 back to normal automatically.
    /// </summary>
    private void WatchGame()
    {
        if (!_app.TestModeOn || TestSession.Cs2Running())
        {
            _cs2GoneTicks = 0;
            return;
        }
        // Two checks in a row (about 5 seconds), so a restarting game isn't caught in between.
        if (++_cs2GoneTicks < 2) return;
        _cs2GoneTicks = 0;
        try
        {
            _play.AutoRestore();
            Toast.Show("CS2 closed - restored to normal. You can play online again.", Toast.Kind.Info);
        }
        catch (Exception ex)
        {
            _app.Log("Could not restore gameinfo.gi automatically: " + ex.Message);
        }
    }

    private void OnChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnChanged);
            return;
        }

        if (_app.TestModeOn) _status.Set(Theme.Warning, "Test mode on", "VAC off · use Stop when done");
        else if (_app.Ready) _status.Set(Theme.Good, "Ready", "CS2 is normal");
        else _status.Set(Theme.Bad, "Setup needed", "Open Setup to install");
        _skins.SetData(_app.Game, _app.Db);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        SaveWindow();
        if (!_app.TestModeOn) return;

        var answer = MessageBox.Show(this,
            "Skin test mode is still ON.\n\nYes = close CS2 + server and restore CS2 now (recommended)\n" +
            "No = leave it on (only start CS2 from this launcher until you press Stop)",
            "Restore CS2?", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);

        if (answer == DialogResult.Cancel) e.Cancel = true;
        else if (answer == DialogResult.Yes)
        {
            try { _play.StopFromClose(); }
            catch (Exception ex) { MessageBox.Show(this, "Could not restore gameinfo.gi: " + ex.Message); }
        }
    }
}
