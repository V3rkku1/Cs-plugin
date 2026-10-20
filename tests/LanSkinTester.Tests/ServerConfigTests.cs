using System.Reflection;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace LanSkinTester.Tests;

/// <summary>
/// What the launcher writes for the server (every quick start, with and without Bot Improver, hacker mode, Flying
/// Scoutsman, a Workshop map), from launcher-commands.json. The launcher's own tests (cargo test in src/Launcher)
/// keep that file current.
/// </summary>
public sealed class LauncherCommands
{
    public List<string> Commands { get; set; } = [];
    public string AllPowers { get; set; } = "";
    public List<string> AimKeys { get; set; } = [];
    public List<string> AimStyles { get; set; } = [];
    public List<string> AimBones { get; set; } = [];
    public List<string> AimTargets { get; set; } = [];
    /// <summary>Plugin retakes: [max players, bots, the cfg's bot_quota].</summary>
    public List<int[]> RetakeQuotas { get; set; } = [];

    private static LauncherCommands? _loaded;

    public static LauncherCommands Load() => _loaded ??= System.Text.Json.JsonSerializer.Deserialize<LauncherCommands>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "launcher-commands.json")),
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
}

/// <summary>
/// Every line the launcher writes for the server must reach something that exists there: our plugin, a
/// plugin the launcher downloads, or CS2 itself. A name that is none of these does nothing (Bot Improver's
/// weapon and Flying Scoutsman "commands" were cfg aliases the launcher never installed).
/// </summary>
public class ServerConfigTests
{
    private record PluginCommand(int MinArgs, bool ServerOnly);

    private static readonly Dictionary<string, PluginCommand> Plugin = typeof(global::LanSkinTester.LanSkinTester)
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        .SelectMany(m => m.GetCustomAttributes<ConsoleCommandAttribute>().Select(c => (c.Command, Helper: m.GetCustomAttribute<CommandHelperAttribute>())))
        .ToDictionary(x => x.Command, x => new PluginCommand(x.Helper?.MinArgs ?? 0, x.Helper?.WhoCanExcecute == CommandUsage.SERVER_ONLY));

    /// <summary>Commands from the plugins the launcher downloads (checked against their sources).</summary>
    private static readonly HashSet<string> ThirdParty =
    [
        "bh_namesource",   // XBribo/CS2-Bot-Hider: BotHiderImpl
        "bot_aim",         // ed0ard/CS2-Bullseye-Bot (Bot Improver's BotAimImprover)
        "bot_nades",       // ed0ard/CS2-Bot-NadeSystem (Bot Improver's NadeSystem)
        "br_reroll",       // ed0ard/CS2-Bot-Randomizer (Bot Improver's BotRandomizer)
        "retakes_enabled", // B3none/cs2-retakes: FakeConVar
    ];

    /// <summary>CS2's own commands and cvars the launcher uses.</summary>
    private static readonly HashSet<string> Engine =
    [
        "sv_hibernate_when_empty", "sv_lan", "sv_password", "sv_cheats", "sv_infinite_ammo", "sv_gravity", "sv_airaccelerate",
        "sv_autobunnyhopping", "sv_enablebunnyhopping", "sv_skirmish_id", "sv_maxspeed", "sv_friction",
        "weapon_recoil_scale", "weapon_recoil_view_punch_extra", "weapon_recoil_suppression_shots", "weapon_accuracy_nospread",
        "bot_difficulty", "bot_quota", "bot_quota_mode", "bot_join_after_player", "bot_join_team", "bot_kick", "bot_dont_shoot",
        "bot_knives_only", "bot_pistols_only", "bot_snipers_only", "bot_allow_snipers", "bot_allow_shotguns",
        "bot_allow_sub_machine_guns", "bot_allow_machine_guns", "bot_allow_rifles", "bot_allow_pistols", "bot_loadout",
        "bot_defense_rush_chance", "bot_add_ct", "bot_add_t",
        "mp_teammates_are_enemies", "mp_maxmoney", "mp_startmoney", "mp_afterroundmoney", "mp_buy_anywhere", "mp_buytime",
        "mp_freezetime", "mp_ignore_round_win_conditions", "mp_roundtime", "mp_roundtime_defuse", "mp_roundtime_hostage",
        "mp_respawn_on_death_t", "mp_respawn_on_death_ct", "mp_friendlyfire", "mp_items_prohibited", "mp_limitteams",
        "mp_retake_t_count", "mp_retake_ct_count", "mp_c4timer", "mp_warmup_end", "mp_restartgame", "mp_free_armor",
        "mp_give_player_c4", "mp_default_team_winner_no_objective", "mp_ct_default_primary", "mp_t_default_primary",
        "mp_ct_default_secondary", "mp_t_default_secondary",
        "nav_path_fixup_gap_segments", "game_type", "game_mode", "map", "changelevel", "host_workshop_map", "exec",
    ];

    /// <summary>Splits a console line like the engine: spaces separate, quotes group.</summary>
    internal static List<string> Tokens(string line) =>
        Regex.Matches(line, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();

    private static void AssertKnown(string line)
    {
        var tokens = Tokens(line);
        var name = tokens[0];
        if (Plugin.TryGetValue(name, out var command))
        {
            Assert.True(tokens.Count - 1 >= command.MinArgs, $"\"{line}\": {name} needs {command.MinArgs} argument(s)");
            return;
        }
        Assert.True(ThirdParty.Contains(name) || Engine.Contains(name), $"\"{line}\": {name} doesn't exist on the server");
    }

    [Fact]
    public void EveryLauncherCommandIsReal()
    {
        var commands = LauncherCommands.Load().Commands;
        Assert.True(commands.Count > 50, "launcher-commands.json is missing or empty");
        foreach (var line in commands) AssertKnown(line);
    }

    [Fact]
    public void LiveCommandsAreReal()
    {
        // Live Server buttons (live.js).
        foreach (var line in new[]
                 {
                     "bot_add_ct", "bot_add_t", "bot_kick", "mp_warmup_end", "mp_restartgame 1", "lst_armor", "lst_nades", "lst_clear",
                     "lst_god", "lst_noclip", "lst_respawn", "lst_refresh", "lst_rethrow", "lst_spot save", "lst_spot load", "lst_swap",
                     "lst_endround ct", "lst_endround t", "lst_view first", "lst_view third", "lst_pause on", "lst_pause off",
                     "br_reroll", "bot_aim mixed", "bot_nades normal", "lst_give weapon_ak47", "lst_show_damage 1",
                     "lst_shared_loadout 1", "sv_password \"x\"",
                 })
            AssertKnown(line);
    }

    [Fact]
    public void LauncherOnlyCommandsAreServerOnly()
    {
        // Players must not be able to run what the launcher drives (lst_*), except the aimbot key binds.
        foreach (var (name, command) in Plugin.Where(c => c.Key.StartsWith("lst_")))
            Assert.True(command.ServerOnly, $"{name} can be run by players");
    }
}
