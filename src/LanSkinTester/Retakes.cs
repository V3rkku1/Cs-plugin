using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Retakes: every round the bomb is already planted on a random site (A or B), the terrorists start
/// on that site guarding it, and the counter-terrorists (you) start at CT spawn and must retake it.
/// Weapons come from the game's default-loadout cvars the launcher sets (no buying).
///   lst_retakes 0|1
/// </summary>
public partial class LanSkinTester
{
    private bool _retakes;

    /// <summary>This round's site: where the bomb goes and the terrorists start.</summary>
    private CBombTarget? _retakeSite;
    private Vector? _bombSpot;
    private int _retakeRound;

    private static readonly Vector PlayerMins = new(-16, -16, 0);
    private static readonly Vector PlayerMaxs = new(16, 16, 72);

    private static readonly TraceOptions WorldOnly = new()
    {
        InteractsAs = Contents.Player,
        InteractsWith = Contents.Solid | Contents.WorldGeometry | Contents.PlayerClip | Contents.Window | Contents.PhysicsProp,
        InteractsExclude = Contents.Player | Contents.Hitbox | Contents.Debris,
    };

    private void LoadRetakes()
    {
        RegisterEventHandler<EventRoundStart>(OnRetakeRoundStart);
        RegisterEventHandler<EventRoundFreezeEnd>(OnRetakeFreezeEnd);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _retakeSite = null;
            _bombSpot = null;
            _retakeRound = 0;
        });
    }

    [ConsoleCommand("lst_retakes", "Retakes mode: the bomb is planted on a random site every round, CTs retake it")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakesCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _retakes = cmd.GetArg(1) is "1" or "on" or "true";
    }

    private HookResult OnRetakeRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _retakeSite = null;
        _bombSpot = null;
        if (!_retakes || GameRules()?.WarmupPeriod == true) return HookResult.Continue;
        // Players respawn right after round_start.
        AddTimer(0.2f, PrepareRetake);
        return HookResult.Continue;
    }

    private HookResult OnRetakeFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_retakes || _retakeSite == null || _bombSpot == null) return HookResult.Continue;
        PlantBomb(_retakeSite, _bombSpot);
        return HookResult.Continue;
    }

    private static CCSGameRules? GameRules() =>
        Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;

    private void PrepareRetake()
    {
        try
        {
            var sites = Utilities.FindAllEntitiesByDesignerName<CBombTarget>("func_bomb_target").Where(s => s.IsValid).ToList();
            if (sites.Count == 0)
            {
                Server.PrintToChatAll($" {ChatColors.Red}[Retakes]{ChatColors.Default} This map has no bomb sites.");
                return;
            }
            _retakeRound++;
            var site = sites[Random.Shared.Next(sites.Count)];
            var name = site.IsBombSiteB ? "B" : "A";
            if (!SiteBox(site, out var mins, out var maxs)) return;

            var terrorists = Utilities.GetPlayers()
                .Where(p => p.IsValid && p.PawnIsAlive && p.Team == CsTeam.Terrorist && p.PlayerPawn.Value is { IsValid: true })
                .ToList();
            var spots = FindSpots(mins, maxs, terrorists.Count + 1, terrorists.FirstOrDefault()?.PlayerPawn.Value);
            if (spots.Count == 0)
            {
                Logger.LogWarning("Retakes: no free spot found on site {Site}", name);
                return;
            }

            _retakeSite = site;
            _bombSpot = spots[0];
            for (var i = 0; i < terrorists.Count; i++)
            {
                var spot = spots[Math.Min(i + 1, spots.Count - 1)];
                var yaw = Random.Shared.Next(360);
                terrorists[i].PlayerPawn.Value!.Teleport(spot, new QAngle(0, yaw, 0), new Vector(0, 0, 0));
            }

            var cts = Utilities.GetPlayers().Count(p => p.IsValid && p.PawnIsAlive && p.Team == CsTeam.CounterTerrorist);
            foreach (var player in Humans())
            {
                player.PrintToCenterHtml(
                    $"<font color='#ffb020' class='fontSize-xl'>RETAKE {name}</font><br>" +
                    $"<font color='#cccccc'>{terrorists.Count} T on {name} · {cts} CT · bomb gets planted when the round starts</font>", 5);
            }
            Server.PrintToChatAll($" {ChatColors.Gold}[Retakes]{ChatColors.Default} Round {_retakeRound}: retake site {ChatColors.Gold}{name}{ChatColors.Default} ({terrorists.Count}v{cts}).");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Retakes: could not set up the round");
        }
    }

    /// <summary>The site's area in world space.</summary>
    private bool SiteBox(CBombTarget site, out Vector mins, out Vector maxs)
    {
        mins = new Vector(0, 0, 0);
        maxs = new Vector(0, 0, 0);
        try
        {
            Trace.GetEntityWorldSpaceAABB(site, out mins, out maxs);
            if (maxs.X > mins.X && maxs.Y > mins.Y) return true;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Retakes: world bounds unavailable, using the collision box");
        }
        var origin = site.AbsOrigin ?? new Vector(0, 0, 0);
        var collision = site.Collision;
        if (collision == null) return false;
        mins = new Vector(origin.X + collision.Mins.X, origin.Y + collision.Mins.Y, origin.Z + collision.Mins.Z);
        maxs = new Vector(origin.X + collision.Maxs.X, origin.Y + collision.Maxs.Y, origin.Z + collision.Maxs.Z);
        return maxs.X > mins.X && maxs.Y > mins.Y;
    }

    /// <summary>
    /// Free standing spots on the site floor: the first is the site's middle (for the bomb), the rest
    /// spread around it, each with room for a player and in plain sight of the middle (not behind a wall).
    /// </summary>
    private List<Vector> FindSpots(Vector mins, Vector maxs, int count, CBaseEntity? ignore)
    {
        var spots = new List<Vector>();
        var center = new Vector((mins.X + maxs.X) / 2, (mins.Y + maxs.Y) / 2, maxs.Z);
        var centerFloor = Floor(center, mins.Z - 64, ignore);
        if (centerFloor == null) return spots;
        spots.Add(centerFloor);

        var radiusMax = Math.Clamp(Math.Min(maxs.X - mins.X, maxs.Y - mins.Y) / 2 + 160, 160, 520);
        for (var attempt = 0; attempt < 160 && spots.Count < count; attempt++)
        {
            var angle = Random.Shared.NextDouble() * Math.PI * 2;
            var radius = 60 + Random.Shared.NextDouble() * (radiusMax - 60);
            var x = (float)(centerFloor.X + Math.Cos(angle) * radius);
            var y = (float)(centerFloor.Y + Math.Sin(angle) * radius);

            // Clear line from the middle of the site at chest height (no wall between).
            var from = new Vector(centerFloor.X, centerFloor.Y, centerFloor.Z + 48);
            var to = new Vector(x, y, centerFloor.Z + 48);
            var sight = Trace.TraceEndShape(from, to, ignore!, WorldOnly);
            if (sight.DidHit() && sight.Fraction < 0.99f) continue;

            var floor = Floor(new Vector(x, y, centerFloor.Z + 48), centerFloor.Z - 160, ignore);
            if (floor == null) continue;
            if (spots.Any(s => Math.Abs(s.X - floor.X) < 40 && Math.Abs(s.Y - floor.Y) < 40)) continue;
            spots.Add(floor);
        }
        return spots;
    }

    /// <summary>The floor under a point (down to minZ) where a player fits, or null.</summary>
    private static Vector? Floor(Vector top, float minZ, CBaseEntity? ignore)
    {
        var down = Trace.TraceEndShape(top, new Vector(top.X, top.Y, minZ), ignore!, WorldOnly);
        if (!down.DidHit()) return null;
        var spot = new Vector(down.EndPos.X, down.EndPos.Y, down.EndPos.Z + 4);
        var room = Trace.TraceHullShape(spot, new Vector(spot.X, spot.Y, spot.Z + 1), PlayerMins, PlayerMaxs, ignore!, WorldOnly);
        return room.IsAllSolid || (room.DidHit() && room.Fraction < 1f) ? null : spot;
    }

    private void PlantBomb(CBombTarget site, Vector spot)
    {
        var carrier = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.PawnIsAlive && p.Team == CsTeam.Terrorist);
        try
        {
            var c4 = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");
            if (c4 == null) throw new InvalidOperationException("planted_c4 not created");
            var timer = ConVar.Find("mp_c4timer")?.GetPrimitiveValue<int>() ?? 40;
            c4.BombSite = site.IsBombSiteB ? 1 : 0;
            c4.HasExploded = false;
            c4.BombDefused = false;
            c4.CannotBeDefused = false;
            c4.BombTicking = true;
            c4.TimerLength = timer;
            c4.DispatchSpawn();
            c4.Teleport(spot, new QAngle(0, Random.Shared.Next(360), 0), new Vector(0, 0, 0));

            Server.NextFrame(() =>
            {
                if (!c4.IsValid) return;
                c4.C4Blow = Server.CurrentTime + timer;
                c4.BombTicking = true;
                Utilities.SetStateChanged(c4, "CPlantedC4", "m_flC4Blow");
                Utilities.SetStateChanged(c4, "CPlantedC4", "m_bBombTicking");
                site.BombPlantedHere = true;
                if (GameRules() is { } rules)
                {
                    rules.BombPlanted = true;
                    rules.BombDropped = false;
                }
                var planted = new EventBombPlanted(true) { Site = (int)site.Index };
                if (carrier != null) planted.Userid = carrier;
                planted.FireEvent(false);
            });
        }
        catch (Exception ex)
        {
            // Fallback: give the bomb to a terrorist on the site; bots plant it straight away.
            Logger.LogWarning(ex, "Retakes: could not place the bomb directly, giving it to a terrorist");
            carrier?.GiveNamedItem("weapon_c4");
        }
    }
}
