using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// "Hacker mode": a fun game mode for your own server. Everything happens on the server (glow
/// entities, view angles, damage), so nobody's game is modified.
///   lst_hack off|all|one        everyone is a hacker, or one random player per round ("catch the hacker")
///   lst_hack_powers wallhack,boxesp,boneesp,tags,aimbot,trigger,onetap,ammo,speed   (or "all")
/// </summary>
public partial class LanSkinTester
{
    private enum HackMode { Off, Everyone, OneRandom }

    [Flags]
    private enum Power
    {
        None = 0, Wallhack = 1, Aimbot = 2, OneTap = 4, Ammo = 8, Speed = 16,
        Bhop = 32, NoFlash = 64, EspInfo = 128, AimWalls = 256, NoRecoil = 512, Radar = 1024,
        BoxEsp = 2048, BoneEsp = 4096, Tags = 8192, Trigger = 16384,
        All = 32767,
    }

    /// <summary>
    /// When the aimbot works. The server only sees the game's own actions (fire, scope, walk, use,
    /// inspect), so "custom" means any key the player binds to +lst_aim (the launcher does that).
    /// </summary>
    private enum AimKey { Fire, Always, Mouse2, Shift, E, F, Custom }

    private static readonly string[] AimKeyNames = ["Hold fire", "Always on", "Hold right click", "Hold Shift", "Hold E", "Hold F", "Own key only"];

    // A "legit" aimbot: only enemies you can actually see, a small lock-on area, a human-like reaction
    // delay, smooth movement and a slightly imperfect aim point - not an instant snap through walls.
    private const float AimbotFov = 20f;          // default lock-on area, degrees from the crosshair
    private const float AimSmoothing = 0.18f;      // default share of the remaining angle closed per tick
    private const float AimMaxStep = 3.5f;         // max degrees turned per tick
    private const float OneTapMultiplier = 2f;     // "damage boost"
    private const float SpeedMultiplier = 1.35f;

    private static readonly Color HackerGlow = Color.FromArgb(255, 57, 255, 20);
    private static readonly Color TerroristGlow = Color.FromArgb(255, 255, 90, 40);
    private static readonly Color CounterTerroristGlow = Color.FromArgb(255, 70, 150, 255);

    private static readonly string[] KillLines =
    [
        "{v} reported {h}. Report status: lol",
        "{h}: \"it's just a gaming chair\"",
        "{v} has been diagnosed with a skill issue",
        "{h} downloaded more RAM",
        "Overwatch is reviewing this... in 3-5 business years",
        "{h}: \"bro I'm literally playing on a trackpad\"",
        "VAC is thinking... still thinking...",
        "{h} pressed the win button",
        "{v} got prefired through 3 walls. Totally normal.",
        "{h}: \"I just have good game sense\"",
    ];

    private HackMode _hackMode;
    private Power _powers = Power.Wallhack | Power.Aimbot | Power.Ammo;

    /// <summary>Aimbot state per hacker: who it's locked on, since when, and where on the target it aims.</summary>
    private sealed class AimLock
    {
        public int TargetSlot = -1;
        public DateTime AcquiredAt;
        public double ReactionMs;
        public float OffsetZ;   // below the eyes: head / neck
        public float OffsetSide; // a few units left/right
    }

    private readonly Dictionary<int, AimLock> _aimLocks = new();

    /// <summary>Powers a player picked in the cheat menu (overrides the server-wide set), by slot.</summary>
    private readonly Dictionary<int, Power> _personal = new();

    /// <summary>Aimbot settings from the cheat menu, by slot.</summary>
    private readonly Dictionary<int, int> _aggression = new();

    /// <summary>Aimbot aggression 1-10 when a player hasn't picked one (launcher: lst_aim_aggression).</summary>
    private int _defaultAggression = 5;

    /// <summary>Aim key for everyone (launcher: lst_aim_key) and per-player picks from the cheat menu.</summary>
    private AimKey _defaultAimKey = AimKey.Fire;
    private readonly Dictionary<int, AimKey> _aimKeys = new();

    /// <summary>Players holding their own bound key (+lst_aim), or who toggled the aimbot on (lst_aim_toggle).</summary>
    private readonly HashSet<int> _aimHeld = new();
    private readonly HashSet<int> _aimToggled = new();

    /// <summary>Who the aimbot goes for.</summary>
    private enum AimTarget { Closest, Crosshair, Health, Threat }

    private static readonly string[] AimTargetNames = ["Closest enemy", "Near crosshair", "Lowest HP", "Highest threat"];
    private AimTarget _defaultAimTarget = AimTarget.Closest;
    private readonly Dictionary<int, AimTarget> _aimTargets = new();

    private AimTarget AimTargetOf(CCSPlayerController player) => _aimTargets.TryGetValue(player.Slot, out var t) ? t : _defaultAimTarget;

    private static AimTarget ParseAimTarget(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("cross")) return AimTarget.Crosshair;
        if (t is "health" or "hp" or "low" or "lowest") return AimTarget.Health;
        if (t.StartsWith("threat") || t is "danger") return AimTarget.Threat;
        return AimTarget.Closest;
    }

    [ConsoleCommand("lst_aim_target", "Aimbot target: closest | crosshair | health | threat")]
    [CommandHelper(minArgs: 1, usage: "closest|crosshair|health|threat", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAimTargetCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _defaultAimTarget = ParseAimTarget(cmd.GetArg(1));
    }

    /// <summary>Legit eases the crosshair. Silent aims the bullet while you shoot. Rage snaps and fires.</summary>
    private enum AimStyle { Legit, Silent, Rage }

    private enum AimBone { Head, Neck, Chest }

    private static readonly string[] AimStyleNames = ["Legit", "Silent", "Rage"];
    private static readonly string[] AimBoneNames = ["Head", "Neck", "Chest"];

    private AimStyle _defaultAimStyle = AimStyle.Legit;
    private AimBone _defaultAimBone = AimBone.Head;
    private readonly Dictionary<int, AimStyle> _aimStyles = new();
    private readonly Dictionary<int, AimBone> _aimBones = new();

    private AimStyle AimStyleOf(CCSPlayerController player) => _aimStyles.TryGetValue(player.Slot, out var s) ? s : _defaultAimStyle;
    private AimBone AimBoneOf(CCSPlayerController player) => _aimBones.TryGetValue(player.Slot, out var b) ? b : _defaultAimBone;

    private static AimStyle? ParseAimStyle(string text) => text.Trim().ToLowerInvariant() switch
    {
        "legit" or "smooth" or "human" => AimStyle.Legit,
        "silent" => AimStyle.Silent,
        "rage" or "snap" => AimStyle.Rage,
        _ => null,
    };

    private static AimBone? ParseAimBone(string text) => text.Trim().ToLowerInvariant() switch
    {
        "head" => AimBone.Head,
        "neck" => AimBone.Neck,
        "chest" or "body" => AimBone.Chest,
        _ => null,
    };

    [ConsoleCommand("lst_aim_style", "Aimbot style: legit (smooth) | silent (bullet only) | rage (snap and shoot)")]
    [CommandHelper(minArgs: 1, usage: "legit|silent|rage", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAimStyleCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (ParseAimStyle(cmd.GetArg(1)) is { } style) _defaultAimStyle = style;
    }

    [ConsoleCommand("lst_aim_bone", "Aimbot body part: head | neck | chest")]
    [CommandHelper(minArgs: 1, usage: "head|neck|chest", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAimBoneCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (ParseAimBone(cmd.GetArg(1)) is { } bone) _defaultAimBone = bone;
    }

    /// <summary>View angles to put back on the next tick after a silent shot.</summary>
    private readonly Dictionary<int, (float Pitch, float Yaw)> _silentRestore = new();

    /// <summary>Slots whose +attack we sent (triggerbot or rage), so we can release it.</summary>
    private readonly HashSet<int> _attackHeld = new();

    private sealed class TriggerLock
    {
        public int TargetSlot = -1;
        public DateTime SeenAt;
        public double DelayMs;
    }

    private readonly Dictionary<int, TriggerLock> _triggers = new();

    private AimKey AimKeyOf(CCSPlayerController player) => _aimKeys.TryGetValue(player.Slot, out var k) ? k : _defaultAimKey;

    private bool AimPressed(CCSPlayerController player)
    {
        if (_aimHeld.Contains(player.Slot) || _aimToggled.Contains(player.Slot)) return true;
        var b = player.Buttons;
        return AimKeyOf(player) switch
        {
            AimKey.Fire => (b & PlayerButtons.Attack) != 0,
            AimKey.Always => true,
            AimKey.Mouse2 => (b & PlayerButtons.Attack2) != 0,
            AimKey.Shift => (b & (PlayerButtons.Speed | PlayerButtons.Walk)) != 0,
            AimKey.E => (b & PlayerButtons.Use) != 0,
            AimKey.F => (b & PlayerButtons.Inspect) != 0,
            _ => false,
        };
    }

    private static AimKey? ParseAimKey(string text) => text.Trim().ToLowerInvariant() switch
    {
        "fire" or "mouse1" or "attack" => AimKey.Fire,
        "always" or "on" => AimKey.Always,
        "mouse2" or "scope" or "attack2" => AimKey.Mouse2,
        "shift" or "walk" or "speed" => AimKey.Shift,
        "e" or "use" => AimKey.E,
        "f" or "inspect" => AimKey.F,
        "custom" or "key" or "bind" => AimKey.Custom,
        _ => null,
    };

    [ConsoleCommand("lst_aim_key", "When the aimbot works: fire | always | mouse2 | shift | e | f | custom (+lst_aim only)")]
    [CommandHelper(minArgs: 1, usage: "fire|always|mouse2|shift|e|f|custom", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAimKeyCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (ParseAimKey(cmd.GetArg(1)) is { } key) _defaultAimKey = key;
    }

    [ConsoleCommand("css_aimkey", "Pick your aimbot key: fire, always, mouse2, shift, e, f or custom")]
    public void OnAimKeyChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        if (cmd.ArgCount < 2 || ParseAimKey(cmd.GetArg(1)) is not { } key)
        {
            Reply(player, $"Aim key: {AimKeyNames[(int)AimKeyOf(player)]}. Change: !aimkey fire|always|mouse2|shift|e|f. " +
                          "Any key: bind it in the console, e.g. bind mouse4 +lst_aim");
            return;
        }
        _aimKeys[player.Slot] = key;
        Reply(player, $"Aim key: {AimKeyNames[(int)key]}");
    }

    [ConsoleCommand("css_aimtoggle", "Turn the aimbot on/off (bind a key: bind x css_aimtoggle)")]
    public void OnAimToggleCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid || !IsHacker(player)) return;
        var on = _aimToggled.Add(player.Slot);
        if (!on) _aimToggled.Remove(player.Slot);
        player.PrintToCenter(on ? "Aimbot: ALWAYS ON" : "Aimbot: back to your aim key");
    }

    private void RegisterAimBindCommands()
    {
        try
        {
            // A key bound to +lst_aim sends +lst_aim when pressed and -lst_aim when released.
            AddCommand("+lst_aim", "Aimbot while this key is held (bind mouse4 +lst_aim)", (player, _) =>
            {
                if (player is { IsValid: true }) _aimHeld.Add(player.Slot);
            });
            AddCommand("-lst_aim", "Aimbot key released", (player, _) =>
            {
                if (player is { IsValid: true }) _aimHeld.Remove(player.Slot);
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not register +lst_aim (own aimbot key); !aimtoggle still works");
        }
    }

    private int AggressionOf(CCSPlayerController player) =>
        _aggression.TryGetValue(player.Slot, out var a) ? a : _defaultAggression;

    [ConsoleCommand("lst_aim_aggression", "Default aimbot aggression 1 (gentle) - 10 (hard head lock)")]
    [CommandHelper(minArgs: 1, usage: "1-10", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnAimAggressionCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (int.TryParse(cmd.GetArg(1), out var level)) _defaultAggression = Math.Clamp(level, 1, 10);
    }
    private readonly HashSet<int> _hackerSlots = new();
    private readonly Dictionary<int, (CCSPlayerController Owner, CDynamicProp Relay, CDynamicProp Glow)> _glows = new();

    private void LoadHackerMode()
    {
        RegisterListener<Listeners.CheckTransmit>(OnHackCheckTransmit);
        RegisterListener<Listeners.OnTick>(OnHackTick);
        RegisterListener<Listeners.OnServerPreEntityThink>(OnHackPreThink);
        RegisterListener<Listeners.OnEntitySpawned>(OnHackSmokeSpawned);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _glows.Clear();
            _hackerSlots.Clear();
            _cheatMenus.Clear(); // their boards went with the old map
            _aimLocks.Clear();
            _esp.Clear();
            _triggers.Clear();
            _silentRestore.Clear();
            _attackHeld.Clear();
            _forcedSince.Clear();
            _nextServerShot.Clear();
            _bhopReleased.Clear();
            _smokes.Clear();
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            RemoveGlow(slot);
            RemovePlayerEsp(slot);
            _hackerSlots.Remove(slot);
            _personal.Remove(slot);
            _aggression.Remove(slot);
            _aimLocks.Remove(slot);
            _aimKeys.Remove(slot);
            _aimTargets.Remove(slot);
            _aimStyles.Remove(slot);
            _aimBones.Remove(slot);
            _aimHeld.Remove(slot);
            _aimToggled.Remove(slot);
            _cheatMenus.Remove(slot);
            _espText.Remove(slot);
            _triggers.Remove(slot);
            _silentRestore.Remove(slot);
            _attackHeld.Remove(slot);
            ForgetShots(slot);
            _bhopReleased.Remove(slot);
        });
        RegisterEventHandler<EventRoundStart>(OnHackRoundStart);
        RegisterEventHandler<EventPlayerSpawn>(OnHackSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnHackDeath);
        RegisterEventHandler<EventWeaponFire>(OnHackWeaponFire);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnHackTakeDamage);
        RegisterEventHandler<EventPlayerBlind>(OnHackBlind);
        RegisterAimBindCommands();
        LoadCheatMenu();
    }

    private void UnloadHackerMode()
    {
        RemoveAllGlows();
    }

    // ---------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("lst_hack", "Hacker mode: off | all (every player hacks) | one (one random hacker per round)")]
    [CommandHelper(minArgs: 1, usage: "off|all|one", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnHackCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var mode = cmd.GetArg(1).ToLowerInvariant() switch
        {
            "all" or "everyone" or "1" => HackMode.Everyone,
            "one" or "random" or "2" => HackMode.OneRandom,
            _ => HackMode.Off,
        };
        if (mode == _hackMode) return;

        _hackMode = mode;
        _hackerSlots.Clear();
        _personal.Clear(); // everyone starts from the server's powers again
        if (mode == HackMode.OneRandom) PickRandomHacker();
        ResetSpeed();
        RebuildGlows();
        Announce();
    }

    [ConsoleCommand("lst_hack_powers", "Hacker powers: wallhack,boxesp,boneesp,tags,aimbot,trigger,aimwalls,onetap,ammo,speed,bhop,noflash,espinfo,norecoil,radar or all")]
    [CommandHelper(minArgs: 1, usage: "wallhack,boxesp,aimbot,trigger,...|all|none", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnHackPowersCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var powers = Power.None;
        foreach (var part in cmd.ArgString.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries))
            if (Enum.TryParse<Power>(part.Trim(), ignoreCase: true, out var p)) powers |= p;
        if (powers == _powers) return;

        _powers = powers;
        ResetSpeed();
        RebuildGlows();
    }

    [ConsoleCommand("css_hacks", "Show hacker mode status")]
    public void OnHacksChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        if (_hackMode == HackMode.Off)
        {
            Reply(player, IsHacker(player)
                ? $"Your cheats: {DescribePowers(player)}. !cheats opens the menu."
                : "Hacker mode is off. The host turns it on in the launcher (Play page, Hacker Mode).");
            return;
        }
        Reply(player, IsHacker(player)
            ? $"You are hacking: {DescribePowers(player)}{(Has(player, Power.Aimbot) ? $" - aimbot: {AimKeyNames[(int)AimKeyOf(player)].ToLowerInvariant()}" : "")}. !cheats opens the menu."
            : $"{string.Join(", ", Hackers().Select(h => h.PlayerName))} is hacking. Look for the green glow!");
    }

    // ---------------------------------------------------------------------------------------------
    // Who hacks
    // ---------------------------------------------------------------------------------------------

    private Power PowersOf(CCSPlayerController player) =>
        _personal.TryGetValue(player.Slot, out var own) ? own : _hackMode != HackMode.Off ? _powers : Power.None;

    private bool Has(CCSPlayerController player, Power power) => (PowersOf(player) & power) != 0;

    /// <summary>
    /// Everyone mode: every human. One random: the chosen one. Off: whoever turned cheats on for
    /// themselves in the cheat menu (practice, LAN / private servers only).
    /// </summary>
    private bool IsHacker(CCSPlayerController? player) => player != null && IsHumanPlayer(player) && _hackMode switch
    {
        HackMode.Everyone => true,
        HackMode.OneRandom => _hackerSlots.Contains(player.Slot),
        _ => false, // cheats only exist in hacker mode
    };

    private IEnumerable<CCSPlayerController> Hackers() => Utilities.GetPlayers().Where(IsHacker);

    private void PickRandomHacker()
    {
        var candidates = Humans().Where(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist).ToList();
        if (candidates.Count == 0) candidates = Humans().ToList();
        _hackerSlots.Clear();
        if (candidates.Count > 0) _hackerSlots.Add(candidates[Random.Shared.Next(candidates.Count)].Slot);
    }

    private static bool IsEnemy(CCSPlayerController a, CCSPlayerController b)
    {
        if (a.Slot == b.Slot || b.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) return false;
        return a.Team != b.Team || ConVar.Find("mp_teammates_are_enemies")?.GetPrimitiveValue<bool>() == true;
    }

    private string DescribePowers(CCSPlayerController player)
    {
        var names = new List<string>();
        if (Has(player, Power.Wallhack)) names.Add("chams");
        if (Has(player, Power.BoxEsp)) names.Add("box esp");
        if (Has(player, Power.BoneEsp)) names.Add("skeleton");
        if (Has(player, Power.Tags)) names.Add("tags");
        if (Has(player, Power.Aimbot))
        {
            var style = AimStyleNames[(int)AimStyleOf(player)].ToLowerInvariant();
            var bone = AimBoneNames[(int)AimBoneOf(player)].ToLowerInvariant();
            names.Add(Has(player, Power.AimWalls) ? $"aimbot {style} {bone} (through walls)" : $"aimbot {style} {bone}");
        }
        if (Has(player, Power.Trigger)) names.Add("triggerbot");
        if (Has(player, Power.EspInfo)) names.Add("enemy info");
        if (Has(player, Power.OneTap)) names.Add("damage boost");
        if (Has(player, Power.Ammo)) names.Add("infinite ammo");
        if (Has(player, Power.Speed)) names.Add("speed");
        if (Has(player, Power.Bhop)) names.Add("bhop");
        if (Has(player, Power.NoFlash)) names.Add("no flash");
        if (Has(player, Power.NoRecoil)) names.Add("recoil control");
        if (Has(player, Power.Radar)) names.Add("radar");
        return names.Count == 0 ? "nothing (all powers off)" : string.Join(" · ", names);
    }

    /// <summary>Center-screen ESP waits while an announcement is showing.</summary>
    private DateTime _quietUntil;

    private void Announce()
    {
        _quietUntil = DateTime.UtcNow.AddSeconds(6);
        foreach (var player in Humans())
        {
            if (_hackMode == HackMode.Off)
            {
                player.PrintToCenterHtml("<font color='#ffffff'>Hacker mode off.</font> Back to honest gaming.", 4);
                continue;
            }

            if (IsHacker(player))
                player.PrintToCenterHtml(
                    $"<font color='#39ff14' class='fontSize-l'>YOU ARE {(_hackMode == HackMode.Everyone ? "A" : "THE")} HACKER</font><br>" +
                    $"<font color='#aaaaaa'>{DescribePowers(player)}<br>!cheats opens the cheat menu</font>", 6);
            else
                player.PrintToCenterHtml(
                    $"<font color='#ff4040' class='fontSize-l'>{string.Join(", ", Hackers().Select(h => h.PlayerName))} IS HACKING</font><br>" +
                    "<font color='#aaaaaa'>They glow green. Get them!</font>", 6);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Events
    // ---------------------------------------------------------------------------------------------

    private HookResult OnHackRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_hackMode == HackMode.Off)
        {
            if (GlowsNeeded) AddTimer(0.5f, RebuildGlows); // someone has walls from the cheat menu
            return HookResult.Continue;
        }
        if (_hackMode == HackMode.OneRandom) PickRandomHacker();
        // Players respawn right after round_start: build the glows once they exist.
        AddTimer(0.5f, () =>
        {
            RebuildGlows();
            Announce();
        });
        return HookResult.Continue;
    }

    private HookResult OnHackSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!GlowsNeeded || player == null || !player.IsValid) return HookResult.Continue;
        // Deathmatch respawns: give the new pawn its glow.
        AddTimer(0.2f, () =>
        {
            if (player.IsValid && player.PawnIsAlive) CreateGlow(player);
        });
        return HookResult.Continue;
    }

    private HookResult OnHackDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        if (victim != null && victim.IsValid) RemoveGlow(victim.Slot);

        var attacker = @event.Attacker;
        if (victim == null || !IsHacker(attacker) || attacker!.Slot == victim.Slot)
            return HookResult.Continue;

        var line = KillLines[Random.Shared.Next(KillLines.Length)]
            .Replace("{h}", $"{ChatColors.Lime}{attacker.PlayerName}{ChatColors.Default}")
            .Replace("{v}", $"{ChatColors.Red}{victim.PlayerName}{ChatColors.Default}");
        Server.PrintToChatAll($" {ChatColors.Lime}[H4X]{ChatColors.Default} {line}");
        return HookResult.Continue;
    }

    private HookResult OnHackWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is { IsValid: true }) NoteRealShot(player);
        if (IsHacker(player) && Has(player!, Power.Aimbot) && AimStyleOf(player!) == AimStyle.Silent)
        {
            try { SilentShot(player!); }
            catch (Exception ex) { Logger.LogDebug(ex, "Silent aim shot"); }
        }
        if (!IsHacker(player) || !Has(player!, Power.Ammo)) return HookResult.Continue;

        Server.NextFrame(() =>
        {
            var weapon = player!.IsValid ? player.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value : null;
            if (weapon == null || !weapon.IsValid) return;
            var max = weapon.VData?.MaxClip1 ?? 0;
            if (max <= 0 || weapon.Clip1 >= max) return;
            weapon.Clip1 = max;
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
        });
        return HookResult.Continue;
    }

    private HookResult OnHackTakeDamage(CBaseEntity victim, CTakeDamageInfo damage)
    {
        if (_hackMode == HackMode.Off && _personal.Count == 0) return HookResult.Continue;
        try
        {
            if (victim.DesignerName != "player") return HookResult.Continue;

            var attackerPawn = damage.Attacker.Value?.As<CCSPlayerPawn>();
            var attacker = attackerPawn is { IsValid: true } ? attackerPawn.Controller.Value?.As<CCSPlayerController>() : null;
            var target = new CCSPlayerPawn(victim.Handle).Controller.Value?.As<CCSPlayerController>();
            if (IsHacker(attacker) && Has(attacker!, Power.OneTap) && target != null && IsEnemy(attacker!, target))
                damage.Damage *= OneTapMultiplier;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Hacker one-tap");
        }
        return HookResult.Continue;
    }

    // ---------------------------------------------------------------------------------------------
    // Aimbot + speed (every tick)
    // ---------------------------------------------------------------------------------------------

    private void OnHackTick()
    {
        TickCheatMenus();
        if (_hackMode == HackMode.Off && _personal.Count == 0)
        {
            ReleaseAllForcedAttacks();
            _silentRestore.Clear();
            ClearPlayerEsp();
            return;
        }
        _tick++;
        UpdatePlayerEsp();

        foreach (var hacker in Hackers())
        {
            if (!hacker.PawnIsAlive)
            {
                SetForcedAttack(hacker, false);
                continue;
            }
            var pawn = hacker.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            try
            {
                if (Has(hacker, Power.Bhop)) AutoBhop(hacker, pawn);
                if (Has(hacker, Power.EspInfo)) ShowEnemyInfo(hacker, pawn, refresh: _tick % 8 == 0);

                if (Has(hacker, Power.Speed) && pawn.VelocityModifier < SpeedMultiplier)
                {
                    pawn.VelocityModifier = SpeedMultiplier;
                    Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
                }

                if (Has(hacker, Power.NoRecoil)) RemoveRecoil(pawn);
                if (Has(hacker, Power.Radar) && _tick % 2 == 0) RevealOnRadar(hacker);

                var menu = _cheatMenus.ContainsKey(hacker.Slot);
                var triggerFire = false;
                if (!menu && Has(hacker, Power.Trigger)) triggerFire = TriggerTick(hacker, pawn);
                else
                {
                    _triggers.Remove(hacker.Slot);
                    _forcedSince.Remove(hacker.Slot);
                }

                var fire = triggerFire;
                if (!menu && Has(hacker, Power.Aimbot) && AimPressed(hacker)) fire |= Aim(hacker, pawn, triggerFire);
                else _aimLocks.Remove(hacker.Slot);
                SetForcedAttack(hacker, fire);

                // Silent aim: give the view back once they stop shooting.
                if (_silentRestore.TryGetValue(hacker.Slot, out var own) && !fire && (hacker.Buttons & PlayerButtons.Attack) == 0)
                {
                    TurnView(pawn, own.Pitch, own.Yaw);
                    _silentRestore.Remove(hacker.Slot);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Hacker tick failed for slot {Slot}", hacker.Slot);
            }
        }
    }

    /// <summary>Marks the slot so pre-think injects IN_ATTACK into that pawn's buttons. No client command.</summary>
    private void SetForcedAttack(CCSPlayerController player, bool down)
    {
        var changed = down ? _attackHeld.Add(player.Slot) : _attackHeld.Remove(player.Slot);
        if (!changed || player.IsBot) return;
        // The pawn's button word is rewritten from the client's own input each tick, so also press the
        // key on the client. If CS2 refuses the command, the button write in pre-think is all that's left.
        try { player.ExecuteClientCommand(down ? "+attack" : "-attack"); }
        catch (Exception ex) { Logger.LogDebug(ex, "Forced attack"); }
    }

    private void ReleaseAllForcedAttacks()
    {
        foreach (var slot in _attackHeld.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is { IsValid: true }) SetForcedAttack(player, false);
        }
        _attackHeld.Clear();
    }

    private bool _recoilBroken;

    /// <summary>
    /// Zero recoil for this player only: the server aims bullets with the player's view angles plus the
    /// "aim punch" (recoil kick), so clearing the punch every tick sends every bullet where they look.
    /// </summary>
    private void RemoveRecoil(CCSPlayerPawn pawn)
    {
        if (_recoilBroken) return;
        try
        {
            var punch = pawn.AimPunchServices;
            if (punch == null) return;
            foreach (var angle in new[] { punch.PredictableBaseAngle, punch.PredictableBaseAngleVel, punch.UnpredictableBaseAngle })
            {
                if (angle.X == 0 && angle.Y == 0 && angle.Z == 0) continue;
                angle.X = angle.Y = angle.Z = 0;
            }
        }
        catch (Exception ex)
        {
            _recoilBroken = true; // schema changed after a CS2 update: the server cvar still works
            Logger.LogWarning(ex, "Per-player no recoil failed");
        }
    }

    private bool _radarBroken;

    /// <summary>Radar hack: every enemy shows on the hacker's radar ("spotted" by them).</summary>
    private void RevealOnRadar(CCSPlayerController hacker)
    {
        if (_radarBroken) return;
        try
        {
            foreach (var enemy in Utilities.GetPlayers())
            {
                if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
                var pawn = enemy.PlayerPawn.Value;
                if (pawn == null || !pawn.IsValid) continue;
                var spotted = pawn.EntitySpottedState;
                var mask = spotted.SpottedByMask;
                var index = hacker.Slot / 32;
                var bit = 1u << (hacker.Slot % 32);
                if (spotted.Spotted && index < mask.Length && (mask[index] & bit) != 0) continue;
                spotted.Spotted = true;
                if (index < mask.Length) mask[index] |= bit;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_entitySpottedState");
            }
        }
        catch (Exception ex)
        {
            _radarBroken = true;
            Logger.LogWarning(ex, "Radar hack failed");
        }
    }

    // Line-of-sight traces hit the world only (walls, floors, props, glass), not players.
    private static readonly TraceOptions SightTrace = new()
    {
        InteractsAs = Contents.Player,
        InteractsWith = Contents.Solid | Contents.WorldGeometry | Contents.Window | Contents.BlockLos | Contents.PhysicsProp,
        InteractsExclude = Contents.Player | Contents.Hitbox | Contents.Debris,
    };

    private bool _traceBroken;

    /// <summary>
    /// True when there's a clear line from the viewer's eyes to the enemy's head or chest. (CS2's own
    /// "spotted" flag can't be used: shooting reveals you on the radar, so in deathmatch it is almost
    /// always on, walls or not.)
    /// </summary>
    private bool IsVisibleTo(CCSPlayerPawn enemyPawn, CCSPlayerPawn viewerPawn)
    {
        if (_traceBroken || viewerPawn.AbsOrigin is not { } from || enemyPawn.AbsOrigin is not { } to) return false;
        var eye = new Vector(from.X, from.Y, from.Z + EyeHeight(viewerPawn));
        var enemyEyes = to.Z + EyeHeight(enemyPawn);
        try
        {
            foreach (var z in new[] { enemyEyes - 2f, enemyEyes - 20f }) // head, then chest
            {
                var result = Trace.TraceEndShape(eye, new Vector(to.X, to.Y, z), viewerPawn, SightTrace);
                if (!result.DidHit() || result.Fraction >= 0.97f) return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            // Missing gamedata after a CS2 update: never aim through walls, just stop aiming.
            _traceBroken = true;
            Logger.LogError(ex, "Line-of-sight trace failed - the aimbot is disabled until the plugin/CounterStrikeSharp is updated");
            return false;
        }
    }

    /// <summary>Angles from the hacker's eyes to a point on the enemy (head/neck with a small offset).</summary>
    private static (float Pitch, float Yaw)? AnglesTo(CCSPlayerPawn pawn, CCSPlayerPawn enemyPawn, AimLock? aim)
    {
        var origin = pawn.AbsOrigin;
        var target = enemyPawn.AbsOrigin;
        if (origin == null || target == null) return null;

        var dx = target.X - origin.X;
        var dy = target.Y - origin.Y;
        var yaw = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
        if (aim != null)
        {
            // Sideways offset, perpendicular to the line of sight.
            var rad = (yaw + 90) * Math.PI / 180;
            dx += (float)Math.Cos(rad) * aim.OffsetSide;
            dy += (float)Math.Sin(rad) * aim.OffsetSide;
            yaw = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);
        }
        var dz = target.Z + EyeHeight(enemyPawn) - (aim?.OffsetZ ?? 2f) - (origin.Z + EyeHeight(pawn));
        var pitch = (float)(-Math.Atan2(dz, Math.Sqrt(dx * dx + dy * dy)) * 180 / Math.PI);
        return (pitch, yaw);
    }

    private int _tick;

    /// <summary>Holding jump keeps jumping the moment you land (perfect bunnyhops).</summary>
    private static void AutoBhop(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        if ((player.Buttons & PlayerButtons.Jump) == 0 || (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) == 0) return;
        if (pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP) return;
        var v = pawn.AbsVelocity;
        pawn.Teleport(null, null, new Vector(v.X, v.Y, 301.99f));
    }

    private HookResult OnHackBlind(EventPlayerBlind @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!IsHacker(player) || !Has(player!, Power.NoFlash)) return HookResult.Continue;
        Server.NextFrame(() =>
        {
            var pawn = player!.IsValid ? player.PlayerPawn.Value : null;
            if (pawn == null || !pawn.IsValid) return;
            pawn.FlashDuration = 0;
            pawn.FlashMaxAlpha = 0;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashDuration");
            Utilities.SetStateChanged(pawn, "CCSPlayerPawnBase", "m_flFlashMaxAlpha");
        });
        return HookResult.Continue;
    }

    private static readonly string[] Arrows = ["↑", "↗", "→", "↘", "↓", "↙", "←", "↖"];

    /// <summary>Center-screen ESP: the three nearest enemies, with gear.</summary>
    private readonly Dictionary<int, string> _espText = new();

    /// <summary>
    /// Recomputes the lines every few ticks (refresh) but re-sends them every tick so the center panel
    /// stays steady instead of fading in and out.
    /// </summary>
    private void ShowEnemyInfo(CCSPlayerController hacker, CCSPlayerPawn pawn, bool refresh)
    {
        if (_cheatMenus.ContainsKey(hacker.Slot) || DateTime.UtcNow < _quietUntil) return;
        if (!refresh)
        {
            if (_espText.TryGetValue(hacker.Slot, out var cached) && cached != "") hacker.PrintToCenterHtml(cached, 1);
            return;
        }
        _espText[hacker.Slot] = "";
        var origin = pawn.AbsOrigin;
        if (origin == null) return;

        var found = new List<(CCSPlayerController Player, CCSPlayerPawn Pawn, double Distance, Vector Origin)>();
        foreach (var enemy in Utilities.GetPlayers())
        {
            if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn?.AbsOrigin is not { } o) continue;
            var d = Math.Sqrt(Math.Pow(o.X - origin.X, 2) + Math.Pow(o.Y - origin.Y, 2) + Math.Pow(o.Z - origin.Z, 2));
            found.Add((enemy, enemyPawn, d, o));
        }
        if (found.Count == 0) return;
        found.Sort((a, b) => a.Distance.CompareTo(b.Distance));

        var lines = new List<string>();
        foreach (var (enemy, enemyPawn, distance, target) in found.Take(3))
        {
            var yaw = Math.Atan2(target.Y - origin.Y, target.X - origin.X) * 180 / Math.PI;
            TryReadView(pawn, out _, out var viewYaw);
            var rel = AngleDiff(viewYaw, (float)yaw); // positive: enemy is to the right
            var arrow = Arrows[(int)Math.Round(((rel % 360) + 360) % 360 / 45.0) % 8];
            var visible = IsVisibleTo(enemyPawn, pawn);
            var gear = ReadGear(enemyPawn);
            var extra = gear.Gun == "" ? gear.Utility : gear.Utility == "" ? gear.Gun : $"{gear.Gun} {gear.Utility}";
            lines.Add($"<font color='{(visible ? "#ff5050" : "#ffb020")}'>{arrow} {System.Net.WebUtility.HtmlEncode(enemy.PlayerName)}</font>" +
                      $"<font color='#cccccc'> · {enemyPawn.Health} HP · {gear.Armor}" +
                      (extra == "" ? "" : $" · {System.Net.WebUtility.HtmlEncode(extra)}") +
                      $" · {distance * 0.0254:0} m{(visible ? " · VISIBLE" : "")}</font>");
        }
        var text = string.Join("<br>", lines);
        _espText[hacker.Slot] = text;
        hacker.PrintToCenterHtml(text, 1);
    }

    /// <summary>
    /// Aggression 1-10 sets everything: lock-on area (9-36 degrees), how hard it pulls (smoothing),
    /// reaction time and how precisely it hits the head. 5 is about the old "legit" default.
    /// </summary>
    private (float Fov, float Smooth) AimSettingsOf(CCSPlayerController player)
    {
        var a = AggressionOf(player);
        return (6f + 3f * a, 0.06f + 0.035f * a);
    }

    private (double Min, double Max) ReactionOf(CCSPlayerController player)
    {
        var min = 250.0 - 20.0 * AggressionOf(player);
        return (min, min + 110.0);
    }

    /// <summary>1 at aggression 1 (loose: head/neck, a few units off), 0 at 10 (dead-centre head).</summary>
    private float LoosenessOf(CCSPlayerController player) => (10 - AggressionOf(player)) / 9f;

    /// <summary>How far below the eyes the chosen body part sits.</summary>
    private static float BoneDrop(AimBone bone) => bone switch
    {
        AimBone.Neck => 12f,
        AimBone.Chest => 26f,
        _ => 2f,
    };

    /// <returns>True when rage should fire for this target.</returns>
    private bool Aim(CCSPlayerController hacker, CCSPlayerPawn pawn, bool triggerWantsShot)
    {
        var style = AimStyleOf(hacker);
        var (fov, smoothing) = AimSettingsOf(hacker);
        if (style == AimStyle.Rage) fov = 180f;
        var mode = AimTargetOf(hacker);
        var sticky = mode == AimTarget.Crosshair;
        if (!TryReadView(pawn, out var currentPitch, out var currentYaw)) return false;
        _aimLocks.TryGetValue(hacker.Slot, out var aim);
        var walls = Has(hacker, Power.AimWalls);

        // Crosshair mode keeps the current target while it stays visible and near the crosshair.
        // Distance, lowest HP and threat are re-checked every tick.
        CCSPlayerController? best = null;
        CCSPlayerPawn? targetPawn = null;
        var bestScore = sticky ? fov : double.MaxValue;
        // Every mode keeps a live lock: re-picking each tick restarted the reaction delay whenever two
        // enemies scored about the same, so the aim never moved.
        if (aim != null && Utilities.GetPlayerFromSlot(aim.TargetSlot) is { IsValid: true, PawnIsAlive: true } locked
            && IsEnemy(hacker, locked) && locked.PlayerPawn.Value is { IsValid: true } lockedPawn && (walls || IsVisibleTo(lockedPawn, pawn))
            && AnglesTo(pawn, lockedPawn, aim) is { } a
            && (!sticky || Math.Max(Math.Abs(AngleDiff(a.Yaw, currentYaw)), Math.Abs(AngleDiff(a.Pitch, currentPitch))) < fov * 1.5f))
        {
            best = locked;
            targetPawn = lockedPawn;
        }
        else
        {
            var origin = pawn.AbsOrigin;
            foreach (var enemy in Utilities.GetPlayers())
            {
                if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || enemyPawn.AbsOrigin is not { } o || origin == null) continue;
                double score = mode switch
                {
                    AimTarget.Closest => Math.Pow(o.X - origin.X, 2) + Math.Pow(o.Y - origin.Y, 2) + Math.Pow(o.Z - origin.Z, 2),
                    AimTarget.Health => enemyPawn.Health * 100000d + Math.Pow(o.X - origin.X, 2) + Math.Pow(o.Y - origin.Y, 2),
                    AimTarget.Threat => LookingAtMe(enemyPawn, pawn) * 100000d + Math.Sqrt(Math.Pow(o.X - origin.X, 2) + Math.Pow(o.Y - origin.Y, 2)),
                    _ => AnglesTo(pawn, enemyPawn, null) is { } angles
                        ? Math.Max(Math.Abs(AngleDiff(angles.Yaw, currentYaw)), Math.Abs(AngleDiff(angles.Pitch, currentPitch)))
                        : double.MaxValue,
                };
                // Trace only the better candidate. "Aim through walls" skips the line-of-sight check.
                if (score >= bestScore || (!walls && !IsVisibleTo(enemyPawn, pawn))) continue;
                bestScore = score;
                best = enemy;
                targetPawn = enemyPawn;
            }
        }
        if (best == null || targetPawn == null)
        {
            _aimLocks.Remove(hacker.Slot);
            return false;
        }
        if (aim == null || aim.TargetSlot != best.Slot)
        {
            var reaction = ReactionOf(hacker);
            var loose = style == AimStyle.Rage ? 0f : LoosenessOf(hacker);
            aim = new AimLock
            {
                TargetSlot = best.Slot,
                AcquiredAt = DateTime.UtcNow,
                ReactionMs = style == AimStyle.Rage ? 0 : (reaction.Min + Random.Shared.NextDouble() * (reaction.Max - reaction.Min)) * (sticky && aim != null ? 0.4 : 1),
                OffsetZ = BoneDrop(AimBoneOf(hacker)) + (float)Random.Shared.NextDouble() * 4f * loose,
                OffsetSide = (float)(Random.Shared.NextDouble() * 5 - 2.5) * loose,
            };
            _aimLocks[hacker.Slot] = aim;
        }

        if ((DateTime.UtcNow - aim.AcquiredAt).TotalMilliseconds < aim.ReactionMs) return false;
        if (AnglesTo(pawn, targetPawn, aim) is not { } goal) return false;

        if (style == AimStyle.Silent)
        {
            // The view never moves: each real shot hits the target server-side (SilentShot, on weapon_fire).
            return false;
        }

        if (style == AimStyle.Rage)
        {
            TurnView(pawn, goal.Pitch, goal.Yaw);
            return true;
        }

        var dYaw = AngleDiff(goal.Yaw, currentYaw);
        var dPitch = AngleDiff(goal.Pitch, currentPitch);
        if (Math.Abs(dYaw) < 0.05f && Math.Abs(dPitch) < 0.05f) return false;
        var maxStep = AimMaxStep * smoothing / AimSmoothing;
        if (mode != AimTarget.Crosshair) maxStep *= 1.6f;
        var stepYaw = Math.Clamp(dYaw * smoothing, -maxStep, maxStep);
        var stepPitch = Math.Clamp(dPitch * smoothing, -maxStep, maxStep);
        if (Math.Abs(dYaw) < 0.4f) stepYaw = dYaw;
        if (Math.Abs(dPitch) < 0.4f) stepPitch = dPitch;
        TurnView(pawn, currentPitch + stepPitch, currentYaw + stepYaw);
        return false;
    }

    /// <summary>
    /// Turns the player's real view. Writing v_angle alone does nothing: the next command from the client
    /// overwrites it. Teleport snaps the eye angles on the client too.
    /// </summary>
    private static void TurnView(CCSPlayerPawn pawn, float pitch, float yaw)
    {
        pawn.Teleport(null, new QAngle(Math.Clamp(pitch, -89f, 89f), yaw, 0), null);
    }

    /// <summary>Degrees between where they look and a line to us. Smaller means they are aiming at us.</summary>
    private static float LookingAtMe(CCSPlayerPawn enemy, CCSPlayerPawn me)
    {
        if (AnglesTo(enemy, me, null) is not { } angles) return 180f;
        if (!TryReadView(enemy, out var pitch, out var yaw)) return 180f;
        return Math.Max(Math.Abs(AngleDiff(angles.Yaw, yaw)), Math.Abs(AngleDiff(angles.Pitch, pitch)));
    }

    private static float EyeHeight(CCSPlayerPawn pawn)
    {
        var z = pawn.ViewOffset.Z;
        return z > 1 ? z : 64f;
    }

    private static float AngleDiff(float a, float b)
    {
        var d = (a - b) % 360f;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }

    private void ResetSpeed()
    {
        foreach (var player in Humans().Where(p => p.PawnIsAlive))
        {
            if (IsHacker(player) && Has(player, Power.Speed)) continue;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.VelocityModifier <= 1f) continue;
            pawn.VelocityModifier = 1f;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Wallhack: a glowing copy of each player model, only sent to the players allowed to see it
    // ---------------------------------------------------------------------------------------------

    private bool GlowsNeeded => _hackMode == HackMode.OneRandom || Hackers().Any(h => Has(h, Power.Wallhack));

    private void RebuildGlows()
    {
        RemoveAllGlows();
        if (!GlowsNeeded) return;
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive)) CreateGlow(player);
    }

    private void CreateGlow(CCSPlayerController player)
    {
        RemoveGlow(player.Slot);
        if (!GlowsNeeded) return;

        try
        {
            var pawn = player.PlayerPawn.Value;
            var model = pawn?.CBodyComponent?.SceneNode?.GetSkeletonInstance()?.ModelState.ModelName;
            if (pawn == null || !pawn.IsValid || string.IsNullOrEmpty(model)) return;

            // The relay follows the player invisibly; the glow follows the relay (that's what makes it animate).
            var relay = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            var glow = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (relay == null || glow == null) return;

            relay.SetModel(model);
            relay.Spawnflags = 256; // no collisions
            relay.RenderMode = RenderMode_t.kRenderNone;
            relay.DispatchSpawn();

            var color = IsHacker(player) && _hackMode == HackMode.OneRandom ? HackerGlow
                : player.Team == CsTeam.Terrorist ? TerroristGlow : CounterTerroristGlow;
            glow.SetModel(model);
            glow.Spawnflags = 256;
            glow.DispatchSpawn();
            // Engine glow (spectator-style highlight). It is drawn through walls for whoever receives the entity.
            // A render-mode override here hides that highlight on current CS2, so the model stays the normal one.
            glow.Glow.GlowColorOverride = color;
            glow.Glow.GlowRange = 10000;
            glow.Glow.GlowRangeMin = 0;
            glow.Glow.GlowTeam = -1;
            glow.Glow.GlowType = 3;

            relay.AcceptInput("FollowEntity", pawn, relay, "!activator");
            glow.AcceptInput("FollowEntity", relay, glow, "!activator");
            _glows[player.Slot] = (player, relay, glow);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not create a hacker-mode glow");
        }
    }

    private void RemoveGlow(int slot)
    {
        if (!_glows.Remove(slot, out var g)) return;
        if (g.Glow.IsValid) g.Glow.Remove();
        if (g.Relay.IsValid) g.Relay.Remove();
    }

    private void RemoveAllGlows()
    {
        foreach (var slot in _glows.Keys.ToList()) RemoveGlow(slot);
    }

    /// <summary>
    /// Who sees whose glow: hackers with chams see every other player (enemies and teammates, different
    /// colors). In "one random" mode everyone else sees the hacker. Nobody sees their own.
    /// </summary>
    private bool CanSeeGlow(CCSPlayerController viewer, CCSPlayerController owner)
    {
        if (!viewer.IsValid || !owner.IsValid || viewer.Slot == owner.Slot) return false;
        if (_hackMode == HackMode.OneRandom && IsHacker(owner)) return true;
        if (!IsHacker(viewer) || !Has(viewer, Power.Wallhack)) return false;
        return owner.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist;
    }

    private void OnHackCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_glows.Count == 0 && _cheatMenus.Count == 0 && _esp.Count == 0 && _smokes.Count == 0) return;
        foreach (var (info, viewer) in infoList)
        {
            if (_cheatMenus.Count > 0) HideOtherMenus(info, viewer);
            if (_smokes.Count > 0) HideSmokes(info, viewer);
            foreach (var (_, (owner, _, glow)) in _glows)
            {
                if (!glow.IsValid) continue;
                if (viewer == null || !CanSeeGlow(viewer, owner)) info.TransmitEntities.Remove(glow);
            }
            if (_esp.Count > 0) FilterEsp(info, viewer);
        }
    }
}
