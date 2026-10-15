using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;

namespace LanSkinTester.Launcher;

/// <summary>
/// Hacker mode on its own page: who hacks, quick profiles, and every cheat grouped by kind. Changes are
/// saved at once and sent to the running server right away (no Apply button needed).
/// </summary>
public class HackerPage : UserControl
{
    private readonly AppState _app;
    private bool _loading;

    private readonly Segmented _mode = new(ServerSettings.HackerModes, 110);
    private readonly SettingRow _modeRow;

    // Aimbot
    private readonly Toggle _aimbot = new();
    private readonly Picker _aimKey = new(200);
    private readonly Segmented _aimStyle = new(ServerSettings.AimStyles, 88);
    private readonly Segmented _aimBone = new(ServerSettings.AimBones, 76);
    private readonly Segmented _aimTarget = new(ServerSettings.AimTargets, 150, 2);
    private readonly TextField _bindKey = new(110) { PlaceholderText = "e.g. mouse4", MaxLength = 16 };
    private readonly Slider _aggression = new() { Minimum = 1, Maximum = 10, Width = 200 };
    private readonly Label _aggressionLabel = new SmoothLabel { AutoSize = false, Width = 64, Height = 28, Font = Theme.Bold, ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight };
    private readonly Toggle _aimWalls = new();
    private readonly Toggle _trigger = new();

    // Visuals
    private readonly Toggle _wallhack = new();
    private readonly Toggle _boxEsp = new();
    private readonly Toggle _boneEsp = new();
    private readonly Toggle _tags = new();
    private readonly Toggle _radar = new();
    private readonly Toggle _espInfo = new();
    private readonly Toggle _noFlash = new();

    // Weapons
    private readonly Toggle _noRecoil = new();
    private readonly Toggle _noSpread = new();
    private readonly Toggle _ammo = new();
    private readonly Toggle _damage = new();

    // Movement
    private readonly Toggle _speed = new();
    private readonly Toggle _bhop = new();

    private readonly SettingRow _liveRow;
    private readonly System.Windows.Forms.Timer _applyTimer = new() { Interval = 500 };

    private static readonly (string Title, string Subtitle, Action<ServerSettings> Apply)[] Profiles =
    [
        ("Legit", "Smooth aim, looks like a good player", s =>
        {
            Set(s, wallhack: true, aimbot: true, radar: true, trigger: true);
            s.HackAggression = 3;
        }),
        ("Closet", "Silent aim + walls, nothing loud", s =>
        {
            Set(s, wallhack: true, aimbot: true, radar: true, noFlash: true, noRecoil: true, tags: true, trigger: true, style: 1);
            s.HackAggression = 5;
        }),
        ("Rage", "Snap aim, ESP and trigger, through walls", s =>
        {
            Set(s, wallhack: true, aimbot: true, aimWalls: true, radar: true, esp: true, noFlash: true, noRecoil: true,
                noSpread: true, ammo: true, damage: true, speed: true, bhop: true,
                box: true, bones: true, tags: true, trigger: true, style: 2);
            s.HackAggression = 10;
            s.HackAimKey = 1; // always on
        }),
        ("Movement", "Speed and bunnyhop only", s => Set(s, speed: true, bhop: true, noFlash: true)),
    ];

    private static void Set(ServerSettings s, bool wallhack = false, bool aimbot = false, bool aimWalls = false, bool radar = false,
        bool esp = false, bool noFlash = false, bool noRecoil = false, bool noSpread = false, bool ammo = false, bool damage = false,
        bool speed = false, bool bhop = false, bool box = false, bool bones = false, bool tags = false, bool trigger = false,
        int style = 0, int bone = 0)
    {
        s.HackWallhack = wallhack;
        s.HackAimbot = aimbot;
        s.HackAimWalls = aimWalls;
        s.HackRadar = radar;
        s.HackEspInfo = esp;
        s.HackNoFlash = noFlash;
        s.HackNoRecoil = noRecoil;
        s.HackNoSpread = noSpread;
        s.HackAmmo = ammo;
        s.HackOneTap = damage;
        s.HackSpeed = speed;
        s.HackBhop = bhop;
        s.HackBoxEsp = box;
        s.HackBoneEsp = bones;
        s.HackTags = tags;
        s.HackTrigger = trigger;
        s.HackAimStyle = style;
        s.HackAimBone = bone;
        s.HackAimKey = 0;
        if (s.HackerMode == 0) s.HackerMode = 1;
    }

    public HackerPage(AppState app)
    {
        _app = app;
        BackColor = Theme.Background;
        _aimKey.SetItems(ServerSettings.AimKeys);

        const int left = 560;
        const int right = 500;

        // ---- Left column ----
        var mode = new Group(left);
        _modeRow = mode.AddRow("Who Hacks", _mode, "");
        var profiles = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(left - 24, 0), BackColor = Color.Transparent, Margin = Padding.Empty };
        foreach (var profile in Profiles)
        {
            var button = new PillButton(profile.Title) { Margin = new Padding(0, 0, 8, 0) };
            button.Click += (_, _) => UseProfile(profile.Title, profile.Apply);
            new ToolTip().SetToolTip(button, profile.Subtitle);
            profiles.Controls.Add(button);
        }
        var off = new PillButton("All Off", PillButton.Style.Destructive);
        off.Click += (_, _) => UseProfile("All off", s => { Set(s); s.HackerMode = 0; });
        profiles.Controls.Add(off);
        mode.AddRow("Quick Profiles", null, "One click sets all the cheats below");
        mode.AddContent(profiles, new Padding(14, 0, 14, 12));

        var aim = new Group(left);
        aim.AddRow("Aimbot", _aimbot, "Moves your aim onto an enemy");
        aim.AddRow("Style", _aimStyle, "Legit eases on · Silent aims the bullet · Rage snaps and shoots");
        aim.AddRow("Body", _aimBone, "Head, neck or chest");
        aim.AddRow("Aim At", _aimTarget, "Distance, crosshair, lowest HP, or who is looking at you");
        aim.AddRow("Aim Key", _aimKey, "When the aimbot works");
        _bindKey.Margin = Padding.Empty;
        aim.AddRow("Own Aim Key", _bindKey, "Any key: mouse4, mouse5, x, alt, f... · bound in CS2 on this PC");
        aim.AddRow("Aggression", new HStack(_aggression, _aggressionLabel), "1 gentle and human · 10 instant lock. Also shortens the trigger delay");
        aim.AddRow("Aim Through Walls", _aimWalls, "Aimbot and triggerbot also lock through walls");
        aim.AddRow("Triggerbot", _trigger, "Shoots when an enemy crosses your crosshair, after a short random delay");

        var visuals = new Group(left);
        visuals.AddRow("Chams", _wallhack, "Solid colored player models, visible through walls");
        visuals.AddRow("Box ESP", _boxEsp, "3D box around enemies and teammates");
        visuals.AddRow("Skeleton ESP", _boneEsp, "Pose stick-figure through walls");
        visuals.AddRow("Name Tags", _tags, "Name, HP, armor, gun and grenades above each player");
        visuals.AddRow("Radar Hack", _radar, "Every enemy shows on your radar, even when they are quiet");
        visuals.AddRow("Enemy Info", _espInfo, "Three nearest enemies: name, HP, armor, gun, utility, distance");
        visuals.AddRow("No Flash", _noFlash, "Flashbangs don't blind you");

        var leftColumn = new VStack(
            Theme.Caption("MODE"), mode,
            Theme.Caption("AIMBOT"), aim,
            Theme.Caption("VISUALS"), visuals) { Margin = new Padding(0, 0, 28, 0) };

        // ---- Right column ----
        var weapons = new Group(right);
        weapons.AddRow("Recoil Control", _noRecoil, "Pulls the spray back onto the crosshair for you");
        weapons.AddRow("Zero Spread", _noSpread, "Every bullet goes where you aim · whole server");
        weapons.AddRow("Infinite Ammo", _ammo, "Never reload");
        weapons.AddRow("Damage Boost", _damage, "2× damage");

        var movement = new Group(right);
        movement.AddRow("Speedhack", _speed, "Run 35% faster");
        movement.AddRow("Bunnyhop", _bhop, "Hold jump to bhop perfectly");

        var inGame = new Group(right);
        _liveRow = inGame.AddRow("Server not running", null, "Changes apply as soon as the server runs");
        inGame.AddRow("!cheats", null, "Cheat menu in game · bind a key: bind F3 css_cheats");
        inGame.AddRow("!aimkey", null, "Pick your own aim key in game: !aimkey mouse2");
        inGame.AddRow("!aimtoggle", null, "Aimbot always on / off · bind x css_aimtoggle");

        var rightColumn = new VStack(
            Theme.Caption("WEAPONS"), weapons,
            Theme.Caption("MOVEMENT"), movement,
            Theme.Caption("IN GAME"), inGame);

        var page = new VStack(PlayPage.Header("Hacker Mode", "Fun cheats on your own server. Nobody's game is changed and nothing gets you banned."),
            new HStack(leftColumn, rightColumn));
        Controls.Add(new ScrollPanel { Dock = DockStyle.Fill, Padding = new Padding(36, 26, 24, 28), Content = page });
        Theme.Apply(this);

        LoadSettings();

        foreach (var toggle in new[] { _aimbot, _aimWalls, _trigger, _wallhack, _boxEsp, _boneEsp, _tags, _radar, _espInfo, _noFlash, _noRecoil, _noSpread, _ammo, _damage, _speed, _bhop })
            toggle.CheckedChanged += (_, _) => Changed();
        _mode.SelectedChanged += (_, _) => Changed();
        _aimKey.SelectedIndexChanged += (_, _) => Changed();
        _aimStyle.SelectedChanged += (_, _) => Changed();
        _aimBone.SelectedChanged += (_, _) => Changed();
        _aimTarget.SelectedChanged += (_, _) => Changed();
        _aggression.ValueChanged += (_, _) =>
        {
            _aggressionLabel.Text = AggressionText(_aggression.Value);
            Changed();
        };
        _bindKey.TextChanged += (_, _) => Changed();

        _applyTimer.Tick += (_, _) =>
        {
            _applyTimer.Stop();
            ApplyLive();
        };
        _app.Changed += RefreshLive;
        VisibleChanged += (_, _) =>
        {
            if (!Visible) return;
            LoadSettings(); // a quick start on the Play page may have changed them
            RefreshLive();
        };
    }

    private static string AggressionText(int level) => level switch { <= 3 => $"{level} legit", <= 7 => $"{level}", _ => $"{level} rage" };

    public void LoadSettings()
    {
        _loading = true;
        var s = _app.Settings.Server;
        _mode.SelectedIndex = Math.Clamp(s.HackerMode, 0, 2);
        _aimbot.Checked = s.HackAimbot;
        _aimKey.SelectedIndex = Math.Clamp(s.HackAimKey, 0, ServerSettings.AimKeys.Length - 1);
        _aimStyle.SelectedIndex = Math.Clamp(s.HackAimStyle, 0, ServerSettings.AimStyles.Length - 1);
        _aimBone.SelectedIndex = Math.Clamp(s.HackAimBone, 0, ServerSettings.AimBones.Length - 1);
        _aimTarget.SelectedIndex = Math.Clamp(s.HackAimTarget, 0, ServerSettings.AimTargets.Length - 1);
        _bindKey.Text = s.AimBindKey;
        _aggression.Value = Math.Clamp(s.HackAggression, 1, 10);
        _aggressionLabel.Text = AggressionText(_aggression.Value);
        _aimWalls.Checked = s.HackAimWalls;
        _trigger.Checked = s.HackTrigger;
        _wallhack.Checked = s.HackWallhack;
        _boxEsp.Checked = s.HackBoxEsp;
        _boneEsp.Checked = s.HackBoneEsp;
        _tags.Checked = s.HackTags;
        _radar.Checked = s.HackRadar;
        _espInfo.Checked = s.HackEspInfo;
        _noFlash.Checked = s.HackNoFlash;
        _noRecoil.Checked = s.HackNoRecoil;
        _noSpread.Checked = s.HackNoSpread;
        _ammo.Checked = s.HackAmmo;
        _damage.Checked = s.HackOneTap;
        _speed.Checked = s.HackSpeed;
        _bhop.Checked = s.HackBhop;
        _loading = false;
        UpdateHints();
    }

    private void Changed()
    {
        if (_loading) return;
        var s = _app.Settings.Server;
        s.HackerMode = _mode.SelectedIndex;
        s.HackAimbot = _aimbot.Checked;
        s.HackAimKey = _aimKey.SelectedIndex;
        s.HackAimStyle = _aimStyle.SelectedIndex;
        s.HackAimBone = _aimBone.SelectedIndex;
        s.HackAimTarget = _aimTarget.SelectedIndex;
        s.AimBindKey = _bindKey.Text.Trim();
        s.HackAggression = _aggression.Value;
        s.HackAimWalls = _aimWalls.Checked;
        s.HackTrigger = _trigger.Checked;
        s.HackWallhack = _wallhack.Checked;
        s.HackBoxEsp = _boxEsp.Checked;
        s.HackBoneEsp = _boneEsp.Checked;
        s.HackTags = _tags.Checked;
        s.HackRadar = _radar.Checked;
        s.HackEspInfo = _espInfo.Checked;
        s.HackNoFlash = _noFlash.Checked;
        s.HackNoRecoil = _noRecoil.Checked;
        s.HackNoSpread = _noSpread.Checked;
        s.HackAmmo = _ammo.Checked;
        s.HackOneTap = _damage.Checked;
        s.HackSpeed = _speed.Checked;
        s.HackBhop = _bhop.Checked;
        try { _app.Settings.Save(); }
        catch (IOException) { /* saved next time */ }
        UpdateHints();
        // Wait a moment so dragging the slider or typing a key sends one update, not twenty.
        _applyTimer.Stop();
        _applyTimer.Start();
    }

    private void UseProfile(string name, Action<ServerSettings> apply)
    {
        apply(_app.Settings.Server);
        LoadSettings();
        Changed();
        Toast.Show($"Hacker profile: {name}");
    }

    private void UpdateHints()
    {
        _modeRow.Subtitle = _mode.SelectedIndex switch
        {
            1 => "Everyone hacks · great vs bots",
            2 => "One hacker per round glows green",
            _ => "Off · pick who hacks to turn it on",
        };
        var key = ServerSettings.CleanKey(_bindKey.Text);
        var enabled = _mode.SelectedIndex != 0;
        foreach (var c in new Control[] { _aimbot, _aimStyle, _aimBone, _aimKey, _aimTarget, _bindKey, _aggression, _aimWalls, _trigger, _wallhack, _boxEsp, _boneEsp, _tags, _radar, _espInfo, _noFlash, _noRecoil, _noSpread, _ammo, _damage, _speed, _bhop })
            c.Enabled = enabled;
        var aimOn = enabled && _aimbot.Checked;
        var aimOrTrigger = enabled && (_aimbot.Checked || _trigger.Checked);
        _bindKey.Enabled = _aimKey.Enabled = aimOn;
        _aimStyle.Enabled = _aimBone.Enabled = _aimTarget.Enabled = _aggression.Enabled = _aimWalls.Enabled = aimOrTrigger;
        if (_bindKey.Text.Trim() != "" && key == null) _bindKey.PlaceholderText = "letters/numbers only";
    }

    private void ApplyLive()
    {
        if (_app.Game == null || !_app.TestModeOn) return;
        try
        {
            _app.Settings.Server.ApplyHackerToRunningServer(_app.Game);
            _app.Settings.Server.WriteClientCfg(_app.Game);
            RefreshLive(sent: true);
        }
        catch (IOException ex)
        {
            _app.Log("Could not send the hacker settings: " + ex.Message);
        }
    }

    private void RefreshLive() => RefreshLive(sent: false);

    private void RefreshLive(bool sent)
    {
        if (!IsHandleCreated) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => RefreshLive(sent));
            return;
        }
        var on = _app.TestModeOn;
        _liveRow.Title = on ? (sent ? "Sent to the server" : "Server running") : "Server not running";
        _liveRow.Subtitle = on
            ? "Every change here applies in game right away" + (ServerSettings.CleanKey(_bindKey.Text) != null ? " · own aim key works after CS2 restarts (or console: exec lanskintester_client)" : "")
            : "Start on the Play page · changes here apply right away";
        _liveRow.SubtitleColor = on ? Theme.Good : Theme.Muted;
    }
}
