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
/// More Trainer drills: prefire (time to clear bots that don't shoot), clutch (the bomb is already down,
/// retake it alone), a spray score (how far each bullet of a spray lands from the first) and movement
/// courses (a start and a finish you set per map, timed, with a best time). Per-gun shots and hits go to
/// training.jsonl for the Stats page.
/// </summary>
public partial class LanSkinTester
{
    /// <summary>Drills where bots don't shoot back: their kills don't count for headshots, K/D or accuracy.</summary>
    private bool PassiveBots => _drill is Drill.Reflex or Drill.Prefire;

    private float _prefireStart = -1;
    private readonly Dictionary<(int Slot, string Gun), (int Shots, int Hits)> _roundGuns = new();

    private static string WeaponName(string weapon) =>
        weapon.StartsWith("weapon_", StringComparison.OrdinalIgnoreCase) ? weapon[7..].ToLowerInvariant() : weapon.ToLowerInvariant();

    // ---------------------------------------------------------------------------------------------
    // Prefire: the clock runs from the freeze end until the last bot is down.
    // ---------------------------------------------------------------------------------------------

    [GameEventHandler]
    public HookResult OnPrefireDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if (_drill != Drill.Prefire || _prefireStart < 0 || @event.Userid is not { IsValid: true } victim || !IsBotPlayer(victim))
            return HookResult.Continue;
        // The victim may still count as alive during its own death event.
        var left = Utilities.GetPlayers().Count(p => p.IsValid && p.PawnIsAlive && IsBotPlayer(p) && p.Slot != victim.Slot);
        if (left > 0) return HookResult.Continue;
        var ms = (Server.CurrentTime - _prefireStart) * 1000f;
        _prefireStart = -1;
        foreach (var player in Humans())
        {
            LogTraining(player, "prefire", ("ms", MathF.Round(ms)));
            Reply(player, $"Site cleared in {ms / 1000f:0.00} s.");
        }
        return HookResult.Continue;
    }

    // ---------------------------------------------------------------------------------------------
    // Clutch: the bots' bomb is taken away and planted on the site, the bots guard it, you retake.
    // ---------------------------------------------------------------------------------------------

    private void StartClutch(List<CCSPlayerController> bots)
    {
        var spots = SiteSpots(_drillSiteB, bots.Count + 1);
        if (spots.Count == 0) return;
        foreach (var bot in bots) bot.RemoveItemByDesignerName("weapon_c4");
        for (var i = 0; i < bots.Count && i + 1 < spots.Count; i++) PlaceAt(bots[i], spots[i + 1], null);
        try
        {
            var c4 = Utilities.CreateEntityByName<CPlantedC4>("planted_c4");
            if (c4 == null) return;
            c4.BombTicking = true;
            c4.BombSite = _drillSiteB ? 1 : 0;
            c4.TimerLength = 40;
            c4.C4Blow = Server.CurrentTime + 40;
            c4.Teleport(spots[0], new QAngle(0, 0, 0), new Vector(0, 0, 0));
            c4.DispatchSpawn();
            var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
            if (rules != null) rules.BombPlanted = true;
            // Tells the bots (and the HUD) the bomb is down, as the planter.
            if (bots.FirstOrDefault() is { } planter) new EventBombPlanted(true) { Userid = planter, Site = c4.BombSite }.FireEvent(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not plant the clutch drill's bomb");
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Spray score: each bullet's angle from the first bullet of the spray, averaged.
    // ---------------------------------------------------------------------------------------------

    private bool _sprayAll;
    private readonly Dictionary<int, SprayTrack> _spray = new();

    private sealed class SprayTrack
    {
        public Vector? First;
        public float Last, TotalAngle;
        public int Bullets;
    }

    [ConsoleCommand("lst_spray", "Spray score after every spray for every human: 1 on, 0 off")]
    [CommandHelper(minArgs: 1, usage: "1|0", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSprayCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _sprayAll = cmd.GetArg(1) == "1";
        _spray.Clear();
    }

    [ConsoleCommand("css_spray", "Toggle the spray score")]
    public void OnSprayChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        _sprayAll = !_sprayAll;
        _spray.Clear();
        Reply(player!, _sprayAll ? "Spray score on: spray 8+ bullets at a wall." : "Spray score off.");
    }

    [GameEventHandler]
    public HookResult OnSprayImpact(EventBulletImpact @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (!_sprayAll || player is not { IsValid: true } || !IsHumanPlayer(player)
            || player.PlayerPawn.Value is not { IsValid: true, AbsOrigin: { } at } pawn) return HookResult.Continue;
        // shortcut: a wallbang reports an impact on each surface, so it counts twice; fine for spraying at a wall.
        var eye = new Vector(at.X, at.Y, at.Z + EyeHeight(pawn));
        var dir = Normalize(new Vector(@event.X - eye.X, @event.Y - eye.Y, @event.Z - eye.Z));
        var now = Server.CurrentTime;
        if (!_spray.TryGetValue(player.Slot, out var track) || now - track.Last > 0.35f)
            _spray[player.Slot] = track = new SprayTrack { First = dir };
        else
            track.TotalAngle += MathF.Acos(Math.Clamp(dir.X * track.First!.X + dir.Y * track.First.Y + dir.Z * track.First.Z, -1f, 1f)) * 180 / MathF.PI;
        track.Last = now;
        track.Bullets++;
        var bullets = track.Bullets;
        AddTimer(0.4f, () =>
        {
            if (!player.IsValid || !_spray.TryGetValue(player.Slot, out var t) || t != track || t.Bullets != bullets) return;
            _spray.Remove(player.Slot);
            if (bullets < 8) return;
            var spread = t.TotalAngle / (bullets - 1);
            LogTraining(player, "spray", ("deg", MathF.Round(spread, 2)), ("bullets", bullets));
            Reply(player, $"Spray: {bullets} bullets, {spread:0.00}° from your first bullet on average " +
                (spread < 1.5f ? "(great)" : spread < 3f ? "(good)" : "(pull down more)"));
        }, TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private static Vector Normalize(Vector v)
    {
        var length = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return length < 0.001f ? new Vector(1, 0, 0) : new Vector(v.X / length, v.Y / length, v.Z / length);
    }

    // ---------------------------------------------------------------------------------------------
    // Movement courses: one start and one finish per map (courses/<map>.json), with each player's best.
    // ---------------------------------------------------------------------------------------------

    private sealed class Course
    {
        public float[]? Start { get; set; }
        public float[]? Finish { get; set; }
        public Dictionary<string, float> Best { get; set; } = new();
    }

    private const float CourseRadius = 64;
    private Course? _courseOnMap;
    private string _courseMap = "";
    // Per player: when they left the start (the clock), or -1 while standing in it.
    private readonly Dictionary<int, float> _course = new();

    private string CourseFile() => Path.Combine(ModuleDirectory, "courses",
        new string(Server.MapName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + ".json");

    private Course LoadCourse()
    {
        if (_courseMap == Server.MapName && _courseOnMap != null) return _courseOnMap;
        _courseMap = Server.MapName;
        _course.Clear();
        try { _courseOnMap = JsonSerializer.Deserialize<Course>(File.ReadAllText(CourseFile())) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { _courseOnMap = new(); }
        return _courseOnMap;
    }

    private void SaveCourse(Course course)
    {
        var file = CourseFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(course));
    }

    [ConsoleCommand("lst_course", "Movement course on this map: lst_course start|finish|clear (where the first human stands)")]
    [CommandHelper(minArgs: 1, usage: "start|finish|clear", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnCourseCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (Humans().FirstOrDefault(p => p.PawnIsAlive) is { } player) SetCourse(player, cmd.GetArg(1));
    }

    [ConsoleCommand("css_course", "Movement course: !course start|finish|clear")]
    [CommandHelper(minArgs: 1, usage: "start|finish|clear")]
    public void OnCourseChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player) || !Alive(player!)) return;
        SetCourse(player!, cmd.GetArg(1));
    }

    private void SetCourse(CCSPlayerController player, string what)
    {
        var course = LoadCourse();
        var at = player.PlayerPawn.Value?.AbsOrigin;
        var point = at == null ? null : new[] { at.X, at.Y, at.Z };
        switch (what.ToLowerInvariant())
        {
            case "start": course.Start = point; course.Best.Clear(); break;
            case "finish": course.Finish = point; course.Best.Clear(); break;
            default: course.Start = course.Finish = null; course.Best.Clear(); break;
        }
        _course.Clear();
        SaveCourse(course);
        TellHumans(course.Start != null && course.Finish != null ? "Course ready: leave the start to start the clock, reach the finish to stop it."
            : course.Start != null ? "Course start set. Now stand on the finish: !course finish"
            : course.Finish != null ? "Course finish set. Now stand on the start: !course start" : "Course removed.");
    }

    private void TickCourses()
    {
        if (_courseMap != Server.MapName) LoadCourse();
        if (_courseOnMap is not { Start: { } start, Finish: { } finish } course) return;
        foreach (var player in Humans())
        {
            if (!player.PawnIsAlive || player.PlayerPawn.Value?.AbsOrigin is not { } at) continue;
            var atStart = Near(at, start);
            if (atStart) _course[player.Slot] = -1;
            else if (_course.TryGetValue(player.Slot, out var since))
            {
                var now = Server.CurrentTime;
                if (since < 0) _course[player.Slot] = since = now;
                if (Near(at, finish))
                {
                    _course.Remove(player.Slot);
                    var seconds = now - since;
                    var best = course.Best.GetValueOrDefault(player.PlayerName, float.MaxValue);
                    if (seconds < best)
                    {
                        course.Best[player.PlayerName] = seconds;
                        SaveCourse(course);
                    }
                    LogTraining(player, "course", ("ms", MathF.Round(seconds * 1000)));
                    Reply(player, seconds < best ? $"Course: {seconds:0.00} s. New best!" : $"Course: {seconds:0.00} s (best {best:0.00} s).");
                }
                else if (Server.TickCount % 16 == 0) player.PrintToCenter($"Course {now - since:0.0} s");
            }
        }
    }

    private static bool Near(Vector at, float[] point) =>
        MathF.Abs(at.X - point[0]) < CourseRadius && MathF.Abs(at.Y - point[1]) < CourseRadius && MathF.Abs(at.Z - point[2]) < CourseRadius;
}
