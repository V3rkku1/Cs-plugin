using System.Text;

namespace LanSkinTester.Launcher.Core;

public record GameMode(string Name, int GameType, int Mode, string[] Maps)
{
    public override string ToString() => Name;
}

/// <summary>A one-click setup on the PLAY page.</summary>
public record ServerPreset(string Title, string Subtitle, Action<ServerSettings> Apply);

/// <summary>
/// Game mode, bots and practice options for the local server. Turned into csgo\cfg\lanskintester_server.cfg,
/// which the plugin runs after every map start (after the game mode's own config, so these win).
/// </summary>
public class ServerSettings
{
    private static readonly string[] DefuseMaps =
        ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_vertigo", "de_overpass", "de_train", "cs_office", "cs_italy"];

    public static readonly GameMode[] Modes =
    [
        new("Deathmatch", 1, 2, DefuseMaps),
        new("Competitive", 0, 1, DefuseMaps),
        new("Casual", 0, 0, DefuseMaps),
        new("Wingman", 0, 2, ["de_inferno", "de_nuke", "de_vertigo", "de_overpass"]),
        new("Arms Race", 1, 0, ["ar_baggage", "ar_shoots", "ar_pool_day"]),
    ];

    public static readonly string[] Difficulties = ["Easy", "Normal", "Hard", "Expert"];
    public static readonly string[] BotWeaponChoices = ["Any weapons", "Pistols only", "Snipers only", "Knives only"];
    public static readonly string[] BotTeams = ["Both teams", "Terrorists", "Counter-Terrorists"];

    public string Mode { get; set; } = "Deathmatch";
    public string Map { get; set; } = "de_dust2";
    public int Bots { get; set; } = 10;
    public int Difficulty { get; set; } = 3;
    public int BotTeam { get; set; }
    public int BotWeapons { get; set; }
    public bool FreeForAll { get; set; }
    public bool BotsDontShoot { get; set; }
    public bool InfiniteAmmo { get; set; }
    public bool MaxMoney { get; set; } = true;
    public bool NoRoundEnd { get; set; }
    public bool AllowCheats { get; set; } = true;

    /// <summary>Round length in minutes; 0 = the game mode's default.</summary>
    public int RoundTime { get; set; }
    public bool FriendlyFire { get; set; }

    /// <summary>Steam Workshop map id (overrides Map). Needs internet: the server downloads it.</summary>
    public string WorkshopMap { get; set; } = "";

    /// <summary>Extra CS2 launch options for the game window, e.g. "-windowed -w 1920 -h 1080 -novid".</summary>
    public string GameArgs { get; set; } = "-novid";

    /// <summary>Start CS2 and join automatically (off = only the server starts).</summary>
    public bool AutoStartGame { get; set; } = true;

    public static readonly ServerPreset[] Presets =
    [
        new("DM vs Expert bots", "Deathmatch · 12 expert bots · max money", s =>
        {
            s.Reset("Deathmatch", "de_dust2");
            s.Bots = 12;
            s.Difficulty = 3;
        }),
        new("Free-for-all", "Everyone vs everyone · 10 hard bots", s =>
        {
            s.Reset("Deathmatch", "de_mirage");
            s.Bots = 10;
            s.Difficulty = 2;
            s.FreeForAll = true;
        }),
        new("Skin showcase", "Peaceful bots · third person allowed", s =>
        {
            s.Reset("Casual", "de_inferno");
            s.Bots = 4;
            s.Difficulty = 0;
            s.BotsDontShoot = true;
            s.InfiniteAmmo = true;
            s.NoRoundEnd = true;
        }),
        new("Aim practice", "Pistol bots · infinite ammo · respawn", s =>
        {
            s.Reset("Deathmatch", "de_dust2");
            s.Bots = 16;
            s.Difficulty = 1;
            s.BotWeapons = 1;
            s.InfiniteAmmo = true;
        }),
        new("Competitive 5v5", "9 hard bots · normal rules", s =>
        {
            s.Reset("Competitive", "de_mirage");
            s.Bots = 9;
            s.Difficulty = 2;
            s.MaxMoney = false;
        }),
        new("Arms Race", "Gun game · 9 bots", s =>
        {
            s.Reset("Arms Race", "ar_shoots");
            s.Bots = 9;
            s.Difficulty = 2;
            s.MaxMoney = false;
        }),
    ];

    /// <summary>Back to plain defaults for a mode (keeps launch options and auto-start).</summary>
    private void Reset(string mode, string map)
    {
        Mode = mode;
        Map = map;
        WorkshopMap = "";
        BotTeam = BotWeapons = RoundTime = 0;
        FreeForAll = BotsDontShoot = InfiniteAmmo = NoRoundEnd = FriendlyFire = false;
        MaxMoney = AllowCheats = true;
    }

    public GameMode GetMode() => Modes.FirstOrDefault(m => m.Name == Mode) ?? Modes[0];

    private bool NeedsCheats => AllowCheats || InfiniteAmmo || BotsDontShoot;

    public string StartArguments()
    {
        var mode = GetMode();
        var map = WorkshopMapId != null ? $"+host_workshop_map {WorkshopMapId}" : $"+map {Map}";
        return $"-dedicated -insecure +sv_lan 1 +game_type {mode.GameType} +game_mode {mode.Mode} {map}";
    }

    /// <summary>The Workshop id from an id or a steamcommunity.com/sharedfiles/...?id=123 link.</summary>
    public string? WorkshopMapId
    {
        get
        {
            var m = System.Text.RegularExpressions.Regex.Match(WorkshopMap ?? "", @"(\d{6,})");
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    public string GameArguments()
    {
        var extra = (GameArgs ?? "").Replace("+connect", "").Trim();
        return $"-insecure {extra} +connect localhost".Replace("  ", " ");
    }

    public string ToCfg()
    {
        var mode = GetMode();
        var cheats = NeedsCheats;
        var sb = new StringBuilder();
        sb.AppendLine("// Written by the LAN Skin Tester launcher - changes here are overwritten.");
        sb.AppendLine("sv_lan 1");

        // Cheat-protected settings need sv_cheats 1 while they're set (and turning it off resets them).
        sb.AppendLine("sv_cheats 1");
        sb.AppendLine($"sv_infinite_ammo {(InfiniteAmmo ? 1 : 0)}");
        sb.AppendLine($"bot_dont_shoot {(BotsDontShoot ? 1 : 0)}");
        sb.AppendLine($"sv_cheats {(cheats ? 1 : 0)}");

        // Bots: difficulty only applies to newly added bots, so kick and re-add them.
        sb.AppendLine($"bot_difficulty {Math.Clamp(Difficulty, 0, 3)}");
        sb.AppendLine("bot_quota_mode normal");
        sb.AppendLine("bot_join_after_player 0");
        sb.AppendLine($"bot_join_team {BotTeam switch { 1 => "T", 2 => "CT", _ => "any" }}");
        sb.AppendLine($"bot_knives_only {(BotWeapons == 3 ? 1 : 0)}");
        sb.AppendLine($"bot_pistols_only {(BotWeapons == 1 ? 1 : 0)}");
        sb.AppendLine($"bot_snipers_only {(BotWeapons == 2 ? 1 : 0)}");
        sb.AppendLine("bot_kick");
        sb.AppendLine($"bot_quota {Math.Clamp(Bots, 0, 31)}");

        if (mode.GameType == 1 && mode.Mode == 2)
            sb.AppendLine($"mp_teammates_are_enemies {(FreeForAll ? 1 : 0)}");

        if (MaxMoney)
        {
            sb.AppendLine("mp_maxmoney 65535");
            sb.AppendLine("mp_startmoney 65535");
            sb.AppendLine("mp_afterroundmoney 65535");
            sb.AppendLine("mp_buy_anywhere 1");
            sb.AppendLine("mp_buytime 9999");
        }

        if (NoRoundEnd)
        {
            sb.AppendLine("mp_ignore_round_win_conditions 1");
            sb.AppendLine("mp_roundtime 60");
            sb.AppendLine("mp_roundtime_defuse 60");
            sb.AppendLine("mp_freezetime 0");
            sb.AppendLine("mp_respawn_on_death_t 1");
            sb.AppendLine("mp_respawn_on_death_ct 1");
        }

        if (RoundTime > 0)
        {
            sb.AppendLine($"mp_roundtime {RoundTime}");
            sb.AppendLine($"mp_roundtime_defuse {RoundTime}");
            sb.AppendLine($"mp_roundtime_hostage {RoundTime}");
        }
        if (FriendlyFire) sb.AppendLine("mp_friendlyfire 1");

        sb.AppendLine("mp_warmup_end");
        return sb.ToString();
    }

    public void WriteCfg(GameFolder game)
    {
        var cfgDir = Path.Combine(game.Csgo, "cfg");
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "lanskintester_server.cfg"), ToCfg());
    }

    /// <summary>
    /// Applies the settings to a running server through the plugin. Changing mode or map reloads the map
    /// (the plugin then runs the cfg again); otherwise the cfg is just re-run.
    /// </summary>
    public void ApplyToRunningServer(GameFolder game, bool mapOrModeChanged)
    {
        WriteCfg(game);
        var mode = GetMode();
        if (mapOrModeChanged)
            SendCommands(game, $"game_type {mode.GameType}", $"game_mode {mode.Mode}",
                WorkshopMapId != null ? $"host_workshop_map {WorkshopMapId}" : $"changelevel {Map}");
        else
            SendCommands(game, "exec lanskintester_server.cfg");
    }

    /// <summary>
    /// Queues server console commands; the plugin runs them within a second. Appends, so two quick
    /// clicks don't overwrite each other.
    /// </summary>
    public static void SendCommands(GameFolder game, params string[] commands)
    {
        var file = Path.Combine(game.PluginDir, "server_commands.txt");
        var existing = File.Exists(file) ? File.ReadAllText(file) : "";
        var temp = file + ".tmp";
        File.WriteAllText(temp, existing + string.Join("\n", commands) + "\n");
        File.Move(temp, file, overwrite: true);
    }
}
