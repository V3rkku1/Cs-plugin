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
    private readonly SkinsPage _skins;
    private readonly FriendsPage _friends;
    private readonly SetupPage _setup;
    private readonly SidebarItem _navPlay = new("Play", SidebarItem.Icon.Play);
    private readonly SidebarItem _navSkins = new("Skins", SidebarItem.Icon.Skins);
    private readonly SidebarItem _navFriends = new("Friends", SidebarItem.Icon.Friends);
    private readonly SidebarItem _navSetup = new("Setup", SidebarItem.Icon.Setup);
    private readonly StatusBadge _status = new();

    public MainForm()
    {
        Text = "LAN Skin Tester";
        Size = new Size(1400, 920);
        MinimumSize = new Size(1180, 740);
        StartPosition = FormStartPosition.CenterScreen;
        Theme.StyleWindow(this);

        _play = new PlayPage(_app) { Dock = DockStyle.Fill };
        _skins = new SkinsPage(_app.Images) { Dock = DockStyle.Fill };
        _friends = new FriendsPage(_app, _play) { Dock = DockStyle.Fill };
        _setup = new SetupPage(_app) { Dock = DockStyle.Fill };
        _pages.Controls.AddRange([_play, _skins, _friends, _setup]);

        Controls.Add(_pages);
        Controls.Add(BuildSidebar());
        Theme.Apply(this);

        _app.Changed += OnChanged;
        Load += (_, _) =>
        {
            _app.SetGame(GameFolder.IsValid(_app.Settings.GamePath)
                ? _app.Settings.GamePath
                : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
            // First run (or something missing): start on Setup.
            ShowPage(_app.Ready && !_app.Db.IsEmpty ? _play : _setup);
            if (_app.Game == null) _app.Log("CS2 not found automatically - press Browse on the Setup page.");
        };
        FormClosing += OnClosing;
    }

    private Control BuildSidebar()
    {
        var bar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar, Padding = new Padding(16, 22, 16, 16) };

        var app = new Label { Text = "Skin Tester", Font = Theme.Big, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Location = new Point(18, 20) };
        var sub = new Label { Text = "CS2 · LAN", Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = true, BackColor = Color.Transparent, Location = new Point(19, 48) };

        var nav = new VStack(_navPlay, _navSkins, _navFriends, _navSetup) { Location = new Point(16, 92) };
        _navPlay.Click += (_, _) => ShowPage(_play);
        _navSkins.Click += (_, _) => ShowPage(_skins);
        _navFriends.Click += (_, _) => ShowPage(_friends);
        _navSetup.Click += (_, _) => ShowPage(_setup);

        _status.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        bar.Controls.AddRange([app, sub, nav, _status]);
        bar.Layout += (_, _) => _status.Location = new Point(16, bar.Height - _status.Height - 18);

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
        _navSkins.Selected = page == _skins;
        _navFriends.Selected = page == _friends;
        _navSetup.Selected = page == _setup;
        if (page == _skins) _skins.Reload();
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
