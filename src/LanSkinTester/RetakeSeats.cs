using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;

namespace LanSkinTester;

/// <summary>
/// lst_retake_seats SEATS BOTS [LIVE]: retakes only has SEATS places (plugin retakes: its MaxPlayers,
/// Valve retakes: 3 + 4). Bots fill the places nobody uses, and give one up for every person who
/// connects, so a second, third... friend always gets a team instead of waiting in the queue forever.
/// B3none's plugin counts every bot that spawns as an active player and never lets one go for a
/// queued person, so without this the bots held every place.
///
/// Only bot_quota is changed (the bot manager kicks or adds the bot itself, exactly like a normal
/// quota change). LIVE = 1 (Valve retakes): change it at once. LIVE = 0 (plugin retakes): change it in
/// warmup or after a round ends, never during a live round; a new person waits in the retakes queue
/// until the next round anyway. SEATS 0 = off.
/// </summary>
public partial class LanSkinTester
{
    private int _seats, _seatBots;
    private bool _seatsLive, _seatsListening, _roundRunning, _seatsPending;

    [ConsoleCommand("lst_retake_seats", "Retake seats: lst_retake_seats SEATS BOTS [LIVE] (0 = off)")]
    [CommandHelper(minArgs: 1, usage: "SEATS [BOTS] [LIVE]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeSeatsCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (!_seatsListening)
        {
            _seatsListening = true;
            RegisterListener<Listeners.OnMapStart>(_ =>
            {
                _roundRunning = false;
                // The retakes plugin runs its own cfg (bot_kick, bot_quota 0) when the map starts.
                AddTimer(4f, () => UpdateSeats(force: true), TimerFlags.STOP_ON_MAPCHANGE);
            });
        }
        _seats = int.TryParse(cmd.GetArg(1), out var seats) ? Math.Clamp(seats, 0, 64) : 0;
        _seatBots = cmd.ArgCount > 2 && int.TryParse(cmd.GetArg(2), out var bots) ? Math.Clamp(bots, 0, 31) : 0;
        _seatsLive = cmd.ArgCount > 3 && cmd.GetArg(3) == "1";
        UpdateSeats(force: true);
    }

    [GameEventHandler]
    public HookResult OnSeatsConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        // A person finished loading: a bot leaves before the retakes plugin seats them (it does that
        // 1 second after this event).
        if (_seats > 0 && @event.Userid is { IsValid: true } player && !player.IsHLTV && !IsBotPlayer(player))
            AddTimer(0.1f, () => UpdateSeats(), TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnSeatsDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        // Counted on the next frame, when the player is gone from the list.
        if (_seats > 0 && @event.Userid is { IsValid: true } player && !player.IsHLTV && !IsBotPlayer(player))
            AddTimer(0.5f, () => UpdateSeats(), TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnSeatsRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundRunning = true;
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnSeatsRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _roundRunning = false;
        // The retakes plugin fills free seats from its queue on the next round's pre-start.
        if (_seats > 0 && _seatsPending) UpdateSeats();
        return HookResult.Continue;
    }

    private void UpdateSeats(bool force = false)
    {
        if (_seats <= 0) return;
        try
        {
            if (!force && !_seatsLive && _roundRunning && !InWarmup())
            {
                _seatsPending = true;
                return;
            }
            _seatsPending = false;
            var humans = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsHLTV && !IsBotPlayer(p)
                                                          && p.Connected == PlayerConnectedState.Connected);
            var quota = Math.Clamp(Math.Min(_seatBots, _seats - humans), 0, 31);
            var current = ConVar.Find("bot_quota")?.GetPrimitiveValue<int>();
            if (current == quota) return;
            Logger.LogInformation("Retake seats: {Humans} people, {Seats} seats, bot_quota {Quota}", humans, _seats, quota);
            Server.ExecuteCommand($"bot_quota {quota}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not update the retake bots");
        }
    }

    private static bool InWarmup()
    {
        try
        {
            return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules?.WarmupPeriod ?? false;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
