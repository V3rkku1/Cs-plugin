using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Retakes extras:
///   lst_retake_seats SEATS BOTS [T CT]  retakes only has SEATS places. Bots give theirs up the moment a
///                                 person connects, so you and your friends always get a team (0 = off).
///                                 With team sizes (Valve retakes: 3 4), picking a full side swaps out a bot.
///   lst_retake_rifles 0|1         every round: a rifle, a pistol, armor (and a kit on CT) for everyone.
///   lst_retake_bots 0|1           realistic bots: mixed full buys, grenades, sometimes an AWP (also gives
///                                 bots a loadout in buy mode, where only people buy).
///   lst_instadefuse 0|1           any defuse finishes instantly, in every mode.
///   lst_bomb_timer SECONDS        retakes bomb timer, counted from when the round goes live (0 = off).
///                                 Auto-plant happens during freeze time, so the clock was already running.
/// </summary>
public partial class LanSkinTester
{
    private int _retakeSeats, _retakeBotsWanted, _retakeTSize, _retakeCtSize;
    private readonly HashSet<int> _rejoining = new();
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
        AddCommandListener("jointeam", OnJoinTeam);
    }

    /// <summary>A person picks a side that bots have filled: one of those bots makes room.</summary>
    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player is not { IsValid: true } || IsBotPlayer(player) || _retakeSeats <= 0) return HookResult.Continue;
        if (_rejoining.Remove(player.Slot)) return HookResult.Continue;
        if (!int.TryParse(cmd.GetArg(1), out var wanted) || wanted is not (2 or 3) || (int)player.Team == wanted) return HookResult.Continue;
        var team = (CsTeam)wanted;
        var size = team == CsTeam.Terrorist ? _retakeTSize : _retakeCtSize;
        if (size <= 0) return HookResult.Continue;
        var onTeam = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && p.Team == team).ToList();
        if (onTeam.Count < size) return HookResult.Continue;
        var bot = onTeam.FirstOrDefault(IsBotPlayer);
        if (bot == null) return HookResult.Continue; // full of people: nothing to do

        // Swap: the bot leaves, then the same jointeam runs again once its seat is free.
        Server.ExecuteCommand($"kickid {bot.UserId}");
        var slot = player.Slot;
        AddTimer(0.3f, () =>
        {
            if (!player.IsValid) return;
            _rejoining.Add(slot);
            player.ExecuteClientCommandFromServer($"jointeam {wanted} 1");
        });
        return HookResult.Handled;
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
        _retakeTSize = cmd.ArgCount > 3 && int.TryParse(cmd.GetArg(3), out var t) ? Math.Max(0, t) : 0;
        _retakeCtSize = cmd.ArgCount > 4 && int.TryParse(cmd.GetArg(4), out var ct) ? Math.Max(0, ct) : 0;
        RecountHumans();
    }

    [ConsoleCommand("lst_retake_rifles", "1 = everyone gets a rifle, pistol and armor every retake round")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeRiflesCommand(CCSPlayerController? caller, CommandInfo cmd) => _retakeRifles = cmd.GetArg(1) == "1";

    [ConsoleCommand("lst_instadefuse", "1 = every defuse finishes instantly")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnInstaDefuseCommand(CCSPlayerController? caller, CommandInfo cmd) => _instaDefuse = cmd.GetArg(1) == "1";

    [ConsoleCommand("lst_retake_bots", "1 = retake bots get real full-buy loadouts with utility and the odd AWP")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeBotsCommand(CCSPlayerController? caller, CommandInfo cmd) => _realisticRetakeBots = cmd.GetArg(1) == "1";

    private bool _realisticRetakeBots;

    [GameEventHandler]
    public HookResult OnRetakeRiflesRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        // Rifles for everyone, or (buy period) real loadouts for the bots, who don't buy themselves.
        if (!_retakeRifles && !(_realisticRetakeBots && _retakeBuy)) return HookResult.Continue;
        // The retakes plugin hands out its guns on round post-start, just after this. Replace them after that.
        AddTimer(0.7f, GiveRetakeLoadouts);
        return HookResult.Continue;
    }

    private void GiveRetakeLoadouts()
    {
        var everyone = _retakeRifles;
        if (!everyone && !(_realisticRetakeBots && _retakeBuy)) return;
        var players = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.PawnIsAlive && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
            .Where(p => everyone || IsBotPlayer(p))
            .ToList();

        // Like a real full buy: now and then one bot per side brings an AWP.
        var awpers = new HashSet<int>();
        if (_realisticRetakeBots)
        {
            foreach (var team in new[] { CsTeam.Terrorist, CsTeam.CounterTerrorist })
            {
                var bots = players.Where(p => p.Team == team && IsBotPlayer(p)).ToList();
                if (bots.Count > 1 && Random.Shared.NextDouble() < 0.4) awpers.Add(bots[Random.Shared.Next(bots.Count)].Slot);
            }
        }

        foreach (var player in players)
        {
            var ct = player.Team == CsTeam.CounterTerrorist;
            var bot = IsBotPlayer(player);
            var realistic = _realisticRetakeBots && bot;
            StripGuns(player);
            player.GiveNamedItem(awpers.Contains(player.Slot) ? "weapon_awp" : PrimaryFor(ct, realistic));
            player.GiveNamedItem(PistolFor(ct, realistic));
            player.GiveNamedItem("item_assaultsuit");
            if (ct) player.GiveNamedItem("item_defuser");
            if (_realisticRetakeBots)
                foreach (var nade in UtilityFor(ct)) player.GiveNamedItem(nade);
            if (!bot) player.ExecuteClientCommandFromServer("slot1");
        }
    }

    private static string PrimaryFor(bool ct, bool mixed)
    {
        if (!mixed) return ct ? "weapon_m4a1_silencer" : "weapon_ak47";
        var roll = Random.Shared.NextDouble();
        return ct ? roll < 0.55 ? "weapon_m4a1_silencer" : roll < 0.92 ? "weapon_m4a1" : "weapon_famas"
                  : roll < 0.9 ? "weapon_ak47" : "weapon_galilar";
    }

    private static string PistolFor(bool ct, bool mixed)
    {
        if (!mixed) return ct ? "weapon_usp_silencer" : "weapon_glock";
        var roll = Random.Shared.NextDouble();
        return ct ? roll < 0.6 ? "weapon_usp_silencer" : roll < 0.8 ? "weapon_hkp2000" : "weapon_fiveseven"
                  : roll < 0.7 ? "weapon_glock" : roll < 0.85 ? "weapon_tec9" : "weapon_p250";
    }

    /// <summary>Two or three grenades, as a retake player would carry.</summary>
    private static IEnumerable<string> UtilityFor(bool ct)
    {
        yield return Random.Shared.NextDouble() < 0.6 ? "weapon_smokegrenade" : "weapon_flashbang";
        yield return "weapon_flashbang";
        var roll = Random.Shared.NextDouble();
        if (roll < 0.45) yield return ct ? "weapon_incgrenade" : "weapon_molotov";
        else if (roll < 0.75) yield return "weapon_hegrenade";
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
