using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;

namespace LanSkinTester;

/// <summary>
/// Small server helpers from Live Server › Server Plugins:
///   lst_cleanup 1   guns lying on the ground for 15 seconds disappear (less clutter and lag in deathmatch)
///   lst_nofall 1    no fall damage
/// </summary>
public partial class LanSkinTester
{
    private bool _cleanup, _noFall, _qolListening;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _cleanupTimer;
    private readonly Dictionary<uint, DateTime> _groundSince = new();

    [ConsoleCommand("lst_cleanup", "1 = dropped guns disappear after 15 seconds")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnCleanupCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _cleanup = cmd.GetArg(1) == "1";
        _groundSince.Clear();
        _cleanupTimer?.Kill();
        _cleanupTimer = _cleanup ? AddTimer(5f, CleanUpGuns, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE) : null;
    }

    [ConsoleCommand("lst_nofall", "1 = no fall damage")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnNoFallCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _noFall = cmd.GetArg(1) == "1";
        if (_qolListening) return;
        _qolListening = true;
        RegisterListener<Listeners.OnEntityTakeDamagePre>((victim, damage) =>
        {
            if (!_noFall || victim.DesignerName != "player") return HookResult.Continue;
            try
            {
                if ((damage.BitsDamageType & DamageTypes_t.DMG_FALL) == 0) return HookResult.Continue;
                damage.Damage = 0;
                return HookResult.Handled;
            }
            catch (Exception)
            {
                return HookResult.Continue;
            }
        });
    }

    /// <summary>Removes guns nobody has carried for 15 seconds (the bomb stays).</summary>
    private void CleanUpGuns()
    {
        var now = DateTime.UtcNow;
        var seen = new HashSet<uint>();
        foreach (var weapon in Utilities.FindAllEntitiesByDesignerName<CBasePlayerWeapon>("weapon_"))
        {
            if (!weapon.IsValid || weapon.DesignerName == "weapon_c4" || weapon.OwnerEntity.IsValid) continue;
            seen.Add(weapon.Index);
            if (!_groundSince.TryGetValue(weapon.Index, out var since)) _groundSince[weapon.Index] = now;
            else if (now - since > TimeSpan.FromSeconds(15)) weapon.AddEntityIOEvent("Kill", null, null, "", 0.01f);
        }
        foreach (var gone in _groundSince.Keys.Where(k => !seen.Contains(k)).ToList()) _groundSince.Remove(gone);
    }
}
