using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Writes the hacker's shot and buttons on the server pawn before the player is simulated, then puts a
/// silent aimer's own view back afterwards. The client never gets a command injected into their game.
/// </summary>
public partial class LanSkinTester
{
    /// <summary>Where this tick's shot should go, written in pre-think. Keyed by player slot.</summary>
    private readonly Dictionary<int, (float Pitch, float Yaw)> _pendingAim = new();

    /// <summary>Jump was cleared in the air so the landing tick counts as a new press.</summary>
    private readonly HashSet<int> _bhopReleased = new();

    private readonly List<CEntityInstance> _smokes = new();

    /// <summary>Hits a player model. World geometry is left out so the result is the body, not the wall.</summary>
    private static readonly TraceOptions PlayerHitTrace = new()
    {
        InteractsAs = Contents.Player,
        InteractsWith = Contents.Player | Contents.Hitbox,
        InteractsExclude = Contents.Debris | Contents.CarriedWeapon | Contents.CarriedObject,
    };

    private static bool TryReadView(CCSPlayerPawn pawn, out float pitch, out float yaw)
    {
        // Current CS2 keeps the real view in v_angle. EyeAngles is the older field and can throw after a schema change.
        try
        {
            var view = pawn.V_angle;
            pitch = view.X;
            yaw = view.Y;
            if (float.IsFinite(pitch) && float.IsFinite(yaw)) return true;
        }
        catch (Exception)
        {
            // fall through
        }
        try
        {
            var view = pawn.EyeAngles;
            pitch = view.X;
            yaw = view.Y;
            return float.IsFinite(pitch) && float.IsFinite(yaw);
        }
        catch (Exception)
        {
            pitch = yaw = 0;
            return false;
        }
    }

    /// <summary>Writes the angles the server aims and shoots with. A ref property, so this is not a Teleport.</summary>
    private static void WriteView(CCSPlayerPawn pawn, float pitch, float yaw)
    {
        void Write(QAngle angles)
        {
            angles.X = pitch;
            angles.Y = yaw;
            angles.Z = 0;
        }
        try { Write(pawn.V_angle); } catch (Exception) { /* schema */ }
        try { Write(pawn.EyeAngles); } catch (Exception) { /* schema */ }
        try { Write(pawn.StashedShootAngles); } catch (Exception) { /* schema */ }
    }

    /// <summary>ORs or clears a button on the pawn and queues it so the command processed this think keeps it.</summary>
    private static void ForceButton(CCSPlayerPawn pawn, PlayerButtons button, bool down)
    {
        var services = pawn.MovementServices;
        if (services == null) return;
        var bit = (ulong)button;
        try
        {
            var states = services.Buttons.ButtonStates;
            if (states.Length > 0)
            {
                if (down) states[0] |= bit;
                else states[0] &= ~bit;
            }
        }
        catch (Exception)
        {
            // The button word moved. The queued mask below is the other place the command reads.
        }
        if (!down) return;
        try
        {
            ref var queued = ref Schema.GetRef<ulong>(services.Handle, "CPlayer_MovementServices", "m_nQueuedButtonDownMask");
            queued |= bit;
        }
        catch (Exception)
        {
            // Older schema: the button word above is enough.
        }
    }

    private void OnHackPreThink()
    {
        if (_hackMode == HackMode.Off && _personal.Count == 0) return;
        foreach (var hacker in Hackers())
        {
            if (!hacker.IsValid || !hacker.PawnIsAlive) continue;
            var pawn = hacker.PlayerPawn.Value;
            if (pawn is not { IsValid: true }) continue;

            if (!CommandAimReady && _pendingAim.TryGetValue(hacker.Slot, out var aim))
            {
                var pitch = aim.Pitch;
                var yaw = aim.Yaw;
                // The bullet is view + punch. Taking the punch off here is the recoil control for that shot.
                if (Has(hacker, Power.NoRecoil))
                {
                    try
                    {
                        var punch = pawn.AimPunchServices?.PredictableBaseAngle;
                        if (punch != null)
                        {
                            pitch -= punch.X * 2f;
                            yaw -= punch.Y * 2f;
                        }
                    }
                    catch (Exception)
                    {
                        // Punch schema changed; RemoveRecoil still clears it every tick.
                    }
                }
                WriteView(pawn, pitch, yaw);
            }

            if (_attackHeld.Contains(hacker.Slot)) ForceButton(pawn, PlayerButtons.Attack, true);
            if (Has(hacker, Power.Bhop)) ApplyBhopButton(hacker, pawn);
        }
    }

    private void OnHackPostThink()
    {
        if (_silentRestore.Count == 0) return;
        foreach (var (slot, angles) in _silentRestore)
        {
            var pawn = Utilities.GetPlayerFromSlot(slot)?.PlayerPawn.Value;
            if (pawn is { IsValid: true }) WriteView(pawn, angles.Pitch, angles.Yaw);
        }
        _silentRestore.Clear();
    }

    /// <summary>
    /// Jump is ignored while it stays held. Clear it in the air and press it again on the landing tick.
    /// </summary>
    private void ApplyBhopButton(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var services = pawn.MovementServices;
        if (services == null) return;
        var onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
        try
        {
            var states = services.Buttons.ButtonStates;
            if (states.Length == 0) return;
            var jump = (ulong)PlayerButtons.Jump;
            var holding = (states[0] & jump) != 0;
            if (onGround)
            {
                if (!holding && !_bhopReleased.Contains(player.Slot)) return;
                states[0] |= jump;
                _bhopReleased.Remove(player.Slot);
                try
                {
                    ref var queued = ref Schema.GetRef<ulong>(services.Handle, "CPlayer_MovementServices", "m_nQueuedButtonDownMask");
                    queued |= jump;
                }
                catch (Exception)
                {
                    // The button word above is the one movement reads.
                }
            }
            else if (holding)
            {
                states[0] &= ~jump;
                _bhopReleased.Add(player.Slot);
            }
        }
        catch (Exception)
        {
            // Velocity boost in the tick still hops.
        }
    }

    private void OnHackSmokeSpawned(CEntityInstance entity)
    {
        var name = entity.DesignerName;
        if (name != null && name.Contains("smoke", StringComparison.OrdinalIgnoreCase))
            _smokes.Add(entity);
    }

    private void HideSmokes(CCheckTransmitInfo info, CCSPlayerController? viewer)
    {
        if (_smokes.Count == 0 || viewer == null || !IsHacker(viewer) || !Has(viewer, Power.NoFlash)) return;
        for (var i = _smokes.Count - 1; i >= 0; i--)
        {
            var smoke = _smokes[i];
            if (!smoke.IsValid)
            {
                _smokes.RemoveAt(i);
                continue;
            }
            info.TransmitEntities.Remove(smoke);
        }
    }

    /// <summary>True when a shot trace from the eyes hits an enemy. Walls block it unless aim-through-walls is on.</summary>
    private bool CrosshairOnEnemy(CCSPlayerController hacker, CCSPlayerPawn pawn, bool walls)
    {
        if (!TryReadView(pawn, out var pitch, out var yaw)) return false;
        return CrosshairOnEnemy(hacker, pawn, walls, pitch, yaw);
    }

    private bool CrosshairOnEnemy(CCSPlayerController hacker, CCSPlayerPawn pawn, bool walls, float pitch, float yaw)
    {
        if (walls) return CrosshairAngleOnEnemy(hacker, pawn, walls: true, pitch, yaw);
        if (pawn.AbsOrigin is not { } origin) return false;
        var eye = new Vector(origin.X, origin.Y, origin.Z + EyeHeight(pawn));
        try
        {
            var hit = Trace.TraceShape(eye, new QAngle(pitch, yaw, 0), pawn, PlayerHitTrace);
            if (hit.DidHit())
            {
                var entity = hit.HitEntity();
                var name = entity is { IsValid: true } ? entity.DesignerName : null;
                if (name != null && name.Contains("player", StringComparison.OrdinalIgnoreCase))
                {
                    var victim = new CCSPlayerPawn(entity!.Handle).Controller.Value?.As<CCSPlayerController>();
                    if (victim != null && victim.IsValid && IsEnemy(hacker, victim)) return true;
                }
            }
        }
        catch (Exception)
        {
            // The trace data changed. The angle test below still fires when the crosshair is on a body.
        }
        return CrosshairAngleOnEnemy(hacker, pawn, walls: false, pitch, yaw);
    }

    private bool CrosshairAngleOnEnemy(CCSPlayerController hacker, CCSPlayerPawn pawn, bool walls, float pitch, float yaw)
    {
        foreach (var enemy in Utilities.GetPlayers())
        {
            if (!enemy.IsValid || !enemy.PawnIsAlive || !IsEnemy(hacker, enemy)) continue;
            var enemyPawn = enemy.PlayerPawn.Value;
            if (enemyPawn == null || !enemyPawn.IsValid) continue;
            if (!walls && !IsVisibleTo(enemyPawn, pawn)) continue;
            // Head, chest and hips, so the shot fires when the crosshair is on the body, not only the eyes.
            foreach (var drop in new[] { 2f, 16f, 32f })
            {
                if (AnglesTo(pawn, enemyPawn, new AimLock { OffsetZ = drop }) is not { } angles) continue;
                var score = Math.Max(Math.Abs(AngleDiff(angles.Yaw, yaw)), Math.Abs(AngleDiff(angles.Pitch, pitch)));
                if (score < 5.5f) return true;
            }
        }
        return false;
    }
}
