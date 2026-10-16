using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Retakes extras:
///   lst_retake_seats SEATS BOTS   retakes only has SEATS places. Bots give theirs up the moment a person
///                                 connects, so you and your friends always get a team (0 = off).
///   lst_retake_rifles 0|1         every round: a rifle, a pistol, armor (and a kit on CT) for everyone.
///   lst_instadefuse 0|1           any defuse finishes instantly, in every mode.
///   lst_bomb_timer SECONDS        retakes bomb timer, counted from when the round goes live (0 = off).
///                                 Auto-plant happens during freeze time, so the clock was already running.
/// </summary>
public partial class LanSkinTester
{
    private int _retakeSeats, _retakeBotsWanted;
    private bool _retakeRifles, _instaDefuse, _retakeExtrasListening;
    private int _bombTimer;
    private readonly HashSet<int> _humanSlots = new();

    private void ListenRetakeExtras()
    {
        if (_retakeExtrasListening) return;
        _retakeExtrasListening = true;
        RegisterListener<Listeners.OnClientConnect>((slot, _, ip) =>
        {
            // Bots don't have an address. Free a seat before the person reaches the team menu.
            if (string.IsNullOrEmpty(ip) || ip.Equals("BOT", StringComparison.OrdinalIgnoreCase)) return;
            _humanSlots.Add(slot);
            UpdateRetakeBots();
            // Safety net: if that was a bot after all, give the seat back to bots.
            AddTimer(1f, () =>
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player is { IsValid: true } && IsBotPlayer(player) && _humanSlots.Remove(slot)) UpdateRetakeBots();
            });
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot =>
        {
            if (_humanSlots.Remove(slot)) UpdateRetakeBots();
        });
        RegisterListener<Listeners.OnMapStart>(_ => Server.NextFrame(RecountHumans));
    }

    private void RecountHumans()
    {
        _humanSlots.Clear();
        foreach (var p in Utilities.GetPlayers())
            if (p.IsValid && !p.IsHLTV && !IsBotPlayer(p)) _humanSlots.Add(p.Slot);
        UpdateRetakeBots();
    }

    private void UpdateRetakeBots()
    {
        if (_retakeSeats <= 0) return;
        var quota = Math.Clamp(Math.Min(_retakeBotsWanted, _retakeSeats - _humanSlots.Count), 0, 31);
        Server.ExecuteCommand($"bot_quota {quota}");
    }

    [ConsoleCommand("lst_retake_seats", "Retakes seats: lst_retake_seats SEATS BOTS (0 = off)")]
    [CommandHelper(minArgs: 1, usage: "SEATS [BOTS]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeSeatsCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        ListenRetakeExtras();
        _retakeSeats = int.TryParse(cmd.GetArg(1), out var seats) ? Math.Clamp(seats, 0, 64) : 0;
        _retakeBotsWanted = cmd.ArgCount > 2 && int.TryParse(cmd.GetArg(2), out var bots) ? Math.Max(0, bots) : 0;
        RecountHumans();
    }

    [ConsoleCommand("lst_retake_rifles", "1 = everyone gets a rifle, pistol and armor every retake round")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeRiflesCommand(CCSPlayerController? caller, CommandInfo cmd) => _retakeRifles = cmd.GetArg(1) == "1";

    [ConsoleCommand("lst_instadefuse", "1 = every defuse finishes instantly")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnInstaDefuseCommand(CCSPlayerController? caller, CommandInfo cmd) => _instaDefuse = cmd.GetArg(1) == "1";

    [GameEventHandler]
    public HookResult OnRetakeRiflesRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (!_retakeRifles) return HookResult.Continue;
        // The retakes plugin hands out its guns on round post-start, just after this. Replace them after that.
        AddTimer(0.7f, GiveRetakeRifles);
        return HookResult.Continue;
    }

    private void GiveRetakeRifles()
    {
        if (!_retakeRifles) return;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || !player.PawnIsAlive) continue;
            if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;
            var ct = player.Team == CsTeam.CounterTerrorist;
            StripGuns(player);
            player.GiveNamedItem(ct ? "weapon_m4a1_silencer" : "weapon_ak47");
            player.GiveNamedItem(ct ? "weapon_usp_silencer" : "weapon_glock");
            player.GiveNamedItem("item_assaultsuit");
            if (ct) player.GiveNamedItem("item_defuser");
            if (!IsBotPlayer(player)) player.ExecuteClientCommandFromServer("slot1");
        }
    }

    [GameEventHandler]
    public HookResult OnInstaDefuseBegin(EventBombBegindefuse @event, GameEventInfo info)
    {
        if (!_instaDefuse) return HookResult.Continue;
        // The defuse timer is set right after this event: finish it on the next frame.
        Server.NextFrame(() =>
        {
            var bomb = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault(b => b.IsValid && b.BeingDefused);
            if (bomb == null) return;
            bomb.DefuseCountDown = Server.CurrentTime;
            Utilities.SetStateChanged(bomb, "CPlantedC4", "m_flDefuseCountDown");
        });
        return HookResult.Continue;
    }

    [ConsoleCommand("lst_bomb_timer", "Retakes bomb timer in seconds, from when the round goes live (0 = off)")]
    [CommandHelper(minArgs: 1, usage: "SECONDS", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBombTimerCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _bombTimer = int.TryParse(cmd.GetArg(1), out var seconds) ? Math.Clamp(seconds, 0, 120) : 0;
        if (_bombTimer > 0) Server.ExecuteCommand($"mp_c4timer {_bombTimer}");
    }

    [GameEventHandler]
    public HookResult OnBombTimerPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (_bombTimer > 0) Server.NextFrame(ResetBombClock);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnBombTimerFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (_bombTimer <= 0) return HookResult.Continue;
        Server.NextFrame(ResetBombClock);
        // In case auto-plant runs on freeze end too, after this handler.
        AddTimer(0.3f, ResetBombClock, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    /// <summary>Gives a ticking bomb the full timer from now.</summary>
    private void ResetBombClock()
    {
        foreach (var bomb in Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4"))
        {
            if (!bomb.IsValid || !bomb.BombTicking || bomb.HasExploded || bomb.BombDefused) continue;
            bomb.C4Blow = Server.CurrentTime + _bombTimer;
            bomb.TimerLength = _bombTimer;
            Utilities.SetStateChanged(bomb, "CPlantedC4", "m_flC4Blow");
            Utilities.SetStateChanged(bomb, "CPlantedC4", "m_flTimerLength");
        }
    }
}
