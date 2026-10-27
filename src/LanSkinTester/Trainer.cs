using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// The launcher's Trainer page: site drills against bots (clear a held site, plant and stop the retake,
/// hold a site against a push), a reaction drill (one bot pops up in front of you, the time to hit it is
/// shown), a speedometer with jump stats, and lineups by name (lst_lineup). Bots are never placed from
/// per-map files: site spots are random floor points inside the map's own bomb site volume.
/// </summary>
public partial class LanSkinTester
{
    private enum Drill { Off, Clear, Retake, Hold, Reflex }

    private Drill _drill;
    private bool _drillSiteB;
    private int _drillWins, _drillRounds, _lineupIndex;
    private float _reflexShownAt = -1;
    private readonly List<float> _reflexTimes = new();
    private readonly Dictionary<int, JumpTrack> _speedo = new();

    private sealed class JumpTrack
    {
        public bool OnGround = true;
        public float FromX, FromY, Pre, Last, LastPre;
        public int Hops, LandedTick;
    }

    private void LoadTrainer()
    {
        RegisterListener<Listeners.OnTick>(OnTrainerTick);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _speedo.Clear();
            _reflexShownAt = -1;
            _lineupIndex = 0;
        });
    }

    // ---------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("lst_drill", "Trainer drill: lst_drill off|clear|retake|hold|reflex [a|b] [bots]")]
    [CommandHelper(minArgs: 1, usage: "off|clear|retake|hold|reflex [a|b] [bots]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnDrillCommand(CCSPlayerController? caller, CommandInfo cmd) =>
        StartDrill(cmd.GetArg(1), cmd.ArgCount > 2 ? cmd.GetArg(2) : "a", cmd.ArgCount > 3 ? cmd.GetArg(3) : "3");

    [ConsoleCommand("css_drill", "Trainer drill: !drill off|clear|retake|hold|reflex [a|b] [bots]")]
    [CommandHelper(minArgs: 1, usage: "off|clear|retake|hold|reflex [a|b] [bots]")]
    public void OnDrillChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        StartDrill(cmd.GetArg(1), cmd.ArgCount > 2 ? cmd.GetArg(2) : "a", cmd.ArgCount > 3 ? cmd.GetArg(3) : "3");
    }

    [ConsoleCommand("lst_speedo", "Speedometer and jump stats for every human: 1 on, 0 off, or toggle")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSpeedoCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var on = cmd.ArgCount > 1 ? cmd.GetArg(1) == "1" : _speedo.Count == 0;
        _speedo.Clear();
        if (on) foreach (var player in Humans()) _speedo[player.Slot] = new JumpTrack();
    }

    [ConsoleCommand("css_speed", "Toggle the speedometer and jump stats")]
    public void OnSpeedChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (!_speedo.Remove(player!.Slot)) _speedo[player.Slot] = new JumpTrack();
        Reply(player, _speedo.ContainsKey(player.Slot) ? "Speedometer on." : "Speedometer off.");
    }

    [ConsoleCommand("lst_lineup", "Send every human to a saved lineup: lst_lineup <name>|next")]
    [CommandHelper(minArgs: 1, usage: "<name>|next", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnLineupCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var lineups = Lineups().OrderBy(l => l.Key).ToList();
        if (lineups.Count == 0) return;
        var name = cmd.GetArg(1).ToLowerInvariant() == "next" ? lineups[_lineupIndex++ % lineups.Count].Key : CleanSpot(cmd.GetArg(1));
        var lineup = lineups.FirstOrDefault(l => l.Key == name);
        if (lineup.Key == null) return;
        foreach (var player in Humans())
        {
            GoToThrow(player, lineup.Value);
            Reply(player, $"Lineup \"{name}\" ({lineup.Value.Weapon.Replace("weapon_", "")}): throw!");
        }
    }

    [ConsoleCommand("lst_delnade", "Delete a saved lineup on this map: lst_delnade <name>")]
    [CommandHelper(minArgs: 1, usage: "<name>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnDelNadeCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var lineups = Lineups();
        if (lineups.Remove(CleanSpot(cmd.GetArg(1)))) SaveLineups(lineups);
    }

    // ---------------------------------------------------------------------------------------------
    // Drills
    // ---------------------------------------------------------------------------------------------

    private void StartDrill(string kind, string site, string bots)
    {
        var drill = kind.ToLowerInvariant() switch
        {
            "clear" => Drill.Clear, "retake" => Drill.Retake, "hold" => Drill.Hold, "reflex" => Drill.Reflex, _ => Drill.Off,
        };
        var wasReflex = _drill == Drill.Reflex;
        _drill = drill;
        _drillSiteB = site.StartsWith("b", StringComparison.OrdinalIgnoreCase);
        _drillWins = _drillRounds = 0;
        _reflexTimes.Clear();
        _reflexShownAt = -1;
        var count = int.TryParse(bots, out var n) ? Math.Clamp(n, 1, 10) : 3;
        var siteName = _drillSiteB ? "B" : "A";

        if (drill == Drill.Off)
        {
            foreach (var bot in Utilities.GetPlayers().Where(p => p.IsValid && IsBotPlayer(p))) Anchor(bot, false);
            if (wasReflex) Server.ExecuteCommand("bot_dont_shoot 0; mp_ignore_round_win_conditions 0");
            TellHumans("Drill off. Apply Changes on the launcher's Live Server page brings back your bots and rules.");
            return;
        }

        if (drill != Drill.Reflex && !FindSite(_drillSiteB, out _, out _))
        {
            _drill = Drill.Off;
            TellHumans($"This map has no bomb site {siteName}. Site drills need a bomb map (de_) in Competitive or Casual.");
            return;
        }

        // The humans play one side, the bots the other.
        var humanTeam = drill == Drill.Hold ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        if (drill == Drill.Reflex)
        {
            humanTeam = Humans().FirstOrDefault(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)?.Team ?? CsTeam.CounterTerrorist;
            count = 1;
        }
        else foreach (var player in Humans().Where(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist && p.Team != humanTeam))
            player.SwitchTeam(humanTeam);
        var botTeam = humanTeam == CsTeam.Terrorist ? "ct" : "t";
        Server.ExecuteCommand($"mp_warmup_end; bot_kick; bot_quota_mode normal; bot_join_team {botTeam}; bot_quota {count}");
        if (drill == Drill.Reflex)
        {
            Server.ExecuteCommand("bot_dont_shoot 1; mp_ignore_round_win_conditions 1; mp_roundtime 60; mp_roundtime_defuse 60");
            AddTimer(2f, () => ShowReflexBot(), TimerFlags.STOP_ON_MAPCHANGE);
            TellHumans("Reaction drill: a bot pops up in front of you. Shoot it as fast as you can.");
            return;
        }
        if (wasReflex) Server.ExecuteCommand("bot_dont_shoot 0; mp_ignore_round_win_conditions 0");
        Server.ExecuteCommand("mp_freezetime 3; mp_buytime 20; mp_roundtime_defuse 1.92; mp_restartgame 1");
        TellHumans(drill switch
        {
            Drill.Clear => $"Clear site {siteName}: {count} bots hold angles on the site. Clear it and plant.",
            Drill.Retake => $"Plant on {siteName}: {count} bots wait at CT spawn and push when the bomb is down.",
            _ => $"Hold site {siteName}: {count} bots attack. Stop the plant.",
        });
    }

    [GameEventHandler]
    public HookResult OnDrillFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (_drill is Drill.Off or Drill.Reflex) return HookResult.Continue;
        AddTimer(0.1f, () =>
        {
            var bots = Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && IsBotPlayer(p)).ToList();
            var humans = Humans().Where(p => p.PawnIsAlive).ToList();
            if (_drill == Drill.Clear)
            {
                var spots = SiteSpots(_drillSiteB, bots.Count);
                var face = humans.FirstOrDefault()?.PlayerPawn.Value?.AbsOrigin;
                for (var i = 0; i < Math.Min(spots.Count, bots.Count); i++)
                {
                    PlaceAt(bots[i], spots[i], face);
                    Anchor(bots[i], true);
                }
            }
            else if (_drill == Drill.Retake) bots.ForEach(b => Anchor(b, true));
            else
            {
                var spots = SiteSpots(_drillSiteB, humans.Count);
                for (var i = 0; i < Math.Min(spots.Count, humans.Count); i++) PlaceAt(humans[i], spots[i], null);
            }
            foreach (var player in humans) GiveArmor(player);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDrillRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (_drill == Drill.Off) return HookResult.Continue;
        if (_drill == Drill.Reflex)
        {
            AddTimer(0.5f, () => ShowReflexBot(), TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        }
        // Full buy every round.
        foreach (var player in Humans())
        {
            if (player.InGameMoneyServices is not { } money) continue;
            money.Account = 16000;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        }
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDrillBombPlanted(EventBombPlanted @event, GameEventInfo info)
    {
        if (_drill != Drill.Retake) return HookResult.Continue;
        foreach (var bot in Utilities.GetPlayers().Where(p => p.IsValid && IsBotPlayer(p))) Anchor(bot, false);
        TellHumans("Bomb down: here they come.");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDrillRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        if (_drill is Drill.Off or Drill.Reflex) return HookResult.Continue;
        var humanTeam = _drill == Drill.Hold ? CsTeam.CounterTerrorist : CsTeam.Terrorist;
        if (@event.Winner is not ((int)CsTeam.Terrorist or (int)CsTeam.CounterTerrorist)) return HookResult.Continue;
        _drillRounds++;
        if (@event.Winner == (int)humanTeam) _drillWins++;
        TellHumans($"{(@event.Winner == (int)humanTeam ? "Round won" : "Round lost")} · drill {_drillWins}/{_drillRounds} won");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnDrillDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (_drill != Drill.Reflex || @event.Userid is not { IsValid: true } victim || !IsBotPlayer(victim)) return HookResult.Continue;
        if (_reflexShownAt >= 0 && @event.Attacker is { IsValid: true } attacker && IsHumanPlayer(attacker))
        {
            var ms = (Server.CurrentTime - _reflexShownAt) * 1000f;
            _reflexTimes.Add(ms);
            Reply(attacker, $"Reaction {ms:0} ms · average {_reflexTimes.Average():0} · best {_reflexTimes.Min():0} · {_reflexTimes.Count} hits");
        }
        _reflexShownAt = -1;
        AddTimer(0.6f + Random.Shared.NextSingle() * 1.4f, () => ShowReflexBot(), TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    /// <summary>Respawns the reflex bot (if dead) and puts it somewhere in front of the first human.</summary>
    private void ShowReflexBot(bool respawned = false)
    {
        if (_drill != Drill.Reflex) return;
        var bot = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && IsBotPlayer(p) && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist);
        var human = Humans().FirstOrDefault(p => p.PawnIsAlive);
        if (bot == null || human?.PlayerPawn.Value is not { IsValid: true, AbsOrigin: not null } pawn) return;
        if (!bot.PawnIsAlive)
        {
            if (respawned) return;
            bot.Respawn();
            AddTimer(0.2f, () => ShowReflexBot(respawned: true), TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }
        if (ReflexSpot(pawn) is not { } spot) return;
        PlaceAt(bot, spot, pawn.AbsOrigin);
        Anchor(bot, true);
        if (bot.PlayerPawn.Value is { IsValid: true } botPawn)
        {
            botPawn.Health = 1; // any hit counts
            Utilities.SetStateChanged(botPawn, "CBaseEntity", "m_iHealth");
        }
        _reflexShownAt = Server.CurrentTime;
    }

    /// <summary>A visible floor point 300 to 1000 units ahead of the player, within 60° of where they look.</summary>
    private Vector? ReflexSpot(CCSPlayerPawn pawn)
    {
        var from = pawn.AbsOrigin!;
        var eye = new Vector(from.X, from.Y, from.Z + 64);
        try
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var yaw = (pawn.EyeAngles.Y + (Random.Shared.NextSingle() * 120 - 60)) * MathF.PI / 180;
                var far = 300 + Random.Shared.NextSingle() * 700;
                var ahead = Trace.TraceEndShape(eye, new Vector(eye.X + MathF.Cos(yaw) * far, eye.Y + MathF.Sin(yaw) * far, eye.Z), pawn, SightTrace);
                var reach = (ahead.DidHit() ? ahead.Fraction * far : far) - 40;
                if (reach < 250) continue;
                var x = eye.X + MathF.Cos(yaw) * reach;
                var y = eye.Y + MathF.Sin(yaw) * reach;
                if (FloorAt(x, y, eye.Z, eye.Z - 400) is not { } z) continue;
                var head = Trace.TraceEndShape(eye, new Vector(x, y, z + 60), pawn, SightTrace);
                if (!head.DidHit() || head.Fraction >= 0.97f) return new Vector(x, y, z);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Reaction drill trace failed");
        }
        return null;
    }

    // ---------------------------------------------------------------------------------------------
    // Sites
    // ---------------------------------------------------------------------------------------------

    private static bool FindSite(bool siteB, out Vector min, out Vector max)
    {
        min = max = new Vector(0, 0, 0);
        var site = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target")
            .FirstOrDefault(t => t.IsValid && t.IsBombSiteB == siteB);
        if (site?.Collision == null) return false;
        var o = site.AbsOrigin ?? new Vector(0, 0, 0);
        var lo = site.Collision.Mins;
        var hi = site.Collision.Maxs;
        min = new Vector(o.X + lo.X, o.Y + lo.Y, o.Z + lo.Z);
        max = new Vector(o.X + hi.X, o.Y + hi.Y, o.Z + hi.Z);
        return true;
    }

    /// <summary>Up to <paramref name="count"/> floor points inside the bomb site, at least 120 units apart where possible.</summary>
    private List<Vector> SiteSpots(bool siteB, int count)
    {
        var spots = new List<Vector>();
        if (count <= 0 || !FindSite(siteB, out var min, out var max)) return spots;
        try
        {
            for (var tries = 0; tries < 60 && spots.Count < count; tries++)
            {
                var x = min.X + Random.Shared.NextSingle() * (max.X - min.X);
                var y = min.Y + Random.Shared.NextSingle() * (max.Y - min.Y);
                if (FloorAt(x, y, max.Z, min.Z - 64) is not { } z) continue;
                var spot = new Vector(x, y, z);
                if (tries < 40 && spots.Any(s => Distance(s, spot) < 120)) continue;
                spots.Add(spot);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Bomb site trace failed");
        }
        return spots;
    }

    private static float? FloorAt(float x, float y, float top, float bottom)
    {
        var down = Trace.TraceEndShape(new Vector(x, y, top), new Vector(x, y, bottom), null, SightTrace);
        return down.DidHit() && down.Fraction > 0.01f ? down.EndPos.Z + 2 : null;
    }

    private static void PlaceAt(CCSPlayerController player, Vector spot, Vector? lookAt)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        var yaw = lookAt == null ? pawn.EyeAngles.Y : MathF.Atan2(lookAt.Y - spot.Y, lookAt.X - spot.X) * 180 / MathF.PI;
        pawn.Teleport(spot, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
    }

    /// <summary>Anchored players can't walk but still turn and shoot.</summary>
    private static void Anchor(CCSPlayerController player, bool on)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        var anchored = pawn.MoveType == MoveType_t.MOVETYPE_NONE;
        if (anchored == on) return;
        pawn.MoveType = pawn.ActualMoveType = on ? MoveType_t.MOVETYPE_NONE : MoveType_t.MOVETYPE_WALK;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    private void TellHumans(string message)
    {
        foreach (var player in Humans()) Reply(player, message);
    }

    // ---------------------------------------------------------------------------------------------
    // Speedometer
    // ---------------------------------------------------------------------------------------------

    private void OnTrainerTick()
    {
        if (_speedo.Count == 0) return;
        foreach (var (slot, track) in _speedo)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true, PawnIsAlive: true } || player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: not null } pawn) continue;
            var v = pawn.AbsVelocity;
            var speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            var onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
            var at = pawn.AbsOrigin;
            if (track.OnGround && !onGround)
            {
                // A jump within a few ticks of landing is a bunnyhop.
                track.Hops = Server.TickCount - track.LandedTick <= 3 ? track.Hops + 1 : 0;
                (track.FromX, track.FromY, track.Pre) = (at.X, at.Y, speed);
            }
            else if (!track.OnGround && onGround)
            {
                track.Last = MathF.Sqrt((at.X - track.FromX) * (at.X - track.FromX) + (at.Y - track.FromY) * (at.Y - track.FromY));
                track.LastPre = track.Pre;
                track.LandedTick = Server.TickCount;
            }
            track.OnGround = onGround;
            if (Server.TickCount % 4 == 0)
                player.PrintToCenterHtml($"<font class='fontSize-xl' color='#7CFC00'>{speed:0}</font> u/s<br>" +
                    $"jump {track.Last:0} u · pre {track.LastPre:0} · hops {track.Hops}");
        }
    }
}
