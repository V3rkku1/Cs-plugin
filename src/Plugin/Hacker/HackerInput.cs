using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Writes the hacker's buttons on the server pawn before the player is simulated, plus the bhop and
/// smoke helpers. View turning lives in TurnView (HackerMode).
/// </summary>
public partial class LanSkinTester
{
    /// <summary>Jump was cleared in the air so the landing tick counts as a new press.</summary>
    private readonly HashSet<int> _bhopReleased = new();

    /// <summary>Smoke entities (handles) that hackers with no-flash don't receive.</summary>
    private readonly List<uint> _smokes = new();

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

            if (_attackHeld.Contains(hacker.Slot)) ForceButton(pawn, PlayerButtons.Attack, true);
            if (Has(hacker, Power.Bhop)) ApplyBhopButton(hacker, pawn);
        }
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
        // No-flash hides smokes only in hacker mode. Outside it the list would grow all map long and keep
        // the per-client transmit filter running every tick.
        if (_hackMode == HackMode.Off) return;
        var name = entity.DesignerName;
        if (name != null && name.Contains("smoke", StringComparison.OrdinalIgnoreCase) && HandleOf(entity) is var handle and not NoEntity)
            _smokes.Add(handle);
    }

    /// <summary>Indices of the smokes still in the world (the gone ones are dropped from the list).</summary>
    private List<uint> SmokeIndices()
    {
        var indices = new List<uint>();
        for (var i = _smokes.Count - 1; i >= 0; i--)
        {
            if (Resolve<CEntityInstance>(_smokes[i]) is { } smoke) indices.Add(smoke.Index);
            else _smokes.RemoveAt(i);
        }
        return indices;
    }

}
