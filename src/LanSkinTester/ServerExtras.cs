using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Small server plugins for playing with friends, each switched on from the launcher (Live Server ›
/// Server Plugins):  lst_qol spawn=1 guns=1 ready=0 vote=1 summary=1 welcome=1 maps=de_dust2,de_mirage
///   spawn    2.5 s spawn protection (see-through until it ends; shooting ends it early)
///   guns     !guns ak47 deagle: your weapons on every Deathmatch respawn
///   ready    warmup waits until every player typed !ready
///   vote     !rtv: when half the players want it, vote the next map (!vote 1-6)
///   summary  round MVP in chat, !stats and !top for the session
///   welcome  a short "what you can do here" line when someone joins
/// </summary>
public partial class LanSkinTester
{
    private const float SpawnProtectSeconds = 2.5f;
    private const float VoteSeconds = 20f;

    private bool _qolSpawn = true, _qolGuns = true, _qolReady, _qolVote = true, _qolSummary = true, _qolWelcome = true;
    private string[] _voteMaps = [];

    private readonly HashSet<int> _protected = new();
    private readonly Dictionary<ulong, (string? Primary, string? Secondary)> _gunPicks = new();
    private readonly HashSet<ulong> _ready = new();
    private readonly HashSet<ulong> _rtv = new();
    private readonly Dictionary<ulong, int> _votes = new();
    private string[]? _voteChoices;
    private readonly Dictionary<ulong, PlayerStats> _stats = new();
    private readonly Dictionary<ulong, PlayerStats> _roundStats = new();
    private int _statRounds;

    private void LoadExtras()
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _protected.Clear();
            _ready.Clear();
            _rtv.Clear();
            _votes.Clear();
            _voteChoices = null;
            _stats.Clear();
            _roundStats.Clear();
            _statRounds = 0;
            if (_qolReady) Server.NextWorldUpdate(() => Server.ExecuteCommand("mp_warmup_pausetimer 1"));
        });
    }

    [ConsoleCommand("lst_qol", "Server plugins: lst_qol spawn=1 guns=1 ready=0 vote=1 summary=1 welcome=1 maps=a,b,c")]
    [CommandHelper(minArgs: 1, usage: "key=0|1 ...", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnQolCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var part in cmd.ArgString.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var on = kv[1] == "1";
            switch (kv[0].ToLowerInvariant())
            {
                case "spawn": _qolSpawn = on; break;
                case "guns": _qolGuns = on; break;
                case "ready":
                    if (_qolReady != on) Server.ExecuteCommand($"mp_warmup_pausetimer {(on ? 1 : 0)}");
                    _qolReady = on;
                    break;
                case "vote": _qolVote = on; break;
                case "summary": _qolSummary = on; break;
                case "welcome": _qolWelcome = on; break;
                case "maps":
                    _voteMaps = kv[1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Where(m => System.Text.RegularExpressions.Regex.IsMatch(m, "^[a-z0-9_]{1,64}$")).Distinct().ToArray();
                    break;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Spawn protection + !guns
    // ---------------------------------------------------------------------------------------------

    [GameEventHandler]
    public HookResult OnExtrasSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || player.IsHLTV) return HookResult.Continue;

        // Only where players respawn (Deathmatch, Arms Race, respawn on death): a round start in
        // competitive or retakes doesn't need it.
        if (_qolSpawn && !IsWarmup() && IsRespawnMode()) Protect(player);
        if (_qolGuns && !player.IsBot && IsDeathmatch() && _gunPicks.TryGetValue(player.SteamID, out var picks))
            AddTimer(0.2f, () => GiveGunPicks(player, picks), TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private void Protect(CCSPlayerController player)
    {
        Server.NextFrame(() =>
        {
            var pawn = player.IsValid ? player.PlayerPawn.Value : null;
            if (pawn == null || !pawn.IsValid || _god.Contains(player.Slot)) return;
            pawn.TakesDamage = false;
            SetSeeThrough(pawn, true);
            _protected.Add(player.Slot);
            AddTimer(SpawnProtectSeconds, () => Unprotect(player), TimerFlags.STOP_ON_MAPCHANGE);
        });
    }

    private void Unprotect(CCSPlayerController player)
    {
        if (!_protected.Remove(player.Slot) || !player.IsValid) return;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        if (!_god.Contains(player.Slot)) pawn.TakesDamage = true;
        SetSeeThrough(pawn, false);
    }

    private static void SetSeeThrough(CCSPlayerPawn pawn, bool on)
    {
        // Alpha only shows with a transparent render mode.
        pawn.RenderMode = on ? RenderMode_t.kRenderTransAlpha : RenderMode_t.kRenderNormal;
        pawn.Render = on ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(255, 255, 255, 255);
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
    }

    [GameEventHandler]
    public HookResult OnExtrasFire(EventWeaponFire @event, GameEventInfo info)
    {
        // Shooting ends the protection: no free kills from inside it.
        if (@event.Userid is { IsValid: true } player && _protected.Contains(player.Slot)) Unprotect(player);
        return HookResult.Continue;
    }

    private static bool IsDeathmatch() => CvarInt("game_type") == 1 && CvarInt("game_mode") == 2;

    private static bool IsRespawnMode() => IsDeathmatch() || CvarInt("mp_respawn_on_death_t") != 0 || CvarInt("mp_respawn_on_death_ct") != 0;

    private static bool IsWarmup()
    {
        var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        return rules?.WarmupPeriod == true;
    }

    /// <summary>Finds a gun by short name (ak47, m4a1_silencer, deagle) and whether it's a pistol.</summary>
    private static (string Classname, bool Pistol)? FindGun(string name)
    {
        name = name.Trim().ToLowerInvariant().Replace("weapon_", "");
        foreach (var (def, classname) in ItemData.Weapons)
        {
            if (classname == "weapon_" + name || (ItemData.DisplayNames.TryGetValue(def, out var display)
                && display.Replace(" ", "").Replace("-", "").Equals(name.Replace("-", ""), StringComparison.OrdinalIgnoreCase)))
                return (classname, ItemData.Pistols.Contains(def));
        }
        return null;
    }

    [ConsoleCommand("css_guns", "Your Deathmatch weapons: !guns <rifle> [pistol] | off")]
    public void OnGunsCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        if (!_qolGuns)
        {
            // Plugin retakes: !guns belongs to the retakes allocator's menu.
            if (!RetakesPluginRunning) Reply(player, "!guns is off on this server.");
            return;
        }
        if (cmd.ArgCount < 2)
        {
            var current = _gunPicks.TryGetValue(player.SteamID, out var p) ? $"{p.Primary ?? "-"} + {p.Secondary ?? "-"}" : "none";
            Reply(player, $"Your guns: {current.Replace("weapon_", "")}. Pick: !guns ak47 deagle (or m4a1_silencer usp_silencer awp ...), !guns off");
            return;
        }
        if (cmd.GetArg(1).Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _gunPicks.Remove(player.SteamID);
            Reply(player, "Guns: back to normal.");
            return;
        }

        string? primary = null, secondary = null;
        for (var i = 1; i < cmd.ArgCount; i++)
        {
            if (FindGun(cmd.GetArg(i)) is not { } gun)
            {
                Reply(player, $"Unknown gun: {cmd.GetArg(i)}");
                return;
            }
            if (gun.Pistol) secondary = gun.Classname;
            else primary = gun.Classname;
        }
        _gunPicks.TryGetValue(player.SteamID, out var old);
        var picks = (primary ?? old.Primary, secondary ?? old.Secondary);
        _gunPicks[player.SteamID] = picks;
        Reply(player, $"Guns saved: {(picks.Item1 ?? "-").Replace("weapon_", "")} + {(picks.Item2 ?? "-").Replace("weapon_", "")}" +
                      (IsDeathmatch() ? ". You get them on every respawn." : ". They are given in Deathmatch."));
        if (IsDeathmatch() && player.PawnIsAlive) GiveGunPicks(player, picks);
    }

    private void GiveGunPicks(CCSPlayerController player, (string? Primary, string? Secondary) picks)
    {
        if (!player.IsValid || !player.PawnIsAlive) return;
        foreach (var (classname, slot) in new[] { (picks.Primary, gear_slot_t.GEAR_SLOT_RIFLE), (picks.Secondary, gear_slot_t.GEAR_SLOT_PISTOL) })
        {
            if (classname == null) continue;
            var held = FindPlayerWeapon(player, w => w.As<CCSWeaponBase>().VData?.GearSlot == slot);
            if (held?.DesignerName == classname) continue;
            held?.AddEntityIOEvent("Kill", null, null, "", 0.01f);
            player.GiveNamedItem(classname);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // !ready
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("css_ready", "Ready for the match (warmup ends when everyone is)")]
    public void OnReadyCommand(CCSPlayerController? player, CommandInfo cmd) => SetReady(player, true);

    [ConsoleCommand("css_unready", "Not ready after all")]
    public void OnUnreadyCommand(CCSPlayerController? player, CommandInfo cmd) => SetReady(player, false);

    private void SetReady(CCSPlayerController? player, bool ready)
    {
        if (player == null || !player.IsValid || player.IsBot) return;
        if (!_qolReady || !IsWarmup())
        {
            Reply(player, "There is no ready-up right now.");
            return;
        }
        if (ready) _ready.Add(player.SteamID);
        else _ready.Remove(player.SteamID);

        var players = Humans().Where(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist).ToList();
        var count = players.Count(p => _ready.Contains(p.SteamID));
        Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} {player.PlayerName} is {(ready ? "ready" : "not ready")} ({count}/{players.Count}).");
        if (players.Count == 0 || count < players.Count) return;

        Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} Everyone is ready. Match starts!");
        _ready.Clear();
        Server.ExecuteCommand("mp_warmup_pausetimer 0; mp_warmup_end");
    }

    // ---------------------------------------------------------------------------------------------
    // !rtv map vote
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("css_rtv", "Rock the vote: change the map when half the players want it")]
    public void OnRtvCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid || player.IsBot) return;
        if (!_qolVote || _voteMaps.Length < 2)
        {
            Reply(player, "Map voting is off on this server.");
            return;
        }
        if (_voteChoices != null)
        {
            Reply(player, "A map vote is already running: !vote <number>");
            return;
        }
        _rtv.Add(player.SteamID);
        var humans = Humans().ToList();
        var needed = Math.Max(1, (humans.Count + 1) / 2);
        var have = humans.Count(p => _rtv.Contains(p.SteamID));
        Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} {player.PlayerName} wants a new map ({have}/{needed}). Type !rtv to agree.");
        if (have >= needed) StartMapVote();
    }

    private void StartMapVote()
    {
        _rtv.Clear();
        _votes.Clear();
        _voteChoices = _voteMaps.Where(m => m != Server.MapName).OrderBy(_ => Random.Shared.Next()).Take(6).ToArray();
        if (_voteChoices.Length == 0)
        {
            _voteChoices = null;
            return;
        }
        Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} Map vote ({VoteSeconds:0} s): " +
                              string.Join("  ", _voteChoices.Select((m, i) => $"{ChatColors.Yellow}!vote {i + 1}{ChatColors.Default} {m}")));
        AddTimer(VoteSeconds, EndMapVote, TimerFlags.STOP_ON_MAPCHANGE);
    }

    [ConsoleCommand("css_vote", "Vote in a map vote: !vote <number>")]
    public void OnVoteCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid || player.IsBot) return;
        if (_voteChoices == null)
        {
            Reply(player, "No map vote right now. Start one with !rtv");
            return;
        }
        if (!int.TryParse(cmd.GetArg(1), out var n) || n < 1 || n > _voteChoices.Length)
        {
            Reply(player, $"Pick 1-{_voteChoices.Length}: !vote 2");
            return;
        }
        _votes[player.SteamID] = n - 1;
        Reply(player, $"You voted {_voteChoices[n - 1]}.");
        // Everyone voted: no need to wait for the timer.
        if (Humans().All(p => _votes.ContainsKey(p.SteamID))) EndMapVote();
    }

    private void EndMapVote()
    {
        if (_voteChoices == null) return;
        var choices = _voteChoices;
        _voteChoices = null;
        if (_votes.Count == 0)
        {
            Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} Nobody voted - staying on {Server.MapName}.");
            return;
        }
        var winner = choices[_votes.Values.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(_ => Random.Shared.Next()).First().Key];
        _votes.Clear();
        Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} Next map: {ChatColors.Yellow}{winner}{ChatColors.Default} (in 5 s)");
        AddTimer(5f, () => Server.ExecuteCommand($"changelevel {winner}"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    // ---------------------------------------------------------------------------------------------
    // Round MVP, !stats, !top
    // ---------------------------------------------------------------------------------------------

    // Bots are counted too (for the launcher's Live page), by slot: their SteamID is 0.
    private static ulong StatKey(CCSPlayerController player) => player.IsBot ? (ulong)player.Slot : player.SteamID;

    private PlayerStats StatsOf(Dictionary<ulong, PlayerStats> table, CCSPlayerController player)
    {
        if (!table.TryGetValue(StatKey(player), out var s)) table[StatKey(player)] = s = new PlayerStats();
        s.Name = player.PlayerName;
        s.Bot = player.IsBot;
        return s;
    }

    [ConsoleCommand("lst_stats_reset", "Start the session stats (!stats, !top, the launcher's Live page) over")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnStatsResetCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _stats.Clear();
        _roundStats.Clear();
        _statRounds = 0;
    }

    /// <summary>The session's stats for status.json, best first.</summary>
    private List<PlayerStats> StatsForStatus() => _stats.Values
        .Select(s => new PlayerStats { Name = s.Name, Bot = s.Bot, Kills = s.Kills, Deaths = s.Deaths, Headshots = s.Headshots, Damage = s.Damage, Rounds = Math.Max(_statRounds, 1) })
        .OrderByDescending(s => s.Kills).ThenByDescending(s => s.Damage).ToList();

    [GameEventHandler]
    public HookResult OnExtrasDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        if (victim is { IsValid: true }) StatsOf(_stats, victim).Deaths++;
        if (attacker is { IsValid: true } && attacker.Slot != victim?.Slot)
        {
            foreach (var table in new[] { _stats, _roundStats })
            {
                var s = StatsOf(table, attacker);
                s.Kills++;
                if (@event.Headshot) s.Headshots++;
            }
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnExtrasHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (@event.Attacker is not { IsValid: true } attacker || attacker.Slot == @event.Userid?.Slot)
            return HookResult.Continue;
        var damage = Math.Min(@event.DmgHealth, 100);
        StatsOf(_stats, attacker).Damage += damage;
        StatsOf(_roundStats, attacker).Damage += damage;
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnExtrasRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (!IsWarmup()) _statRounds++;
        if (!_qolSummary || IsDeathmatch()) return HookResult.Continue;
        var mvp = _roundStats.Values.Where(s => !s.Bot).OrderByDescending(s => s.Kills).ThenByDescending(s => s.Damage).FirstOrDefault();
        _roundStats.Clear();
        if (mvp == null || mvp.Kills == 0) return HookResult.Continue;
        Server.PrintToChatAll($" {ChatColors.Gold}★ Round MVP{ChatColors.Default} {mvp.Name}: {mvp.Kills} kill{(mvp.Kills == 1 ? "" : "s")}" +
                              $" ({mvp.Headshots} HS), {mvp.Damage} damage");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnExtrasRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _roundStats.Clear();
        return HookResult.Continue;
    }

    private static string Describe(PlayerStats s) =>
        $"{s.Kills} K / {s.Deaths} D (K/D {(s.Deaths == 0 ? s.Kills : (double)s.Kills / s.Deaths):0.00}) · " +
        $"{(s.Kills == 0 ? 0 : 100 * s.Headshots / s.Kills)}% HS · {s.Damage} damage";

    [ConsoleCommand("css_stats", "Your kills, deaths, headshots and damage on this map")]
    public void OnStatsCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid || !_qolSummary) return;
        Reply(player, _stats.TryGetValue(player.SteamID, out var s) ? Describe(s) : "No kills or deaths yet on this map.");
    }

    [ConsoleCommand("css_top", "Best players on this map")]
    public void OnTopCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid || !_qolSummary) return;
        var top = _stats.Values.Where(s => !s.Bot).OrderByDescending(s => s.Kills).ThenByDescending(s => s.Damage).Take(3).ToList();
        if (top.Count == 0)
        {
            Reply(player, "No kills yet on this map.");
            return;
        }
        for (var i = 0; i < top.Count; i++) Reply(player, $"{i + 1}. {top[i].Name} - {Describe(top[i])}");
    }

    // ---------------------------------------------------------------------------------------------
    // Welcome line
    // ---------------------------------------------------------------------------------------------

    [GameEventHandler]
    public HookResult OnExtrasConnect(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!_qolWelcome || player == null || !player.IsValid || player.IsBot) return HookResult.Continue;
        var extras = new List<string> { "!skinhelp skins" };
        if (_qolGuns && IsDeathmatch()) extras.Add("!guns");
        if (_qolVote && _voteMaps.Length > 1) extras.Add("!rtv new map");
        if (_qolSummary) extras.Add("!stats");
        if (_qolReady) extras.Add("!ready");
        AddTimer(4f, () =>
        {
            if (player.IsValid) Reply(player, $"Welcome, {player.PlayerName}! Try: {string.Join(" · ", extras)}");
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnExtrasDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is { IsValid: true } player)
        {
            _protected.Remove(player.Slot);
            _rtv.Remove(player.SteamID);
            _ready.Remove(player.SteamID);
            _votes.Remove(player.SteamID);
        }
        return HookResult.Continue;
    }
}
