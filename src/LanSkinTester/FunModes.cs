using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Fun modes on classic rounds (Casual / Competitive), set by the launcher (lst_funmode):
/// Zombie infection: a few players start as zombies (Terrorists, knife only, faster, more health the more
/// humans each has to face); a human a zombie kills joins the zombies. Humans win if any survive the round. Hide and seek: Terrorists hide with a knife
/// while the Counter-Terrorists wait 30 seconds; Terrorists win if one is alive when time runs out.
/// </summary>
public partial class LanSkinTester
{
    private enum FunMode { Off, Zombie, HideAndSeek }

    private FunMode _funMode;
    private readonly HashSet<int> _zombies = new();
    private int _funRound;
    private const int ZombieMinHealth = 2000;
    private const float ZombieSpeed = 1.2f;
    // The first zombies' health this round: more humans per zombie, more health. Infected ones get half.
    private int _zombieHealth = ZombieMinHealth;
    private const float SeekerWait = 30;

    [ConsoleCommand("lst_funmode", "Fun mode: lst_funmode off|zombie|hns")]
    [CommandHelper(minArgs: 1, usage: "off|zombie|hns", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnFunModeCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var mode = cmd.GetArg(1).ToLowerInvariant() switch { "zombie" => FunMode.Zombie, "hns" => FunMode.HideAndSeek, _ => FunMode.Off };
        if (mode == _funMode) return;
        _funMode = mode;
        _zombies.Clear();
        // Teams change size all the time in these modes: the game must not move people back.
        if (mode != FunMode.Off) Server.ExecuteCommand("mp_autoteambalance 0; mp_limitteams 0");
        if (mode != FunMode.Off) TellEveryone(mode == FunMode.Zombie
            ? "Zombie infection: zombies (T) infect whoever they kill. Humans, survive the round!"
            : "Hide and seek: Terrorists hide (knife only), Counter-Terrorists wait 30 s, then seek.");
    }

    private bool FunModeRunning => _funMode != FunMode.Off && CvarInt("game_type") == 0;

    private void TellEveryone(string message)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && !IsBotPlayer(p))) Reply(player, message);
    }

    private static List<CCSPlayerController> Playing() =>
        Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist).ToList();

    [GameEventHandler]
    public HookResult OnFunRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _funRound++;
        _zombies.Clear();
        if (!FunModeRunning) return HookResult.Continue;
        if (_funMode == FunMode.HideAndSeek) _anchors.Clear(); // last round's seekers, if it ended inside the 30 s
        if (_funMode == FunMode.Zombie) AddTimer(0.3f, PickZombies, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    /// <summary>About one zombie per six players (at least one) on T, everyone else on CT, before buying ends.</summary>
    private void PickZombies()
    {
        var players = Playing().OrderBy(_ => Random.Shared.Next()).ToList();
        if (players.Count < 2) return;
        var count = Math.Max(1, players.Count / 6);
        _zombieHealth = Math.Clamp(ZombieMinHealth + 500 * (players.Count - count) / count, ZombieMinHealth, 8000);
        // Moving people to their side kills them for a moment: a side with nobody alive would end the round.
        var ignore = ConVar.Find("mp_ignore_round_win_conditions");
        var was = ignore?.GetPrimitiveValue<bool>() ?? false;
        Server.ExecuteCommand("mp_ignore_round_win_conditions 1");
        AddTimer(2f, () => Server.ExecuteCommand($"mp_ignore_round_win_conditions {(was ? 1 : 0)}"), TimerFlags.STOP_ON_MAPCHANGE);
        for (var i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var zombie = i < count;
            var team = zombie ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
            if (zombie) _zombies.Add(player.Slot);
            if (player.Team == team)
            {
                if (zombie) MakeZombie(player, _zombieHealth);
                continue;
            }
            // ChangeTeam kills: bring them straight back on their new side (freeze time, so they can still buy).
            player.ChangeTeam(team);
            AddTimer(0.3f, () =>
            {
                if (!player.IsValid) return;
                if (!player.PawnIsAlive) player.Respawn();
                if (zombie) AddTimer(0.2f, () => MakeZombie(player, _zombieHealth), TimerFlags.STOP_ON_MAPCHANGE);
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        TellEveryone((count == 1 ? "One zombie is loose" : $"{count} zombies are loose") + $" ({_zombieHealth} HP, faster than you). Don't let them touch you!");
    }

    private void MakeZombie(CCSPlayerController player, int health)
    {
        if (!player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        player.RemoveWeapons();
        player.GiveNamedItem("weapon_knife");
        pawn.Health = pawn.MaxHealth = health;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iMaxHealth");
        if (player.InGameMoneyServices is { } money)
        {
            money.Account = 0;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }
    }

    [GameEventHandler]
    public HookResult OnFunDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (!FunModeRunning || _funMode != FunMode.Zombie) return HookResult.Continue;
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        if (victim is not { IsValid: true } || attacker is not { IsValid: true } || !_zombies.Contains(attacker.Slot)
            || _zombies.Contains(victim.Slot) || victim.Team != CsTeam.CounterTerrorist) return HookResult.Continue;
        // Infected: the human rises as a zombie a moment later (unless that was the last human: the round ends).
        _zombies.Add(victim.Slot);
        var round = _funRound;
        AddTimer(0.1f, () =>
        {
            if (!victim.IsValid || round != _funRound) return;
            victim.ChangeTeam(CsTeam.Terrorist);
            if (!Playing().Any(p => p.Team == CsTeam.CounterTerrorist && p.PawnIsAlive)) return;
            AddTimer(1.5f, () =>
            {
                if (!victim.IsValid || round != _funRound) return;
                victim.Respawn();
                AddTimer(0.2f, () => MakeZombie(victim, Math.Max(ZombieMinHealth, _zombieHealth / 2)), TimerFlags.STOP_ON_MAPCHANGE);
                Reply(victim, "You were infected: you're a zombie now. Knife the humans!");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    /// <summary>Every tick: zombies run faster (the game slows anyone who gets shot, so it's set again).</summary>
    private void TickFunModes()
    {
        if (_zombies.Count == 0 || _funMode != FunMode.Zombie) return;
        foreach (var slot in _zombies)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true, PawnIsAlive: true } || player.Team != CsTeam.Terrorist
                || player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.VelocityModifier >= ZombieSpeed) continue;
            pawn.VelocityModifier = ZombieSpeed;
            Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
        }
    }

    [GameEventHandler]
    public HookResult OnFunPickup(EventItemPickup @event, GameEventInfo info)
    {
        // Zombies (and hiders) only ever hold a knife.
        var player = @event.Userid;
        var item = @event.Item ?? "";
        if (!FunModeRunning || player is not { IsValid: true } || item.Contains("knife") || item.Contains("bayonet")) return HookResult.Continue;
        var knifeOnly = _zombies.Contains(player.Slot) || (_funMode == FunMode.HideAndSeek && player.Team == CsTeam.Terrorist);
        if (knifeOnly) AddTimer(0.05f, () => { if (player.IsValid) player.RemoveItemByDesignerName("weapon_" + item); });
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnFunFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!FunModeRunning || _funMode != FunMode.HideAndSeek) return HookResult.Continue;
        var round = _funRound;
        foreach (var player in Playing().Where(p => p.PawnIsAlive))
        {
            if (player.Team == CsTeam.Terrorist)
            {
                player.RemoveWeapons();
                player.GiveNamedItem("weapon_knife");
            }
            else Anchor(player);
        }
        TellEveryone($"Hide! Seekers go in {SeekerWait:0} seconds.");
        AddTimer(SeekerWait, () =>
        {
            if (round != _funRound) return;
            _anchors.Clear();
            TellEveryone("Ready or not, here they come!");
        }, TimerFlags.STOP_ON_MAPCHANGE);
        // Time runs out with a hider alive: the hiders win (a bomb map would give it to the seekers).
        var minutes = ConVar.Find("mp_roundtime_defuse")?.GetPrimitiveValue<float>() ?? 0;
        if (minutes <= 0) minutes = ConVar.Find("mp_roundtime")?.GetPrimitiveValue<float>() ?? 2;
        AddTimer(Math.Max(5, minutes * 60 - 1), () =>
        {
            if (round != _funRound || !Playing().Any(p => p.Team == CsTeam.Terrorist && p.PawnIsAlive)) return;
            EndRound(tWin: true);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }
}
