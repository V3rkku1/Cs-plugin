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
///   lst_hack_powers wallhack,aimbot,onetap,ammo,speed   (or "all")
/// </summary>
public partial class LanSkinTester
{
    private enum HackMode { Off, Everyone, OneRandom }

    [Flags]
    private enum Power { None = 0, Wallhack = 1, Aimbot = 2, OneTap = 4, Ammo = 8, Speed = 16, All = 31 }

    // A "legit" aimbot: only enemies you can actually see, a small lock-on area, a human-like reaction
    // delay, smooth movement and a slightly imperfect aim point - not an instant snap through walls.
    private const float AimbotFov = 20f;          // degrees from the crosshair
    private const float AimSmoothing = 0.18f;      // share of the remaining angle closed per tick
    private const float AimMaxStep = 3.5f;         // max degrees turned per tick
    private const double ReactionMinMs = 110, ReactionMaxMs = 220;
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
    private readonly HashSet<int> _hackerSlots = new();
    private readonly Dictionary<int, (CCSPlayerController Owner, CDynamicProp Relay, CDynamicProp Glow)> _glows = new();

    private void LoadHackerMode()
    {
        RegisterListener<Listeners.CheckTransmit>(OnHackCheckTransmit);
        RegisterListener<Listeners.OnTick>(OnHackTick);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _glows.Clear();
            _hackerSlots.Clear();
            _aimLocks.Clear();
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            RemoveGlow(slot);
            _hackerSlots.Remove(slot);
        });
        RegisterEventHandler<EventRoundStart>(OnHackRoundStart);
        RegisterEventHandler<EventPlayerSpawn>(OnHackSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnHackDeath);
        RegisterEventHandler<EventWeaponFire>(OnHackWeaponFire);
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnHackTakeDamage);
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
        if (mode == HackMode.OneRandom) PickRandomHacker();
        ResetSpeed();
        RebuildGlows();
        Announce();
    }

    [ConsoleCommand("lst_hack_powers", "Hacker powers: wallhack,aimbot,onetap,ammo,speed or all")]
    [CommandHelper(minArgs: 1, usage: "wallhack,aimbot,onetap,ammo,speed|all", whoCanExecute: CommandUsage.SERVER_ONLY)]
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
            Reply(player, "Hacker mode is off. (The host turns it on in the launcher's Play page.)");
            return;
        }
        Reply(player, IsHacker(player)
            ? $"You are hacking: {DescribePowers()}{(Has(Power.Aimbot) ? " - hold fire to lock on" : "")}"
            : $"{string.Join(", ", Hackers().Select(h => h.PlayerName))} is hacking. Look for the green glow!");
    }

    // ---------------------------------------------------------------------------------------------
    // Who hacks
    // ---------------------------------------------------------------------------------------------

    private bool Has(Power power) => (_powers & power) != 0;

    private bool IsHacker(CCSPlayerController? player) => player != null && player.IsValid && !player.IsBot && !player.IsHLTV && _hackMode switch
    {
        HackMode.Everyone => true,
        HackMode.OneRandom => _hackerSlots.Contains(player.Slot),
        _ => false,
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

    private string DescribePowers()
    {
        var names = new List<string>();
        if (Has(Power.Wallhack)) names.Add("walls");
        if (Has(Power.Aimbot)) names.Add("aimbot");
        if (Has(Power.OneTap)) names.Add("damage boost");
        if (Has(Power.Ammo)) names.Add("infinite ammo");
        if (Has(Power.Speed)) names.Add("speed");
        return names.Count == 0 ? "nothing (all powers off)" : string.Join(" · ", names);
    }

    private void Announce()
    {
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
                    $"<font color='#aaaaaa'>{DescribePowers()}{(Has(Power.Aimbot) ? " · hold fire to lock on" : "")}</font>", 6);
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
        if (_hackMode == HackMode.Off) return HookResult.Continue;
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
        if (_hackMode == HackMode.Off || player == null || !player.IsValid) return HookResult.Continue;
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
        if (_hackMode == HackMode.Off || victim == null || !IsHacker(attacker) || attacker!.Slot == victim.Slot)
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
        if (!Has(Power.Ammo) || !IsHacker(player)) return HookResult.Continue;

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
        if (_hackMode == HackMode.Off || !Has(Power.OneTap)) return HookResult.Continue;
        try
        {
            if (victim.DesignerName != "player") return HookResult.Continue;

            var attackerPawn = damage.Attacker.Value?.As<CCSPlayerPawn>();
            var attacker = attackerPawn is { IsValid: true } ? attackerPawn.Controller.Value?.As<CCSPlayerController>() : null;
            var target = new CCSPlayerPawn(victim.Handle).Controller.Value?.As<CCSPlayerController>();
            if (IsHacker(attacker) && target != null && IsEnemy(attacker!, target))
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
        if (_hackMode == HackMode.Off || !Has(Power.Aimbot | Power.Speed)) return;

        foreach (var hacker in Hackers())
        {
            if (!hacker.PawnIsAlive) continue;
            var pawn = hacker.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) continue;

            if (Has(Power.Speed) && pawn.VelocityModifier < SpeedMultiplier)
            {
                pawn.VelocityModifier = SpeedMultiplier;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }

            if (Has(Power.Aimbot) && (hacker.Buttons & PlayerButtons.Attack) != 0) Aim(hacker, pawn);
            else _aimLocks.Remove(hacker.Slot);
        }
    }

    /// <summary>
    /// True when the game itself says <paramref name="viewer"/> can see this enemy: CS2's spotting system
    /// (what lights up enemies on the radar) does real line-of-sight checks, so walls and smokes block it.
    /// </summary>
    private static bool IsVisibleTo(CCSPlayerPawn enemyPawn, CCSPlayerController viewer)
    {
        try
        {
            var mask = enemyPawn.EntitySpottedState.SpottedByMask;
            var slot = viewer.Slot;
            return slot >= 0 && slot / 32 < mask.Length && (mask[slot / 32] & (1u << (slot % 32))) != 0;
        }
        catch (Exception)
        {
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

    private void Aim(CCSPlayerController hacker, CCSPlayerPawn pawn)
    {
        var current = pawn.EyeAngles;
        _aimLocks.TryGetValue(hacker.Slot, out var aim);

        // Keep the current target while it stays visible and near the crosshair; otherwise pick the
        // visible enemy closest to the crosshair.
        CCSPlayerPawn? targetPawn = null;
        if (aim != null && Utilities.GetPlayerFromSlot(aim.TargetSlot) is { IsValid: true, PawnIsAlive: true } locked
            && IsEnemy(hacker, locked) && locked.PlayerPawn.Value is { IsValid: true } lockedPawn && IsVisibleTo(lockedPawn, hacker)
            && AnglesTo(pawn, lockedPawn, aim) is { } a
            && Math.Max(Math.Abs(AngleDiff(a.Yaw, current.Y)), Math.Abs(AngleDiff(a.Pitch, current.X))) < AimbotFov * 1.5f)
        {
            targetPawn = lockedPawn;
        }
        else
        {
            aim = null;
            var bestDelta = AimbotFov;
            CCSPlayerController? best = null;
            foreach (var enemy in Utilities.GetPlayers())
            {
                if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
                var enemyPawn = enemy.PlayerPawn.Value;
                if (enemyPawn == null || !enemyPawn.IsValid || !IsVisibleTo(enemyPawn, hacker)) continue;
                if (AnglesTo(pawn, enemyPawn, null) is not { } angles) continue;
                var delta = Math.Max(Math.Abs(AngleDiff(angles.Yaw, current.Y)), Math.Abs(AngleDiff(angles.Pitch, current.X)));
                if (delta >= bestDelta) continue;
                bestDelta = delta;
                best = enemy;
                targetPawn = enemyPawn;
            }
            if (best == null)
            {
                _aimLocks.Remove(hacker.Slot);
                return;
            }
            aim = new AimLock
            {
                TargetSlot = best.Slot,
                AcquiredAt = DateTime.UtcNow,
                ReactionMs = ReactionMinMs + Random.Shared.NextDouble() * (ReactionMaxMs - ReactionMinMs),
                // Mostly the head, sometimes the neck - never pixel-perfect.
                OffsetZ = 1f + (float)Random.Shared.NextDouble() * (Random.Shared.Next(4) == 0 ? 9f : 4f),
                OffsetSide = (float)(Random.Shared.NextDouble() * 5 - 2.5),
            };
            _aimLocks[hacker.Slot] = aim;
        }

        // Human reaction time before the aim starts moving.
        if ((DateTime.UtcNow - aim.AcquiredAt).TotalMilliseconds < aim.ReactionMs) return;
        if (targetPawn == null || AnglesTo(pawn, targetPawn, aim) is not { } goal) return;

        // Move part of the way each tick (smooth, capped), like a hand, not a snap.
        var dYaw = AngleDiff(goal.Yaw, current.Y);
        var dPitch = AngleDiff(goal.Pitch, current.X);
        if (Math.Abs(dYaw) < 0.05f && Math.Abs(dPitch) < 0.05f) return;
        var stepYaw = Math.Clamp(dYaw * AimSmoothing, -AimMaxStep, AimMaxStep);
        var stepPitch = Math.Clamp(dPitch * AimSmoothing, -AimMaxStep, AimMaxStep);
        // Close the last bit directly so it doesn't crawl forever.
        if (Math.Abs(dYaw) < 0.4f) stepYaw = dYaw;
        if (Math.Abs(dPitch) < 0.4f) stepPitch = dPitch;
        pawn.Teleport(null, new QAngle(current.X + stepPitch, current.Y + stepYaw, 0), null);
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
        if (_hackMode != HackMode.Off && Has(Power.Speed)) return;
        foreach (var player in Humans().Where(p => p.PawnIsAlive))
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.VelocityModifier <= 1f) continue;
            pawn.VelocityModifier = 1f;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Wallhack: a glowing copy of each player model, only sent to the players allowed to see it
    // ---------------------------------------------------------------------------------------------

    private bool GlowsNeeded => _hackMode == HackMode.OneRandom || (_hackMode == HackMode.Everyone && Has(Power.Wallhack));

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

            glow.SetModel(model);
            glow.Spawnflags = 256;
            glow.DispatchSpawn();
            glow.Glow.GlowColorOverride = IsHacker(player) && _hackMode == HackMode.OneRandom ? HackerGlow
                : player.Team == CsTeam.Terrorist ? TerroristGlow : CounterTerroristGlow;
            glow.Glow.GlowRange = 5000;
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
    /// Who sees whose glow: hackers (with walls) see their enemies; in "one random" mode everyone else
    /// sees the hacker. Nobody sees their own.
    /// </summary>
    private bool CanSeeGlow(CCSPlayerController viewer, CCSPlayerController owner)
    {
        if (!viewer.IsValid || !owner.IsValid || viewer.Slot == owner.Slot) return false;
        if (_hackMode == HackMode.OneRandom && IsHacker(owner)) return true;
        return Has(Power.Wallhack) && IsHacker(viewer) && IsEnemy(viewer, owner);
    }

    private void OnHackCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_glows.Count == 0) return;
        foreach (var (info, viewer) in infoList)
        {
            foreach (var (_, (owner, _, glow)) in _glows)
            {
                if (!glow.IsValid) continue;
                if (viewer == null || !CanSeeGlow(viewer, owner)) info.TransmitEntities.Remove(glow);
            }
        }
    }
}
