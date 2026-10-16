using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Hits the server lands itself, for when the player's own bullet can't be steered:
/// <list type="bullet">
/// <item>Triggerbot: the server can't always press fire for the client (CS2 rebuilds the buttons from the
/// client's input). When a forced shot doesn't come within a few ticks, it shoots for them instead.</item>
/// <item>Silent aim: the view never moves. Each real shot that misses also hits the target nearest the
/// crosshair, inside the aim area.</item>
/// </list>
/// The hit goes through a point_hurt with the hacker as attacker, so it counts as their kill.
/// </summary>
public partial class LanSkinTester
{
    /// <summary>Server tick of each player's last real shot (weapon_fire).</summary>
    private readonly Dictionary<int, int> _lastShotTick = new();

    /// <summary>Tick the triggerbot started pressing fire for a slot, while no real shot has come yet.</summary>
    private readonly Dictionary<int, int> _forcedSince = new();

    /// <summary>Set once a forced press didn't make the gun fire: from then on the triggerbot shoots server-side.</summary>
    private bool _serverTrigger;

    private bool _serverShotBroken;

    /// <summary>Ticks to wait for the forced press to fire before shooting server-side.</summary>
    private const int ForcedShotGraceTicks = 4;

    private void NoteRealShot(CCSPlayerController player)
    {
        _lastShotTick[player.Slot] = Server.TickCount;
        _forcedSince.Remove(player.Slot);
    }

    /// <summary>
    /// Called every tick with whether the triggerbot wants to shoot. Waits a few ticks for the forced
    /// press to fire the gun; when it doesn't, shoots server-side (and keeps doing so).
    /// </summary>
    private void TriggerFallback(CCSPlayerController hacker, CCSPlayerPawn pawn, bool wantsShot)
    {
        if (!wantsShot)
        {
            _forcedSince.Remove(hacker.Slot);
            return;
        }
        if (!_serverTrigger)
        {
            if (!GunReady(pawn, out _)) return;
            if (!_forcedSince.TryGetValue(hacker.Slot, out var since))
            {
                _forcedSince[hacker.Slot] = Server.TickCount;
                return;
            }
            if (Server.TickCount - since < ForcedShotGraceTicks) return;
            // Ready gun, fire held for a while, no shot: CS2 ignored the forced button.
            _serverTrigger = true;
            Logger.LogInformation("Triggerbot: the forced fire button didn't shoot, shooting server-side from now on");
        }
        if (CrosshairTarget(hacker, pawn) is { } victim) ServerShot(hacker, pawn, victim, head: AimBoneOf(hacker) == AimBone.Head);
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
            if (angle >= bestAngle || (!walls && !IsVisibleTo(enemyPawn, pawn))) continue;
            bestAngle = angle;
            best = enemyPawn;
        }
        // The real shot already used the ammo and the fire delay.
        if (best != null) Hurt(pawn, best, ShotDamage(pawn, AimBoneOf(hacker) == AimBone.Head), hacker);
    }

    /// <summary>The enemy under the crosshair (shot trace, then the angle test), or null.</summary>
    private CCSPlayerPawn? CrosshairTarget(CCSPlayerController hacker, CCSPlayerPawn pawn)
    {
        if (!TryReadView(pawn, out var pitch, out var yaw)) return null;
        var walls = Has(hacker, Power.AimWalls);
        foreach (var enemy in Utilities.GetPlayers())
        {
            if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn is not { IsValid: true }) continue;
            if (!walls && !IsVisibleTo(enemyPawn, pawn)) continue;
            foreach (var drop in new[] { 2f, 16f, 32f })
            {
                if (AnglesTo(pawn, enemyPawn, new AimLock { OffsetZ = drop }) is not { } angles) continue;
                if (Math.Max(Math.Abs(AngleDiff(angles.Yaw, yaw)), Math.Abs(AngleDiff(angles.Pitch, pitch))) < 5.5f) return enemyPawn;
            }
        }
        return null;
    }

    /// <summary>A gun (not a knife or grenade) with a bullet in it and its fire delay over.</summary>
    private static bool GunReady(CCSPlayerPawn pawn, out CBasePlayerWeapon? weapon)
    {
        weapon = pawn.WeaponServices?.ActiveWeapon.Value;
        if (weapon is not { IsValid: true } || (weapon.VData?.MaxClip1 ?? 0) <= 0) return false;
        return weapon.Clip1 > 0 && weapon.NextPrimaryAttackTick <= Server.TickCount;
    }

    private static int ShotDamage(CCSPlayerPawn pawn, bool head)
    {
        var data = pawn.WeaponServices?.ActiveWeapon.Value?.GetVData<CCSWeaponBaseVData>();
        var damage = data?.Damage > 0 ? data.Damage : 30;
        return (int)Math.Round(head ? damage * Math.Max(1f, data?.HeadshotMultiplier ?? 4f) : damage);
    }

    /// <summary>One shot from the hacker's gun at the victim: uses a bullet and waits the gun's fire delay.</summary>
    private void ServerShot(CCSPlayerController hacker, CCSPlayerPawn pawn, CCSPlayerPawn victim, bool head)
    {
        if (!GunReady(pawn, out var weapon) || weapon == null) return;
        var data = weapon.GetVData<CCSWeaponBaseVData>();
        var cycle = 0.1f;
        try
        {
            if (data != null && data.CycleTime.Values.Length > 0 && data.CycleTime.Values[0] > 0) cycle = data.CycleTime.Values[0];
        }
        catch (Exception)
        {
            // keep 0.1 s
        }
        weapon.NextPrimaryAttackTick = Server.TickCount + Math.Max(1, (int)Math.Ceiling(cycle / Server.TickInterval));
        Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_nNextPrimaryAttackTick");
        if (!Has(hacker, Power.Ammo))
        {
            weapon.Clip1--;
            Utilities.SetStateChanged(weapon, "CBasePlayerWeapon", "m_iClip1");
        }
        Hurt(pawn, victim, ShotDamage(pawn, head), hacker);
    }

    /// <summary>Damages the victim as a bullet from the attacker (kill feed and score count it as theirs).</summary>
    private void Hurt(CCSPlayerPawn attacker, CCSPlayerPawn victim, int damage, CCSPlayerController hacker)
    {
        if (_serverShotBroken || damage <= 0 || victim.Entity == null) return;
        try
        {
            var hurt = Utilities.CreateEntityByName<CPointHurt>("point_hurt");
            if (hurt == null) return;
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
        catch (Exception ex)
        {
            _serverShotBroken = true;
            Logger.LogWarning(ex, "Server-side hacker shot failed for slot {Slot}", hacker.Slot);
        }
    }
}
