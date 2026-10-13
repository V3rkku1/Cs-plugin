using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;
using Microsoft.Win32;

namespace LanSkinTester.Launcher;

/// <summary>Window shell: top bar with PLAY / SKINS / SETUP and the test-mode badge.</summary>
public class MainForm : Form
{
    private readonly AppState _app = new();
    private readonly Panel _pages = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly PlayPage _play;
    private readonly SkinsPage _skins;
    private readonly SetupPage _setup;
    private readonly Button _navPlay = Theme.MakeButton("PLAY", Theme.ButtonKind.Ghost, 110);
    private readonly Button _navSkins = Theme.MakeButton("SKINS", Theme.ButtonKind.Ghost, 110);
    private readonly Button _navSetup = Theme.MakeButton("SETUP", Theme.ButtonKind.Ghost, 110);
    private readonly Label _modePill = new() { AutoSize = true, Font = Theme.Bold, Padding = new Padding(10, 5, 10, 5), Margin = new Padding(0, 12, 16, 0) };

    public MainForm()
    {
        Text = "LAN Skin Tester";
        Font = Theme.Body;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Size = new Size(1320, 900);
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;

        _play = new PlayPage(_app) { Dock = DockStyle.Fill };
        _skins = new SkinsPage(_app.Images) { Dock = DockStyle.Fill };
        _setup = new SetupPage(_app) { Dock = DockStyle.Fill };
        _pages.Controls.AddRange([_play, _skins, _setup]);

        Controls.Add(_pages);
        Controls.Add(BuildTopBar());
        Theme.Apply(this);

        _app.Changed += OnChanged;
        Load += (_, _) =>
        {
            _app.SetGame(GameFolder.IsValid(_app.Settings.GamePath)
                ? _app.Settings.GamePath
                : GameFolder.Find(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string));
            // First run (or something missing): start on SETUP.
            ShowPage(_app.Ready && !_app.Db.IsEmpty ? _play : _setup);
            if (_app.Game == null) _app.Log("CS2 not found automatically - press Browse on the SETUP page.");
        };
        FormClosing += OnClosing;
    }

    private Control BuildTopBar()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Theme.Surface };
        var title = new Label { Text = "LAN SKIN TESTER", Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Location = new Point(20, 14) };
        var nav = new FlowLayoutPanel { AutoSize = true, Location = new Point(260, 12), BackColor = Theme.Surface };
        foreach (var b in new[] { _navPlay, _navSkins, _navSetup }) b.Height = 34;
        nav.Controls.AddRange([_navPlay, _navSkins, _navSetup]);
        _navPlay.Click += (_, _) => ShowPage(_play);
        _navSkins.Click += (_, _) => ShowPage(_skins);
        _navSetup.Click += (_, _) => ShowPage(_setup);

        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, BackColor = Theme.Surface, FlowDirection = FlowDirection.RightToLeft };
        right.Controls.Add(_modePill);

        bar.Controls.Add(right);
        bar.Controls.Add(nav);
        bar.Controls.Add(title);
        return bar;
    }

    private void ShowPage(Control page)
    {
        page.Visible = true;
        page.BringToFront();
        foreach (Control other in _pages.Controls) if (other != page) other.Visible = false;

        foreach (var (button, target) in new[] { (_navPlay, (Control)_play), (_navSkins, _skins), (_navSetup, _setup) })
        {
            var on = target == page;
            button.ForeColor = on ? Theme.Accent : Theme.Muted;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = on ? Theme.Card : Theme.Surface;
        }
        if (page == _skins) _skins.Reload();
    }

    private void OnChanged()
    {
        if (InvokeRequired)
        {
            BeginInvoke(OnChanged);
            return;
        }

        var on = _app.TestModeOn;
        _modePill.Text = on ? "TEST MODE ON" : _app.Ready ? "CS2 NORMAL" : "SETUP NEEDED";
        _modePill.BackColor = on ? Theme.Accent : Theme.Card;
        _modePill.ForeColor = on ? Color.FromArgb(20, 16, 8) : _app.Ready ? Theme.Good : Theme.Bad;
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
