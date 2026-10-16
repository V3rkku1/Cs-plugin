using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Triggerbot and silent aim:
/// <list type="bullet">
/// <item>Triggerbot: when an enemy's head or body is on the crosshair (and visible), it shoots for real.
/// CS2 rebuilds the buttons from the client's own input, so for the player on this PC the launcher clicks
/// fire (lst_fire_relay); no damage is made up.</item>
/// <item>Silent aim: the view never moves. Each real shot that misses also hits the target nearest the
/// crosshair, inside the aim area (a point_hurt with the hacker as attacker; if that leaves the health
/// unchanged, the health is lowered directly).</item>
/// </list>
/// </summary>
public partial class LanSkinTester
{
    /// <summary>Earliest tick each slot's next triggerbot shot may go (the gun's fire rate).</summary>
    private readonly Dictionary<int, int> _nextTriggerShot = new();

    /// <summary>point_hurt didn't change health (or failed): silent-aim hits lower the health directly.</summary>
    private bool _hurtByHealth;

    /// <summary>The launcher on this PC listens here and clicks fire in CS2 (lst_fire_relay).</summary>
    private int _fireRelayPort;
    private System.Net.Sockets.UdpClient? _fireRelay;

    [ConsoleCommand("lst_fire_relay", "Port the launcher listens on to press fire for this PC's player (0 = off)")]
    [CommandHelper(minArgs: 1, usage: "port", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnFireRelayCommand(CCSPlayerController? caller, CommandInfo cmd) =>
        _fireRelayPort = int.TryParse(cmd.GetArg(1), out var port) && port is > 0 and < 65536 ? port : 0;

    /// <summary>Hit spheres below the eyes: (drop, radius, head). Roughly a standing CS2 player model.</summary>
    private static readonly (float Drop, float Radius, bool Head)[] BodyParts =
    [
        (2f, 6f, true), (18f, 9f, false), (30f, 10f, false), (44f, 9f, false), (58f, 7f, false),
    ];

    private void ForgetShots(int slot) => _nextTriggerShot.Remove(slot);

    /// <summary>The player plays on this PC (joined the server over 127.0.0.1), where the launcher can press fire.</summary>
    private static bool PlaysOnThisPc(CCSPlayerController player)
    {
        var ip = player.IpAddress ?? "";
        return ip.StartsWith("127.") || ip.StartsWith("loopback", StringComparison.OrdinalIgnoreCase) || ip.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every tick for a hacker with the triggerbot: once an enemy is on the crosshair (after a short
    /// reaction delay) it shoots for real. On this PC the launcher clicks fire, one click per shot at the
    /// gun's fire rate. For players on other PCs the server holds fire for them (returns true); CS2 may
    /// ignore that, there is no launcher there to click.
    /// </summary>
    private bool TriggerTick(CCSPlayerController hacker, CCSPlayerPawn pawn)
    {
        var hit = CrosshairTarget(hacker, pawn);
        if (hit is not { } target)
        {
            _triggers.Remove(hacker.Slot);
            return false;
        }

        // A short human-like delay after an enemy comes onto the crosshair (shorter at higher aggression).
        var victimSlot = (int)target.Pawn.Index;
        if (!_triggers.TryGetValue(hacker.Slot, out var wait) || wait.TargetSlot != victimSlot)
        {
            var aggression = AggressionOf(hacker);
            var min = 95.0 - 8.0 * aggression;
            var max = min + 35.0 + (10 - aggression) * 9.0;
            wait = new TriggerLock { TargetSlot = victimSlot, SeenAt = DateTime.UtcNow, DelayMs = min + Random.Shared.NextDouble() * (max - min) };
            _triggers[hacker.Slot] = wait;
        }
        if ((DateTime.UtcNow - wait.SeenAt).TotalMilliseconds < wait.DelayMs) return false;
        return !FireOnThisPc(hacker, pawn);
    }

    /// <summary>
    /// For the player on this PC: one real click per shot, at the gun's fire rate. Returns false when the
    /// launcher can't click for this player (another PC), so the server holds fire for them instead.
    /// </summary>
    private bool FireOnThisPc(CCSPlayerController hacker, CCSPlayerPawn pawn)
    {
        if (_fireRelayPort <= 0 || !PlaysOnThisPc(hacker)) return false;
        if (!HasLoadedGun(pawn, out var weapon) || weapon == null) return true;
        if (_nextTriggerShot.TryGetValue(hacker.Slot, out var next) && Server.TickCount < next) return true;
        _nextTriggerShot[hacker.Slot] = Server.TickCount + FireDelayTicks(weapon);
        PressFireOnThisPc();
        return true;
    }

    /// <summary>Ticks between two shots of this gun (at least one).</summary>
    private static int FireDelayTicks(CBasePlayerWeapon weapon)
    {
        var cycle = 0.1f;
        try
        {
            var data = weapon.GetVData<CCSWeaponBaseVData>();
            if (data != null && data.CycleTime.Values.Length > 0 && data.CycleTime.Values[0] > 0) cycle = data.CycleTime.Values[0];
        }
        catch (Exception)
        {
            // keep 0.1 s
        }
        // A little slower than the gun's best, so pistols are clicked again only after they can fire.
        return Math.Max(2, (int)Math.Ceiling(cycle * 1.15f / Server.TickInterval));
    }

    private void PressFireOnThisPc()
    {
        try
        {
            _fireRelay ??= new System.Net.Sockets.UdpClient();
            var message = System.Text.Encoding.ASCII.GetBytes("LST fire");
            _fireRelay.Send(message, message.Length, "127.0.0.1", _fireRelayPort);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Fire relay");
        }
    }

    /// <summary>Silent aim on a real shot: when it would miss, the nearest enemy to the crosshair takes the hit.</summary>
    private void SilentShot(CCSPlayerController hacker)
    {
        var pawn = hacker.PlayerPawn.Value;
        if (pawn is not { IsValid: true } || !AimPressed(hacker)) return;
        if (CrosshairTarget(hacker, pawn) != null) return; // the real bullet hits on its own
        if (!TryReadView(pawn, out var pitch, out var yaw)) return;

        var (fov, _) = AimSettingsOf(hacker);
        var walls = Has(hacker, Power.AimWalls);
        var bone = new AimLock { OffsetZ = BoneDrop(AimBoneOf(hacker)) };
        CCSPlayerPawn? best = null;
        var bestAngle = fov;
        foreach (var enemy in Utilities.GetPlayers())
        {
            if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn is not { IsValid: true } || AnglesTo(pawn, enemyPawn, bone) is not { } a) continue;
            var angle = Math.Max(Math.Abs(AngleDiff(a.Yaw, yaw)), Math.Abs(AngleDiff(a.Pitch, pitch)));
            if (angle >= bestAngle || (!walls && !Visible(enemyPawn, pawn))) continue;
            bestAngle = angle;
            best = enemyPawn;
        }
        // The real shot already used the ammo and the fire delay.
        if (best != null) Hurt(pawn, best, ShotDamage(pawn, AimBoneOf(hacker) == AimBone.Head), hacker, AimBoneOf(hacker) == AimBone.Head);
    }

    /// <summary>
    /// The nearest enemy whose head or body the crosshair line passes through, and whether it is the head.
    /// Plain geometry along the view (no hitbox trace needed); walls still block unless aim-through-walls is on.
    /// </summary>
    private (CCSPlayerPawn Pawn, bool Head)? CrosshairTarget(CCSPlayerController hacker, CCSPlayerPawn pawn)
    {
        if (!TryReadView(pawn, out var pitch, out var yaw) || pawn.AbsOrigin is not { } origin) return null;
        var p = pitch * Math.PI / 180;
        var y = yaw * Math.PI / 180;
        var (dx, dy, dz) = (Math.Cos(p) * Math.Cos(y), Math.Cos(p) * Math.Sin(y), -Math.Sin(p));
        var (ex, ey, ez) = ((double)origin.X, (double)origin.Y, (double)(origin.Z + EyeHeight(pawn)));
        var walls = Has(hacker, Power.AimWalls);

        (CCSPlayerPawn Pawn, bool Head)? best = null;
        var bestDistance = double.MaxValue;
        foreach (var enemy in Utilities.GetPlayers())
        {
            if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn is not { IsValid: true } || enemyPawn.AbsOrigin is not { } o) continue;
            var eyes = o.Z + EyeHeight(enemyPawn);
            foreach (var (drop, radius, head) in BodyParts)
            {
                // Distance from the part's centre to the crosshair line, in front of the eyes only.
                var (px, py, pz) = (o.X - ex, o.Y - ey, eyes - drop - ez);
                var along = px * dx + py * dy + pz * dz;
                if (along <= 0 || along >= bestDistance) continue;
                var (cx, cy, cz) = (px - along * dx, py - along * dy, pz - along * dz);
                if (cx * cx + cy * cy + cz * cz > radius * radius) continue;
                if (!walls && !Visible(enemyPawn, pawn)) break;
                bestDistance = along;
                best = (enemyPawn, head);
                break; // highest part hit on this enemy (head first)
            }
        }
        return best;
    }

    /// <summary>Line of sight; if the trace itself is broken (after a CS2 update), assume visible.</summary>
    private bool Visible(CCSPlayerPawn enemy, CCSPlayerPawn viewer) => IsVisibleTo(enemy, viewer) || _traceBroken;

    /// <summary>A gun (not a knife or grenade) with a bullet in the magazine.</summary>
    private static bool HasLoadedGun(CCSPlayerPawn pawn, out CBasePlayerWeapon? weapon)
    {
        weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        return weapon is { IsValid: true } && (weapon.VData?.MaxClip1 ?? 0) > 0 && weapon.Clip1 > 0;
    }

    private static int ShotDamage(CCSPlayerPawn pawn, bool head)
    {
        var data = pawn.WeaponServices?.ActiveWeapon.Value?.GetVData<CCSWeaponBaseVData>();
        var damage = data?.Damage > 0 ? data.Damage : 30;
        return (int)Math.Round(head ? damage * Math.Max(1f, data?.HeadshotMultiplier ?? 4f) : damage);
    }

    /// <summary>Damages the victim as a bullet from the attacker; checks next frame that it landed.</summary>
    private void Hurt(CCSPlayerPawn attacker, CCSPlayerPawn victim, int damage, CCSPlayerController hacker, bool head)
    {
        if (damage <= 0 || !victim.IsValid) return;
        if (_headshotOnly && !head) return; // the headshot-only rule applies to these hits too
        var before = victim.Health;

        if (!_hurtByHealth)
        {
            try
            {
                var hurt = Utilities.CreateEntityByName<CPointHurt>("point_hurt");
                if (hurt != null && victim.Entity != null)
                {
                    var target = $"lst_hit_{victim.Index}";
                    var oldName = victim.Entity.Name ?? "";
                    victim.Entity.Name = target;
                    hurt.StrTarget = target;
                    hurt.Damage = damage;
                    hurt.BitsDamageType = DamageTypes_t.DMG_BULLET;
                    hurt.DispatchSpawn();
                    hurt.AcceptInput("Hurt", attacker, attacker);
                    victim.Entity.Name = oldName;
                    hurt.Remove();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "point_hurt failed, hacker hits lower health directly from now on");
                _hurtByHealth = true;
            }
        }

        // point_hurt may hurt on the spot or on its next think: look one frame later.
        Server.NextFrame(() =>
        {
            if (!victim.IsValid || victim.LifeState != (byte)LifeState_t.LIFE_ALIVE) return;
            if (victim.Health != before) return; // it landed
            if (!_hurtByHealth)
            {
                _hurtByHealth = true;
                Logger.LogInformation("point_hurt did no damage, hacker hits lower health directly from now on");
            }
            LowerHealth(victim, damage);
        });
    }

    /// <summary>Fallback hit: takes the health (armour ignored); a lethal hit kills the victim.</summary>
    private static void LowerHealth(CCSPlayerPawn victim, int damage)
    {
        var health = victim.Health - damage;
        if (health <= 0)
        {
            victim.CommitSuicide(false, true);
            return;
        }
        victim.Health = health;
        Utilities.SetStateChanged(victim, "CBaseEntity", "m_iHealth");
    }
}
