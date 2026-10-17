using System.Reflection;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Tests;

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

    public static IEnumerable<object[]> Presets() => ServerSettings.Presets.Select(p => new object[] { p.Title });

    private static ServerSettings Preset(string title)
    {
        var s = new ServerSettings();
        ServerSettings.Presets.Single(p => p.Title == title).Apply(s);
        return s;
    }

    /// <summary>Splits a console line like the engine: spaces separate, quotes group.</summary>
    internal static List<string> Tokens(string line) =>
        Regex.Matches(line, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();

    private static IEnumerable<string> Lines(string cfg) =>
        cfg.Split('\n').Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith("//"));

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

    [Theory]
    [MemberData(nameof(Presets))]
    public void EveryQuickStartWritesOnlyRealCommands(string title)
    {
        foreach (var line in Lines(Preset(title).ToCfg())) AssertKnown(line);
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void QuickStartsWithBotImproverStillWriteOnlyRealCommands(string title)
    {
        var s = Preset(title);
        s.BotImprover = true;
        s.BotHider = true;
        foreach (var weapon in new[] { "", "ak47", "awp", "deagle", "r8", "m4a1s" })
        {
            s.BotForceWeapon = weapon;
            foreach (var line in Lines(s.ToCfg())) AssertKnown(line);
            foreach (var line in s.BotWeaponCommands()) AssertKnown(line);
        }
    }

    [Fact]
    public void LiveCommandsAreReal()
    {
        var s = new ServerSettings { HackerMode = 1 };
        foreach (var line in s.HackerCommands()) AssertKnown(line);
        foreach (var line in ServerSettings.ScoutsOnCommands.Concat(ServerSettings.ScoutsOffCommands)) AssertKnown(line);
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

    [Fact]
    public void PluginRetakesLeaveSeatsForFriends()
    {
        var cfg = Preset("Plugin Retakes").ToCfg();
        // 9 seats: 8 bots next to you, and the plugin lets one go for every friend who connects.
        Assert.Contains("bot_quota 8", cfg);
        Assert.Contains("lst_retake_seats 9 8 0", cfg);
        Assert.Contains("mp_limitteams 0", cfg);
        Assert.Contains("retakes_enabled 1", cfg);
        Assert.Contains("lst_qol spawn=1 guns=0", cfg); // !guns is the allocator's menu here
    }

    [Fact]
    public void ValveRetakesChangeBotsLive()
    {
        var cfg = Preset("Valve Retakes").ToCfg();
        Assert.Contains("bot_quota 6", cfg);
        Assert.Contains("lst_retake_seats 7 6 1", cfg);
        Assert.Contains("mp_limitteams 0", cfg);
        Assert.Contains("sv_skirmish_id 12", cfg);
    }

    [Fact]
    public void OtherModesHaveNoSeatLimit()
    {
        var cfg = Preset("Free-for-all Deathmatch").ToCfg();
        Assert.Contains("lst_retake_seats 0 10 0", cfg);
        Assert.Contains("mp_limitteams 2", cfg);
        Assert.Contains("retakes_enabled 0", cfg);
    }

    [Fact]
    public void TooManyBotsForRetakesStillLeaveYouASeat()
    {
        var s = Preset("Plugin Retakes");
        s.Bots = 20;
        s.RetakeMaxPlayers = 5;
        var cfg = s.ToCfg();
        Assert.Contains("bot_quota 4", cfg);
        Assert.Contains("lst_retake_seats 5 20 0", cfg);
    }

    [Fact]
    public void EveryForcedWeaponHasALoadout()
    {
        foreach (var weapon in ServerSettings.BotForcedWeapons)
            Assert.False(string.IsNullOrEmpty(ServerSettings.BotLoadoutFor(weapon)), weapon);
        Assert.Null(ServerSettings.BotLoadoutFor(""));
        Assert.Null(ServerSettings.BotLoadoutFor("bot_buy"));
        Assert.Equal("m4a1_silencer tec9 vesthelm defuser", ServerSettings.BotLoadoutFor("m4a1s"));
        Assert.Equal("revolver vesthelm defuser", ServerSettings.BotLoadoutFor("r8"));
    }

    [Fact]
    public void ForcedWeaponIsSetWhileCheatsAreOn()
    {
        var s = Preset("Competitive 5v5");
        s.AllowCheats = false;
        s.BotImprover = true;
        s.BotForceWeapon = "ak47";
        var lines = Lines(s.ToCfg()).ToList();
        var loadout = lines.IndexOf("bot_loadout \"ak47 tec9 vesthelm defuser\"");
        Assert.True(loadout > 0, "bot_loadout missing");
        // bot_loadout is cheat-protected: sv_cheats 1 before it, and it must stay on (sv_cheats 0 resets it).
        Assert.Equal("sv_cheats 1", lines.Take(loadout).Last(l => l.StartsWith("sv_cheats")));
        Assert.Equal("sv_cheats 1", lines.Skip(loadout).First(l => l.StartsWith("sv_cheats")));
        Assert.DoesNotContain(lines, l => l is "ak47" or "bot_buy");
    }

    [Fact]
    public void NoForcedWeaponClearsTheLoadout()
    {
        var s = Preset("Competitive 5v5");
        s.BotImprover = true;
        Assert.Contains("bot_loadout \"\"", s.ToCfg());
        s.BotImprover = false;
        s.BotForceWeapon = "awp"; // left over from when Bot Improver was on
        Assert.Contains("bot_loadout \"\"", s.ToCfg());
        Assert.False(s.ForcesBotWeapon);
    }

    [Fact]
    public void ForcedSniperIsNotSwappedForARifle()
    {
        var s = Preset("Free-for-all Deathmatch");
        Assert.Equal(0, s.BotWeapons); // Rifles
        Assert.Contains("lst_bot_no_snipers 1", s.ToCfg());
        s.BotImprover = true;
        s.BotForceWeapon = "awp";
        var cfg = s.ToCfg();
        Assert.Contains("lst_bot_no_snipers 0", cfg);
        Assert.Contains("bot_allow_snipers 1", cfg);
    }

    [Fact]
    public void BotImproverCommandsAreOnlyItsRealCommands()
    {
        var s = new ServerSettings { BotImprover = true, BotForceWeapon = "ak47" };
        Assert.Equal(["bot_aim mixed", "bot_nades normal"], s.BotImproverCommands().ToList());
        s.BotImprover = false;
        Assert.Empty(s.BotImproverCommands());
    }

    [Fact]
    public void WeaponChangeOnARunningServerLeavesCheatsLikeTheCfg()
    {
        var s = new ServerSettings { AllowCheats = false, BotImprover = true, BotForceWeapon = "awp" };
        Assert.Equal(["sv_cheats 1", "bot_loadout \"awp tec9 vesthelm defuser\"", "sv_cheats 1"], s.BotWeaponCommands());
        s.BotForceWeapon = "";
        Assert.Equal(["sv_cheats 1", "bot_loadout \"\"", "sv_cheats 0"], s.BotWeaponCommands());
    }

    [Fact]
    public void FlyingScoutsmanOffUndoesWhatOnChanged()
    {
        string Name(string line) => Tokens(line)[0];
        var on = ServerSettings.ScoutsOnCommands.Select(Name).ToHashSet();
        var off = ServerSettings.ScoutsOffCommands.Select(Name).ToHashSet();
        // Money, round time and freeze time come back from the mode's own config when the map reloads;
        // gravity, air acceleration and bunnyhopping from the launcher's cfg.
        var restoredByReload = new HashSet<string>
        {
            "sv_cheats", "mp_maxmoney", "mp_roundtime", "mp_freezetime", "mp_restartgame", "sv_gravity", "sv_airaccelerate",
            "sv_enablebunnyhopping",
        };
        foreach (var name in on.Where(n => !restoredByReload.Contains(n)))
            Assert.True(off.Contains(name), $"Flying Scoutsman off doesn't reset {name}");
    }

    [Fact]
    public void GameJoinsTheLocalServerWithVacOff()
    {
        var root = Directory.CreateTempSubdirectory("lst-game").FullName;
        try
        {
            var game = new GameFolder(root);
            var args = new ServerSettings { GameArgs = "-windowed +connect 1.2.3.4", Password = "abc" }.GameArguments(game);
            Assert.StartsWith("-insecure ", args);
            Assert.EndsWith("+connect 127.0.0.1:27015", args);
            Assert.Contains("+password \"abc\"", args);
            Assert.Single(Regex.Matches(args, @"\+connect"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void ServerStartsWithAMapAndTheRightMode(string title)
    {
        var s = Preset(title);
        var args = s.StartArguments();
        var mode = s.GetMode();
        Assert.Contains("-dedicated", args);
        Assert.Contains("-insecure", args);
        Assert.Contains($"+game_type {mode.GameType} +game_mode {mode.Mode}", args);
        Assert.Matches(@"\+map \S+", args);
        Assert.Contains($"+sv_skirmish_id {(s.IsValveRetakes ? 12 : 0)}", args);
    }

    [Fact]
    public void ResetGivesAPlainServerAndKeepsFriendsSettings()
    {
        var old = Preset("Plugin Retakes");
        old.Password = "pw";
        old.Friends = ServerSettings.FriendsNetwork.Vpn;
        old.GameArgs = "-novid -w 1920";
        var s = ServerSettings.Stock(old);
        Assert.Equal(("Casual", 0, "pw", ServerSettings.FriendsNetwork.Vpn, "-novid -w 1920"), (s.Mode, s.Bots, s.Password, s.Friends, s.GameArgs));
        var cfg = s.ToCfg();
        Assert.Contains("lst_qol spawn=0 guns=0 ready=0 vote=0 summary=0 welcome=0", cfg);
        Assert.Contains("retakes_enabled 0", cfg);
        Assert.DoesNotContain("mp_maxmoney 65535", cfg);
    }

    [Fact]
    public void WorkshopMapKeepsItsOwnRules()
    {
        var s = ServerSettings.ForWorkshop(new ServerSettings { Password = "pw" }, "3070244462");
        var cfg = s.ToCfg();
        // aim_botz-style maps add their own bots and set buying; none of this may override them.
        foreach (var line in new[] { "bot_kick", "bot_quota", "mp_limitteams", "mp_warmup_end", "sv_cheats", "mp_items_prohibited", "mp_maxmoney" })
            Assert.DoesNotContain(line, cfg);
        Assert.Contains("sv_password \"pw\"", cfg);
        Assert.Contains("lst_only_weapon off", cfg);
        Assert.Contains("lst_retake_seats 0", cfg);
        s.KeepMapRules = false;
        Assert.Contains("mp_warmup_end", s.ToCfg());
    }
}
