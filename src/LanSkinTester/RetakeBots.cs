using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Plugin retakes with auto-plant: the bomb is put down by code, so no bomb_planted event happens and
/// CT bots never learn it is planted: they hold mid instead of going to the site to defuse. When the
/// freeze ends with a planted bomb and no such event this round, fire it ourselves (as the terrorist
/// nearest the bomb, which tells the bots which site).
/// </summary>
public partial class LanSkinTester
{
    private bool _plantAnnounced;

    [GameEventHandler]
    public HookResult OnRetakeBotsRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _plantAnnounced = false;
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRetakeBotsBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        _plantAnnounced = true;
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRetakeBotsFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (RetakesPluginRunning) AddTimer(0.3f, AnnounceAutoPlant, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private void AnnounceAutoPlant()
    {
        if (_plantAnnounced) return;
        try
        {
            var bomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(c => c.IsValid);
            var at = bomb?.AbsOrigin;
            if (bomb == null || at == null) return;
            var planter = Utilities.GetPlayers()
                .Where(p => p.IsValid && p.PawnIsAlive && p.Team == CsTeam.Terrorist && p.PlayerPawn.Value?.AbsOrigin != null)
                .OrderBy(p => Distance(p.PlayerPawn.Value!.AbsOrigin!, at))
                .FirstOrDefault();
            if (planter == null) return;
            var planted = new EventBombPlanted(true) { Userid = planter, Site = bomb.BombSite };
            planted.FireEvent(false);
            _plantAnnounced = true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not tell the bots the bomb is planted");
        }
    }

    private static float Distance(Vector a, Vector b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}
