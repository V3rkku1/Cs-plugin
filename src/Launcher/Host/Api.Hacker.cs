using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>
/// Hacker Mode page: who hacks, quick profiles and every power. Each change is saved at once and sent to
/// the running server right away (no Apply button), like the old HackerPage.
/// </summary>
public partial class Api
{
    /// <summary>The page's settings. In <c>hacker.set</c> every field is optional: only the ones given change.</summary>
    public record HackerSettings(
        int? Mode, bool? Aimbot, int? AimStyle, int? AimBone, int? AimTarget, int? AimKey, string? BindKey,
        int? Aggression, bool? AimWalls, bool? Trigger,
        bool? Chams, bool? BoxEsp, bool? BoneEsp, bool? Tags, bool? Radar, bool? EnemyInfo, bool? NoFlash,
        bool? NoRecoil, bool? NoSpread, bool? Ammo, bool? Damage, bool? Speed, bool? Bhop);

    public record HackerProfile(string Id, string Title, string Subtitle, bool Active);

    public record HackerState(HackerSettings Settings, List<HackerProfile> Profiles, bool Running, bool Sent,
        bool BindKeyValid, string Summary, string[] Modes, string[] AimStyles, string[] AimBones, string[] AimTargets, string[] AimKeys);

    /// <summary>The last change reached the running server (cleared when it stops).</summary>
    private bool _hackerSent;

    private static readonly (string Id, string Title, string Subtitle, Action<ServerSettings> Apply)[] HackerProfiles =
    [
        ("legit", "Legit", "Smooth aim, looks like a good player", s =>
        {
            SetPowers(s, wallhack: true, aimbot: true, radar: true, trigger: true);
            s.HackAggression = 3;
        }),
        ("closet", "Closet", "Silent aim + walls, nothing loud", s =>
        {
            SetPowers(s, wallhack: true, aimbot: true, radar: true, noFlash: true, noRecoil: true, tags: true, trigger: true, style: 1);
            s.HackAggression = 5;
        }),
        ("rage", "Rage", "Snap aim, ESP and trigger, through walls", s =>
        {
            SetPowers(s, wallhack: true, aimbot: true, aimWalls: true, radar: true, esp: true, noFlash: true, noRecoil: true,
                noSpread: true, ammo: true, damage: true, speed: true, bhop: true,
                box: true, bones: true, tags: true, trigger: true, style: 2);
            s.HackAggression = 10;
            s.HackAimKey = 1; // always on
        }),
        ("movement", "Movement", "Speed and bunnyhop only", s => SetPowers(s, speed: true, bhop: true, noFlash: true)),
        ("off", "All Off", "Turns hacker mode and every power off", s => { SetPowers(s); s.HackerMode = 0; }),
    ];

    // Same as the old HackerPage.Set: every power not named goes off, and hacker mode turns on (Everyone) if it was off.
    private static void SetPowers(ServerSettings s, bool wallhack = false, bool aimbot = false, bool aimWalls = false, bool radar = false,
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

    [Call("hacker.state")]
    private HackerState HackerStateNow()
    {
        var s = App.Settings.Server;
        var running = App.TestModeOn;
        if (!running) _hackerSent = false;
        var now = s.HackerCommands(withCvars: false).ToList();
        var profiles = HackerProfiles.Select(p =>
        {
            var test = s.Clone();
            p.Apply(test);
            return new HackerProfile(p.Id, p.Title, p.Subtitle, test.HackerCommands(withCvars: false).SequenceEqual(now));
        }).ToList();
        var settings = new HackerSettings(
            Math.Clamp(s.HackerMode, 0, 2), s.HackAimbot,
            Math.Clamp(s.HackAimStyle, 0, ServerSettings.AimStyles.Length - 1),
            Math.Clamp(s.HackAimBone, 0, ServerSettings.AimBones.Length - 1),
            Math.Clamp(s.HackAimTarget, 0, ServerSettings.AimTargets.Length - 1),
            Math.Clamp(s.HackAimKey, 0, ServerSettings.AimKeys.Length - 1),
            s.AimBindKey, Math.Clamp(s.HackAggression, 1, 10), s.HackAimWalls, s.HackTrigger,
            s.HackWallhack, s.HackBoxEsp, s.HackBoneEsp, s.HackTags, s.HackRadar, s.HackEspInfo, s.HackNoFlash,
            s.HackNoRecoil, s.HackNoSpread, s.HackAmmo, s.HackOneTap, s.HackSpeed, s.HackBhop);
        return new HackerState(settings, profiles, running, _hackerSent,
            s.AimBindKey.Trim() == "" || ServerSettings.CleanKey(s.AimBindKey) != null, HackerSummary(),
            ServerSettings.HackerModes, ServerSettings.AimStyles, ServerSettings.AimBones, ServerSettings.AimTargets, ServerSettings.AimKeys);
    }

    private string HackerSummary() => App.Settings.Server.HackerMode switch
    {
        1 => "On · everyone hacks",
        2 => "On · one random hacker per round",
        _ => "Off · aimbot, wallhack, radar and more",
    };

    [Call("hacker.set")]
    private void SetHacker(HackerSettings a)
    {
        var s = App.Settings.Server;
        if (a.Mode is { } mode) s.HackerMode = Math.Clamp(mode, 0, 2);
        if (a.Aimbot is { } aimbot) s.HackAimbot = aimbot;
        if (a.AimStyle is { } style) s.HackAimStyle = Math.Clamp(style, 0, ServerSettings.AimStyles.Length - 1);
        if (a.AimBone is { } bone) s.HackAimBone = Math.Clamp(bone, 0, ServerSettings.AimBones.Length - 1);
        if (a.AimTarget is { } target) s.HackAimTarget = Math.Clamp(target, 0, ServerSettings.AimTargets.Length - 1);
        if (a.AimKey is { } key) s.HackAimKey = Math.Clamp(key, 0, ServerSettings.AimKeys.Length - 1);
        if (a.BindKey is { } bind) s.AimBindKey = bind.Trim()[..Math.Min(bind.Trim().Length, 16)];
        if (a.Aggression is { } aggression) s.HackAggression = Math.Clamp(aggression, 1, 10);
        if (a.AimWalls is { } walls) s.HackAimWalls = walls;
        if (a.Trigger is { } trigger) s.HackTrigger = trigger;
        if (a.Chams is { } chams) s.HackWallhack = chams;
        if (a.BoxEsp is { } box) s.HackBoxEsp = box;
        if (a.BoneEsp is { } bones) s.HackBoneEsp = bones;
        if (a.Tags is { } tags) s.HackTags = tags;
        if (a.Radar is { } radar) s.HackRadar = radar;
        if (a.EnemyInfo is { } info) s.HackEspInfo = info;
        if (a.NoFlash is { } noFlash) s.HackNoFlash = noFlash;
        if (a.NoRecoil is { } noRecoil) s.HackNoRecoil = noRecoil;
        if (a.NoSpread is { } noSpread) s.HackNoSpread = noSpread;
        if (a.Ammo is { } ammo) s.HackAmmo = ammo;
        if (a.Damage is { } damage) s.HackOneTap = damage;
        if (a.Speed is { } speed) s.HackSpeed = speed;
        if (a.Bhop is { } bhop) s.HackBhop = bhop;
        SaveAndApplyHacker();
    }

    [Call("hacker.profile")]
    private void UseHackerProfile(string id)
    {
        var profile = HackerProfiles.FirstOrDefault(p => p.Id == id);
        if (profile.Apply == null) throw new ApiException("Unknown profile");
        profile.Apply(App.Settings.Server);
        SaveAndApplyHacker();
        Toast($"Hacker profile: {profile.Title}");
    }

    private void SaveAndApplyHacker()
    {
        App.SaveSettings();
        _hackerSent = false;
        if (App.Game != null && App.TestModeOn)
        {
            try
            {
                App.Settings.Server.ApplyHackerToRunningServer(App.Game);
                App.Settings.Server.WriteClientCfg(App.Game);
                _hackerSent = true;
            }
            catch (IOException ex)
            {
                App.Log("Could not send the hacker settings: " + ex.Message);
            }
        }
        App.NotifyChanged();
    }
}
