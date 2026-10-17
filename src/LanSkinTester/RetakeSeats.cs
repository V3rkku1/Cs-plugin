using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

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
            AddCommandListener("jointeam", OnRetakeJoinTeam);
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

    public enum RoomMove { None, MoveBot, KickBot }

    /// <summary>
    /// Valve retakes: a person picking a full side. A bot on it moves to the other side when that has room,
    /// else it is kicked (bot_quota adds a new one where there is room). Without a bot there: the game says full.
    /// </summary>
    internal static RoomMove MakeRoom(int onWanted, int wantedSeats, int onOther, int otherSeats, bool botOnWanted) =>
        onWanted < wantedSeats || !botOnWanted ? RoomMove.None : onOther < otherSeats ? RoomMove.MoveBot : RoomMove.KickBot;

    private HookResult OnRetakeJoinTeam(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!_seatsLive || _seats <= 0 || player is not { IsValid: true } || IsBotPlayer(player)) return HookResult.Continue;
        if (!int.TryParse(cmd.GetArg(1), out var wanted) || wanted is not (2 or 3)) return HookResult.Continue;
        try
        {
            var team = (CsTeam)wanted;
            var other = team == CsTeam.Terrorist ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
            int Seats(CsTeam t) => ConVar.Find(t == CsTeam.Terrorist ? "mp_retake_t_count" : "mp_retake_ct_count")?.GetPrimitiveValue<int>() ?? (t == CsTeam.Terrorist ? 3 : 4);
            var players = Utilities.GetPlayers().Where(p => p.IsValid && p != player && !p.IsHLTV).ToList();
            var bot = players.LastOrDefault(p => p.Team == team && IsBotPlayer(p));
            switch (MakeRoom(players.Count(p => p.Team == team), Seats(team), players.Count(p => p.Team == other), Seats(other), bot != null))
            {
                case RoomMove.MoveBot:
                    bot!.ChangeTeam(other);
                    break;
                case RoomMove.KickBot:
                    Server.ExecuteCommand($"kickid {bot!.UserId}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not make room on the retakes team");
        }
        return HookResult.Continue; // the game seats the person as usual, now there is room
    }

    /// <summary>B3none's retakes plugin runs this match (plugin retakes; Valve retakes changes bots live).</summary>
    private bool RetakesPluginRunning => _seats > 0 && !_seatsLive;

    /// <summary>Bots that fit next to these people: the seats nobody uses, at most the bots wanted.</summary>
    internal static int SeatQuota(int seats, int botsWanted, int humans) => Math.Clamp(Math.Min(botsWanted, seats - humans), 0, 31);

    /// <summary>Plugin retakes changes bots only in warmup or between rounds.</summary>
    internal static bool SeatChangeWaits(bool live, bool roundRunning, bool warmup) => !live && roundRunning && !warmup;

    private void UpdateSeats(bool force = false)
    {
        if (_seats <= 0) return;
        try
        {
            if (!force && SeatChangeWaits(_seatsLive, _roundRunning, IsWarmup()))
            {
                _seatsPending = true;
                return;
            }
            _seatsPending = false;
            var humans = Utilities.GetPlayers().Count(p => p.IsValid && !p.IsHLTV && !IsBotPlayer(p)
                                                          && p.Connected == PlayerConnectedState.Connected);
            var quota = SeatQuota(_seats, _seatBots, humans);
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
}
