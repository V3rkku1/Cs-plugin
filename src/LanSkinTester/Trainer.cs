using System.Globalization;
using System.Text.Json;
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
/// shown), movement stats (speed, jump distance, strafe sync), a counter-strafe check on every shot,
/// lineups (saved ones by name, built-in ones by position: lst_goto) with the grenade's air time when it
/// pops. Every result also goes to training.jsonl (one JSON line each) for the launcher's Stats page,
/// with shots, hits and damage per round. Bots are never placed from per-map files: site spots are
/// random floor points inside the map's own bomb site volume.
/// </summary>
public partial class LanSkinTester
{
    private enum Drill { Off, Clear, Retake, Hold, Reflex }

    private Drill _drill;
    private bool _drillSiteB;
    private int _drillWins, _drillRounds, _lineupIndex;
    private float _reflexShownAt = -1;
    private int _reflexShow; // which pop-up this is, so an old "too slow" timer leaves a newer one alone
    private readonly List<float> _reflexTimes = new();
    private readonly Dictionary<int, JumpTrack> _speedo = new();
    private readonly Dictionary<int, Vector> _anchors = new();
    private bool _nadeInfo;
    private readonly Dictionary<int, float> _thrownAt = new();
    // Counter-strafe check: the last tick each player held a movement key.
    private readonly Dictionary<int, int> _counterStrafe = new();
    private readonly Dictionary<int, float> _lastShot = new();
    // This round's shots, hits and damage per human, written to training.jsonl when the round ends.
    private readonly Dictionary<int, (int Shots, int Hits, int Damage)> _roundAim = new();

    private sealed class JumpTrack
    {
        public bool OnGround = true;
        public float FromX, FromY, FromZ, Pre, Max, LastYaw, Last, LastPre, LastMax;
        public int Hops, LandedTick, TurnTicks, SyncTicks, Strafes, LastSide, LastSync, LastStrafes;
    }

    private void LoadTrainer()
    {
        RegisterListener<Listeners.OnTick>(OnTrainerTick);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _drill = Drill.Off; // a new map may be a mode the drill can't run in
            _speedo.Clear();
            _anchors.Clear();
            _thrownAt.Clear();
            _counterStrafe.Clear();
            _lastShot.Clear();
            _roundAim.Clear();
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
        if (lineup.Key == null)
        {
            TellHumans($"No lineup named \"{name}\" on this map.");
            return;
        }
        foreach (var player in Humans())
        {
            GoToThrow(player, lineup.Value);
            Reply(player, $"Lineup \"{name}\" ({lineup.Value.Weapon.Replace("weapon_", "")}): throw!");
        }
    }

    [ConsoleCommand("lst_goto", "Send every human to a lineup spot with a grenade: lst_goto x y z pitch yaw smoke|flash|molotov|he [text]")]
    [CommandHelper(minArgs: 6, usage: "x y z pitch yaw smoke|flash|molotov|he [text]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnGotoCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var n = new float[5];
        for (var i = 0; i < 5; i++)
            if (!float.TryParse(cmd.GetArg(i + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return;
        var nade = cmd.GetArg(6).ToLowerInvariant();
        var text = string.Join(' ', Enumerable.Range(7, Math.Max(0, cmd.ArgCount - 7)).Select(cmd.GetArg));
        _nadeInfo = true;
        foreach (var player in Humans())
        {
            var weapon = nade switch
            {
                "flash" => "weapon_flashbang",
                "he" => "weapon_hegrenade",
                "molotov" => player.Team == CsTeam.CounterTerrorist ? "weapon_incgrenade" : "weapon_molotov",
                _ => "weapon_smokegrenade",
            };
            GoToThrow(player, new ThrownGrenade(new Spot(n[0], n[1], n[2], n[3], n[4]), weapon));
            if (text.Length > 0) Reply(player, text);
        }
    }

    [ConsoleCommand("lst_nadeinfo", "Tell the thrower how long each grenade flew: 1 on, 0 off")]
    [CommandHelper(minArgs: 1, usage: "1|0", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnNadeInfoCommand(CCSPlayerController? caller, CommandInfo cmd) => _nadeInfo = cmd.GetArg(1) == "1";

    [ConsoleCommand("lst_cstrafe", "Counter-strafe check on every shot for every human: 1 on, 0 off")]
    [CommandHelper(minArgs: 1, usage: "1|0", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnCounterStrafeCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _counterStrafe.Clear();
        if (cmd.GetArg(1) == "1") foreach (var player in Humans()) _counterStrafe[player.Slot] = 0;
    }

    [ConsoleCommand("css_cs", "Toggle the counter-strafe check on your shots")]
    public void OnCounterStrafeChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (!_counterStrafe.Remove(player!.Slot)) _counterStrafe[player.Slot] = 0;
        Reply(player, _counterStrafe.ContainsKey(player.Slot) ? "Counter-strafe check on: stop before you shoot." : "Counter-strafe check off.");
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
            _anchors.Clear();
            if (wasReflex) Server.ExecuteCommand("bot_dont_shoot 0; mp_ignore_round_win_conditions 0");
            TellHumans("Drill off. Apply Changes on the launcher's Live Server page brings back your bots and rules.");
            return;
        }

        // Bombs, rounds and buying: only the classic modes (Casual, Competitive, Wingman) have them.
        if (drill != Drill.Reflex && CvarInt("game_type") != 0)
        {
            _drill = Drill.Off;
            TellHumans("Site drills need Competitive or Casual (pick it on the Play page), not this mode.");
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
            player.ChangeTeam(humanTeam); // not SwitchTeam: its native call crashed the server (0xC0000005)
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
                    Anchor(bots[i], spots[i]);
                }
            }
            else if (_drill == Drill.Retake) bots.ForEach(b => Anchor(b));
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
        _anchors.Clear(); // everyone spawned somewhere new
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
        _anchors.Clear();
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
        foreach (var player in Humans()) LogTraining(player, "drill", ("drill", _drill.ToString().ToLowerInvariant()), ("won", @event.Winner == (int)humanTeam));
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
            LogTraining(attacker, "reflex", ("ms", MathF.Round(ms)));
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
        // The bot is still joining, or you are dead or somewhere with no room: try again shortly.
        void Retry() => AddTimer(1f, () => ShowReflexBot(), TimerFlags.STOP_ON_MAPCHANGE);
        if (bot == null || human?.PlayerPawn.Value is not { IsValid: true, AbsOrigin: not null } pawn)
        {
            Retry();
            return;
        }
        if (!bot.PawnIsAlive)
        {
            if (respawned)
            {
                Retry();
                return;
            }
            bot.Respawn();
            AddTimer(0.2f, () => ShowReflexBot(respawned: true), TimerFlags.STOP_ON_MAPCHANGE);
            return;
        }
        if (ReflexSpot(pawn) is not { } spot)
        {
            Retry();
            return;
        }
        PlaceAt(bot, spot, pawn.AbsOrigin);
        Anchor(bot, spot);
        if (bot.PlayerPawn.Value is { IsValid: true } botPawn)
        {
            botPawn.Health = 1; // any hit counts
            Utilities.SetStateChanged(botPawn, "CBaseEntity", "m_iHealth");
        }
        _reflexShownAt = Server.CurrentTime;
        var show = ++_reflexShow;
        AddTimer(3f, () =>
        {
            if (_drill != Drill.Reflex || show != _reflexShow || _reflexShownAt < 0) return;
            TellHumans("Too slow (3 s). Next one…");
            ShowReflexBot();
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    /// <summary>A visible floor point 300 to 1000 units ahead of the player, within 45° of where they look (on screen).</summary>
    private Vector? ReflexSpot(CCSPlayerPawn pawn)
    {
        var from = pawn.AbsOrigin!;
        var eye = new Vector(from.X, from.Y, from.Z + 64);
        try
        {
            for (var tries = 0; tries < 12; tries++)
            {
                var yaw = (pawn.EyeAngles.Y + (Random.Shared.NextSingle() * 90 - 45)) * MathF.PI / 180;
                var far = 300 + Random.Shared.NextSingle() * 700;
                var ahead = Trace.TraceEndShape(eye, new Vector(eye.X + MathF.Cos(yaw) * far, eye.Y + MathF.Sin(yaw) * far, eye.Z), pawn, SightTrace);
                var reach = (ahead.DidHit() ? ahead.Fraction * far : far) - 40;
                if (reach < 250) continue;
                var x = eye.X + MathF.Cos(yaw) * reach;
                var y = eye.Y + MathF.Sin(yaw) * reach;
                if (FloorAt(x, y, eye.Z, eye.Z - 400, pawn) is not { } z) continue;
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

    private static bool FindSite(bool siteB, out Vector min, out Vector max) => FindSite(siteB, out min, out max, out _);

    private static bool FindSite(bool siteB, out Vector min, out Vector max, out CBombTarget? site)
    {
        min = max = new Vector(0, 0, 0);
        site = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target")
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
        if (count <= 0 || !FindSite(siteB, out var min, out var max, out var site)) return spots;
        try
        {
            for (var tries = 0; tries < 60 && spots.Count < count; tries++)
            {
                var x = min.X + Random.Shared.NextSingle() * (max.X - min.X);
                var y = min.Y + Random.Shared.NextSingle() * (max.Y - min.Y);
                if (FloorAt(x, y, max.Z, min.Z - 64, site!) is not { } z) continue;
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

    /// <summary>The floor under a point. <paramref name="ignore"/> must be a real entity: no null pointers into the engine's trace.</summary>
    private static float? FloorAt(float x, float y, float top, float bottom, CBaseEntity ignore)
    {
        var down = Trace.TraceEndShape(new Vector(x, y, top), new Vector(x, y, bottom), ignore, SightTrace);
        return down.DidHit() && down.Fraction > 0.01f ? down.EndPos.Z + 2 : null;
    }

    private static void PlaceAt(CCSPlayerController player, Vector spot, Vector? lookAt)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn) return;
        var yaw = lookAt == null ? pawn.EyeAngles.Y : MathF.Atan2(lookAt.Y - spot.Y, lookAt.X - spot.X) * 180 / MathF.PI;
        pawn.Teleport(spot, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
    }

    /// <summary>
    /// Anchored players can't walk but still turn and shoot: every tick they are put back on their spot
    /// (HoldAnchors). Only Teleport, no move type change: writing a bot's move type may crash the server.
    /// </summary>
    private void Anchor(CCSPlayerController player, Vector? spot = null)
    {
        // A copy: AbsOrigin is the engine's live value and moves with the pawn.
        if ((spot ?? player.PlayerPawn.Value?.AbsOrigin) is { } at) _anchors[player.Slot] = new Vector(at.X, at.Y, at.Z);
    }

    private void HoldAnchors()
    {
        foreach (var (slot, spot) in _anchors)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true, PawnIsAlive: true } || player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: { } at } pawn) continue;
            if (MathF.Abs(at.X - spot.X) + MathF.Abs(at.Y - spot.Y) > 1f)
                pawn.Teleport(spot, null, new Vector(0, 0, 0));
        }
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
        if (_anchors.Count > 0) HoldAnchors();
        foreach (var slot in _counterStrafe.Keys.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is { IsValid: true } && (player.Buttons & MoveKeys) != 0) _counterStrafe[slot] = Server.TickCount;
        }
        foreach (var (slot, track) in _speedo)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true, PawnIsAlive: true } || !IsHumanPlayer(player)
                || player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: not null } pawn) continue;
            var v = pawn.AbsVelocity;
            var speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            var onGround = (pawn.Flags & (uint)PlayerFlags.FL_ONGROUND) != 0;
            var at = pawn.AbsOrigin;
            var yaw = pawn.EyeAngles.Y;
            if (track.OnGround && !onGround)
            {
                // A jump within a few ticks of landing is a bunnyhop.
                track.Hops = Server.TickCount - track.LandedTick <= 3 ? track.Hops + 1 : 0;
                (track.FromX, track.FromY, track.FromZ, track.Pre, track.Max) = (at.X, at.Y, at.Z, speed, speed);
                track.TurnTicks = track.SyncTicks = track.Strafes = track.LastSide = 0;
            }
            else if (!onGround)
            {
                // Strafe sync: turning left while holding only A, or right while holding only D.
                var turn = yaw - track.LastYaw;
                if (turn > 180) turn -= 360;
                if (turn < -180) turn += 360;
                var left = (player.Buttons & PlayerButtons.Moveleft) != 0;
                var right = (player.Buttons & PlayerButtons.Moveright) != 0;
                var side = left == right ? 0 : left ? 1 : -1;
                if (side != 0 && side != track.LastSide) { track.Strafes++; track.LastSide = side; }
                if (MathF.Abs(turn) > 0.01f)
                {
                    track.TurnTicks++;
                    if ((turn > 0 && side == 1) || (turn < 0 && side == -1)) track.SyncTicks++;
                }
                track.Max = MathF.Max(track.Max, speed);
            }
            if (!track.OnGround && onGround)
            {
                track.Last = MathF.Sqrt((at.X - track.FromX) * (at.X - track.FromX) + (at.Y - track.FromY) * (at.Y - track.FromY));
                (track.LastPre, track.LastMax, track.LastStrafes) = (track.Pre, track.Max, track.Strafes);
                track.LastSync = track.TurnTicks == 0 ? 0 : track.SyncTicks * 100 / track.TurnTicks;
                track.LandedTick = Server.TickCount;
                // Only real jumps count for the stats: landing about as high as you took off (not off a box).
                if (track.Last >= 150 && MathF.Abs(at.Z - track.FromZ) < 8)
                    LogTraining(player, "jump", ("dist", MathF.Round(track.Last)), ("pre", MathF.Round(track.LastPre)), ("sync", track.LastSync));
            }
            track.OnGround = onGround;
            track.LastYaw = yaw;
            if (Server.TickCount % 4 == 0)
                player.PrintToCenterHtml($"<font class='fontSize-xl' color='#7CFC00'>{speed:0}</font> u/s · hops {track.Hops}<br>" +
                    $"<font class='fontSize-s'>jump {track.Last:0} u · pre {track.LastPre:0} · max {track.LastMax:0} · " +
                    $"gain {track.LastMax - track.LastPre:+0;-0;0} · sync {track.LastSync}% · {track.LastStrafes} strafes</font>");
        }
    }

    private const PlayerButtons MoveKeys = PlayerButtons.Forward | PlayerButtons.Back | PlayerButtons.Moveleft | PlayerButtons.Moveright;

    [GameEventHandler]
    public HookResult OnTrainerWeaponFire(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        var weapon = @event.Weapon ?? "";
        if (player is not { IsValid: true } || !IsHumanPlayer(player)) return HookResult.Continue;
        if (IsGrenade(weapon))
        {
            if (_nadeInfo) _thrownAt[player.Slot] = Server.CurrentTime;
            return HookResult.Continue;
        }
        if (weapon.Contains("knife") || weapon.Contains("bayonet") || weapon.Contains("taser")) return HookResult.Continue;
        if (_drill != Drill.Reflex && !HackerSlots().Contains(player.Slot))
        {
            var aim = _roundAim.GetValueOrDefault(player.Slot);
            _roundAim[player.Slot] = aim with { Shots = aim.Shots + 1 };
        }
        // Only the first shot of a burst: the rest of a spray says nothing about stopping.
        var firstShot = Server.CurrentTime - _lastShot.GetValueOrDefault(player.Slot, -1f) > 0.4f;
        _lastShot[player.Slot] = Server.CurrentTime;
        if (!firstShot || !_counterStrafe.TryGetValue(player.Slot, out var lastMove)
            || player.PlayerPawn.Value is not { IsValid: true } pawn) return HookResult.Continue;
        var v = pawn.AbsVelocity;
        var speed = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
        // shortcut: one limit for every gun (34% of a rifle's 215 u/s), not each weapon's own max speed.
        var stopped = speed <= 73;
        var held = (player.Buttons & MoveKeys) != 0;
        var since = held ? "still holding a move key" : lastMove == 0 ? "standing"
            : $"{(Server.TickCount - lastMove) * Server.TickInterval * 1000:0} ms after letting go";
        LogTraining(player, "shot", ("ok", stopped));
        player.PrintToCenter(stopped ? $"Accurate · {speed:0} u/s · {since}" : $"MOVING · {speed:0} u/s · {since}");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnTrainerSmoke(EventSmokegrenadeDetonate @event, GameEventInfo info) => NadePopped(@event.Userid, "Smoke");

    [GameEventHandler]
    public HookResult OnTrainerFlash(EventFlashbangDetonate @event, GameEventInfo info) => NadePopped(@event.Userid, "Flash");

    [GameEventHandler]
    public HookResult OnTrainerHe(EventHegrenadeDetonate @event, GameEventInfo info) => NadePopped(@event.Userid, "HE");

    [GameEventHandler]
    public HookResult OnTrainerMolotovPop(EventMolotovDetonate @event, GameEventInfo info) => NadePopped(@event.Userid, "Molotov");

    private HookResult NadePopped(CCSPlayerController? player, string what)
    {
        if (!_nadeInfo || player is not { IsValid: true } || !_thrownAt.Remove(player.Slot, out var at)) return HookResult.Continue;
        Reply(player, $"{what} popped after {Server.CurrentTime - at:0.0} s. !rethrow to try again, !savenade name to keep it.");
        return HookResult.Continue;
    }

    // ---------------------------------------------------------------------------------------------
    // Training history (the launcher's Stats page)
    // ---------------------------------------------------------------------------------------------

    [GameEventHandler]
    public HookResult OnTrainingDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        // Reaction drill hits are logged as reaction times, and aimbot kills don't count.
        if (_drill == Drill.Reflex) return HookResult.Continue;
        var victim = @event.Userid;
        var attacker = @event.Attacker;
        if (victim is not { IsValid: true }) return HookResult.Continue;
        if (attacker is { IsValid: true } && IsEnemy(attacker, victim) && IsHumanPlayer(attacker) && !HackerSlots().Contains(attacker.Slot))
            LogTraining(attacker, "kill", ("hs", @event.Headshot));
        if (IsHumanPlayer(victim)) LogTraining(victim, "death");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnTrainingHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        var weapon = @event.Weapon ?? "";
        if (_drill == Drill.Reflex || attacker is not { IsValid: true } || victim is not { IsValid: true } || !IsHumanPlayer(attacker)
            || !IsEnemy(attacker, victim) || HackerSlots().Contains(attacker.Slot) || IsGrenade(weapon) || weapon == "inferno") return HookResult.Continue;
        var aim = _roundAim.GetValueOrDefault(attacker.Slot);
        _roundAim[attacker.Slot] = aim with { Hits = aim.Hits + 1, Damage = aim.Damage + Math.Max(0, @event.DmgHealth) };
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnTrainingRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        // One line per human per round, so damage per round counts the quiet rounds too.
        if (_drill != Drill.Reflex)
            foreach (var player in Humans().Where(p => p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
            {
                var aim = _roundAim.GetValueOrDefault(player.Slot);
                LogTraining(player, "round", ("shots", aim.Shots), ("hits", Math.Min(aim.Hits, aim.Shots)), ("dmg", aim.Damage));
            }
        _roundAim.Clear();
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnTrainingDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        if (@event.Userid is not { } player) return HookResult.Continue;
        _speedo.Remove(player.Slot);
        _anchors.Remove(player.Slot);
        _counterStrafe.Remove(player.Slot);
        _lastShot.Remove(player.Slot);
        _roundAim.Remove(player.Slot);
        _thrownAt.Remove(player.Slot);
        return HookResult.Continue;
    }

    private string TrainingFile => Path.Combine(ModuleDirectory, "training.jsonl");

    private void LogTraining(CCSPlayerController player, string kind, params (string Key, object Value)[] values)
    {
        var line = new Dictionary<string, object>
        {
            ["t"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["k"] = kind,
            ["p"] = player.PlayerName,
        };
        foreach (var (key, value) in values) line[key] = value;
        try { File.AppendAllText(TrainingFile, JsonSerializer.Serialize(line) + "\n"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* stats are a nice-to-have */ }
    }
}
