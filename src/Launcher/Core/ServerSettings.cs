using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

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

    public static GameMode[] Modes { get; private set; } =
    [
        new("Deathmatch", 1, 2, DefuseMaps),
        new("Competitive", 0, 1, DefuseMaps),
        new("Casual", 0, 0, DefuseMaps),
        new("Wingman", 0, 2, ["de_inferno", "de_nuke", "de_vertigo", "de_overpass"]),
        new("Arms Race", 1, 0, ["ar_baggage", "ar_shoots", "ar_pool_day"]),
        new("Demolition", 1, 1, ["de_lake", "de_safehouse", "de_shortdust"]),
        new("Weapons Expert", 0, 3, DefuseMaps),
        new("Custom", 3, 0, DefuseMaps),
        // Valve's own Retakes: game_type 0 / game_mode 5, also selected by sv_skirmish_id 12.
        new(RetakesMode, 0, 5, ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_vertigo", "de_overpass", "de_train"]),
    ];

    public const string RetakesMode = "Retakes";
    public const string RushMode = "Rush";

    /// <summary>
    /// Adds modes the launcher only knows by name, read from the game's own files (Rush: Valve's 3v3
    /// tower mode). Returns whether something new was found.
    /// </summary>
    public static bool DiscoverModes(GameFolder game)
    {
        if (Modes.Any(m => m.Name == RushMode)) return false;
        var rush = GameModeCatalog.Find(game, "rush", RushMode);
        if (rush == null) return false;
        Modes = [.. Modes, rush];
        return true;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRetakes => Mode == RetakesMode;

    /// <summary>0 = Valve's built-in retakes, 1 = B3none's cs2-retakes plugin.</summary>
    public int RetakeStyle { get; set; }

    public static readonly string[] RetakeStyles = ["Valve", "Plugin"];

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsPluginRetakes => IsRetakes && RetakeStyle == 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValveRetakes => IsRetakes && RetakeStyle != 1;

    /// <summary>Plugin retakes: players in the match, including you (2-10).</summary>
    public int RetakeMaxPlayers { get; set; } = 9;

    /// <summary>Plugin retakes: percent of the players who are Terrorists.</summary>
    public int RetakeTerroristPercent { get; set; } = 45;

    public bool RetakeScramble { get; set; } = true;
    public int RetakeScrambleRounds { get; set; } = 5;
    public bool RetakeBalance { get; set; } = true;
    public bool RetakeAutoPlant { get; set; } = true;
    public bool RetakeAnnounce { get; set; } = true;

    /// <summary>Plugin retakes: skip the team menu and put people straight into the match.</summary>
    public bool RetakeAutoJoin { get; set; } = true;

    /// <summary>0 = freeze-time buy, 1 = the retakes allocator (pistol, half-buy, rifle).</summary>
    public int RetakeWeapons { get; set; }

    /// <summary>Plugin retakes: freeze time is a buy period. Kept in step with <see cref="RetakeWeapons"/>.</summary>
    public bool RetakeBuy { get; set; } = true;

    /// <summary>Plugin retakes: instadefuse when nothing can stop the defuse.</summary>
    public bool RetakeInstadefuse { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesAllocator => IsPluginRetakes && RetakeWeapons == 1;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool UsesRetakeBuy => IsPluginRetakes && RetakeWeapons != 1;

    /// <summary>Seconds of freeze time to buy, when <see cref="RetakeBuy"/> is on.</summary>
    public int RetakeBuySeconds { get; set; } = 12;

    /// <summary>Plugin retakes: bomb timer in seconds.</summary>
    public int RetakeBombTimer { get; set; } = 40;

    public static readonly string[] Difficulties = ["Easy", "Normal", "Hard", "Expert"];
    /// <summary>Index 0 "Rifles" (no snipers, shotguns, SMGs or machine guns: with max money bots otherwise buy AWPs).</summary>
    public static readonly string[] BotWeaponChoices = ["Rifles", "Pistols only", "Snipers only", "Knives only", "Any weapons"];
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

    /// <summary>Chat line for the damage you deal (practice / retake quality of life).</summary>
    public bool ShowDamage { get; set; }

    /// <summary>Round length in minutes; 0 = the game mode's default.</summary>
    public int RoundTime { get; set; }
    public bool FriendlyFire { get; set; }

    /// <summary>Steam Workshop map id (overrides Map). Needs internet: the server downloads it.</summary>
    public string WorkshopMap { get; set; } = "";

    /// <summary>Extra CS2 launch options for the game window, e.g. "-windowed -w 1920 -h 1080 -novid".</summary>
    public string GameArgs { get; set; } = "-novid";

    public enum FriendsNetwork { Lan, Vpn, Internet }

    /// <summary>
    /// How friends reach the server. Radmin / Hamachi addresses and internet players (UPnP) aren't
    /// "local" for sv_lan 1, so then the server runs with sv_lan 0 (still unlisted: no game server token)
    /// and the plugin is told it's a private server.
    /// </summary>
    public FriendsNetwork Friends { get; set; }

    /// <summary>
    /// sv_lan 0: friends over Radmin / the internet, or the server has to download a Workshop map
    /// itself (an sv_lan 1 server is offline from Steam and can't).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool PrivateServer => Friends != FriendsNetwork.Lan || WorkshopNeedsDownload;

    /// <summary>The Workshop map copied into csgo\maps for this session (see <see cref="WorkshopMaps"/>), or null.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? LocalWorkshopMap { get; private set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool WorkshopNeedsDownload => WorkshopMapId != null && LocalWorkshopMap == null;

    /// <summary>
    /// Workshop maps always load the official way (host_workshop_map, server online). Copying the
    /// downloaded .vpk into csgo\maps broke maps that Steam stores as a split archive (_dir/_000)
    /// and crashed the server, so that is no longer done; old copies are cleaned up.
    /// </summary>
    public void PrepareWorkshopMap(GameFolder game, bool forFriends)
    {
        LocalWorkshopMap = null;
        try { WorkshopMaps.RemoveCopies(game); }
        catch (Exception) { /* not important */ }
    }

    /// <summary>
    /// The console command that loads the map (without "+"). At startup a Workshop map is NOT asked
    /// for on the command line: that runs before the server has logged on to Steam, and it then sits
    /// idle with no map. It boots on the normal map and the launcher sends host_workshop_map once the
    /// server is up (PlayPage).
    /// </summary>
    private string MapCommand(bool running) =>
        LocalWorkshopMap != null ? $"{(running ? "changelevel" : "map")} {LocalWorkshopMap}"
        : WorkshopMapId != null && running ? $"host_workshop_map {WorkshopMapId}"
        : $"{(running ? "changelevel" : "map")} {Map}";

    /// <summary>Everyone uses the launcher's loadout (off = each friend picks skins in chat; the host keeps the launcher's).</summary>
    public bool SharedSkins { get; set; } = true;

    /// <summary>Optional server password for friends.</summary>
    public string Password { get; set; } = "";

    public static readonly string[] HackerModes = ["Off", "Everyone", "One random"];

    /// <summary>Hacker mode: 0 off, 1 every player hacks (great vs bots), 2 one random hacker per round ("catch the hacker").</summary>
    public int HackerMode { get; set; }
    public bool HackWallhack { get; set; } = true;
    public bool HackAimbot { get; set; } = true;
    public bool HackOneTap { get; set; }
    public bool HackAmmo { get; set; } = true;
    public bool HackSpeed { get; set; }
    public bool HackBhop { get; set; }
    public bool HackNoFlash { get; set; }
    public bool HackEspInfo { get; set; }
    public bool HackAimWalls { get; set; }

    /// <summary>Hacker mode: no recoil (per hacker in the plugin, plus CS2's server-wide recoil cvars) and
    /// no spread (only exists server-wide: weapon_accuracy_nospread, so it applies to everyone).</summary>
    public bool HackNoRecoil { get; set; }
    public bool HackNoSpread { get; set; }

    /// <summary>Enemies show on the hacker's radar.</summary>
    public bool HackRadar { get; set; }

    public static readonly string[] AimKeys = ["Hold fire (Mouse 1)", "Always on", "Hold right click", "Hold Shift", "Hold E", "Hold F", "Only my own key"];
    private static readonly string[] AimKeyCommands = ["fire", "always", "mouse2", "shift", "e", "f", "custom"];

    public static readonly string[] AimTargets = ["Closest enemy", "Nearest crosshair"];

    /// <summary>Who the aimbot goes for (index into <see cref="AimTargets"/>).</summary>
    public int HackAimTarget { get; set; }

    /// <summary>When the aimbot works (index into <see cref="AimKeys"/>).</summary>
    public int HackAimKey { get; set; }

    /// <summary>Any keyboard/mouse key bound to the aimbot in CS2 on this PC (e.g. mouse4, x, alt). Empty = none.</summary>
    public string AimBindKey { get; set; } = "";

    /// <summary>Aimbot aggression 1 (gentle, slow, loose) - 10 (fast hard head lock).</summary>
    public int HackAggression { get; set; } = 5;

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
        new("Hacker vs bots", "Walls, aimbot, bhop · 16 expert bots", s =>
        {
            s.Reset("Deathmatch", "de_dust2");
            s.Bots = 16;
            s.Difficulty = 3;
            s.HackerMode = 1;
            s.HackSpeed = s.HackBhop = s.HackEspInfo = s.HackNoFlash = s.HackRadar = s.HackNoRecoil = true;
            s.HackAggression = 7;
        }),
        new("Retakes", "Valve retakes · 3v4 · loadout cards", s =>
        {
            s.Reset(RetakesMode, "de_mirage");
            s.Bots = 6; // you + 6 bots fills the 3 T vs 4 CT match
            s.Difficulty = 2;
            s.MaxMoney = false;
            s.RetakeStyle = 0;
        }),
        new("Plugin retakes", "Allocator · instadefuse · 8 bots", s =>
        {
            s.Reset(RetakesMode, "de_mirage");
            s.Bots = 8;
            s.Difficulty = 2;
            s.MaxMoney = false;
            s.RetakeStyle = 1;
            s.RetakeWeapons = 1;
            s.RetakeBuy = false;
            s.RetakeInstadefuse = true;
        }),
        new("Retake buy", "Plugin retakes · you buy the guns", s =>
        {
            s.Reset(RetakesMode, "de_mirage");
            s.Bots = 8;
            s.Difficulty = 2;
            s.MaxMoney = false;
            s.RetakeStyle = 1;
            s.RetakeWeapons = 0;
            s.RetakeBuy = true;
            s.RetakeBuySeconds = 12;
            s.RetakeInstadefuse = true;
        }),
        new("Demolition", "Gun game with a bomb · 9 bots", s =>
        {
            s.Reset("Demolition", "de_safehouse");
            s.Bots = 9;
            s.Difficulty = 2;
            s.MaxMoney = false;
        }),
        new("Weapons Expert", "Each gun once · 9 hard bots", s =>
        {
            s.Reset("Weapons Expert", "de_mirage");
            s.Bots = 9;
            s.Difficulty = 2;
            s.MaxMoney = false;
        }),
        new("Rush 3v3", "Valve's tower mode · you + 2 bots vs 3", s =>
        {
            var rush = Modes.FirstOrDefault(m => m.Name == RushMode);
            s.Reset(RushMode, rush?.Maps[0] ?? "de_dust2");
            s.Bots = 5;
            s.Difficulty = 2;
            s.MaxMoney = false;
        }),
        new("Catch the hacker", "Friends: one random hacker per round", s =>
        {
            s.Reset("Casual", "de_mirage");
            s.Bots = 0;
            s.HackerMode = 2;
            s.FriendlyFire = false;
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
        ShowDamage = false;
        RetakeStyle = 0;
        RetakeMaxPlayers = 9;
        RetakeTerroristPercent = 45;
        RetakeWeapons = 0;
        RetakeScramble = RetakeBalance = RetakeAutoPlant = RetakeAnnounce = RetakeAutoJoin = RetakeBuy = RetakeInstadefuse = true;
        RetakeScrambleRounds = 5;
        RetakeBuySeconds = 12;
        RetakeBombTimer = 40;
        HackerMode = 0;
        HackWallhack = HackAimbot = HackAmmo = true;
        HackOneTap = HackSpeed = HackBhop = HackNoFlash = HackEspInfo = HackAimWalls = HackNoRecoil = HackNoSpread = HackRadar = false;
        HackAggression = 5;
    }

    public GameMode GetMode()
    {
        var mode = Modes.FirstOrDefault(m => m.Name == Mode) ?? Modes[0];
        // Plugin retakes runs on competitive rules. Valve's mode is game_type 0 / game_mode 5.
        if (IsPluginRetakes) return mode with { GameType = 0, Mode = 1 };
        return mode;
    }

    private bool HackRecoilOrSpread => HackerMode != 0 && (HackNoRecoil || HackNoSpread);

    private bool NeedsCheats => AllowCheats || InfiniteAmmo || BotsDontShoot || HackRecoilOrSpread;

    public string StartArguments()
    {
        var mode = GetMode();
        // -condebug: the server writes csgo\console.log, which the launcher shows if the server crashes.
        // sv_hibernate_when_empty 0: an empty server otherwise sleeps after a few seconds, and then the
        // plugin can't run the launcher's commands (e.g. switching to a Workshop map) until someone joins.
        // sv_skirmish_id survives map changes. 12 is Valve's Retakes war game; every other mode clears it.
        // Retake team sizes have to be set before the map loads (they decide the slot count).
        var skirmish = IsValveRetakes ? 12 : 0;
        var retake = IsValveRetakes ? " +mp_retake_t_count 3 +mp_retake_ct_count 4" : "";
        return $"-dedicated -insecure -condebug +sv_hibernate_when_empty 0 +sv_lan {(PrivateServer ? 0 : 1)} -port {Port} +game_type {mode.GameType} +game_mode {mode.Mode} +sv_skirmish_id {skirmish}{retake} +{MapCommand(running: false)}";
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

    public const int Port = 27015;

    /// <summary>127.0.0.1, not "localhost": that can resolve to IPv6 (::1), where the server doesn't listen.</summary>
    public string GameArguments(GameFolder game) => JoinArguments(game, $"127.0.0.1:{Port}", Password);

    /// <summary>CS2 with VAC off for this run only (nothing is saved in Steam), joining a server.</summary>
    public string JoinArguments(GameFolder game, string address, string? password)
    {
        var extra = (GameArgs ?? "").Replace("+connect", "").Trim();
        var pass = string.IsNullOrWhiteSpace(password) ? "" : $"+password \"{password.Trim()}\" ";
        var exec = WriteClientCfg(game) ? $"+exec {ClientCfg} " : "";
        return $"-insecure {extra} {exec}{pass}+connect {address}".Replace("  ", " ");
    }

    public const string ClientCfg = "lanskintester_client";

    /// <summary>A key name CS2 understands (mouse4, x, alt, f6, kp_5...), or null.</summary>
    public static string? CleanKey(string? key)
    {
        key = (key ?? "").Trim().ToLowerInvariant();
        return System.Text.RegularExpressions.Regex.IsMatch(key, "^[a-z0-9_]{1,16}$") ? key : null;
    }

    /// <summary>
    /// Writes the game's own config for this PC's player: the personal aimbot key (bind KEY +lst_aim).
    /// Returns whether CS2 should run it.
    /// </summary>
    public bool WriteClientCfg(GameFolder game)
    {
        try
        {
            var file = Path.Combine(game.Csgo, "cfg", ClientCfg + ".cfg");
            var key = CleanKey(AimBindKey);
            if (key == null)
            {
                if (File.Exists(file)) File.Delete(file);
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,
                "// Written by the LAN Skin Tester launcher.\n" +
                "// Hacker mode: hold this key for the aimbot (does nothing on other servers).\n" +
                $"bind \"{key}\" \"+lst_aim\"\n");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The Steam account id (32-bit) logged in on this PC, so the plugin knows who the host is.</summary>
    public static uint ActiveSteamAccount()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0);
            return value is int id ? unchecked((uint)id) : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    public string ToCfg()
    {
        var mode = GetMode();
        var cheats = NeedsCheats;
        var sb = new StringBuilder();
        sb.AppendLine("// Written by the LAN Skin Tester launcher - changes here are overwritten.");
        sb.AppendLine("sv_hibernate_when_empty 0"); // keep running with nobody connected (launcher commands, bots)
        sb.AppendLine($"sv_lan {(PrivateServer ? 0 : 1)}");
        sb.AppendLine($"lst_private_server {(PrivateServer ? 1 : 0)}");
        sb.AppendLine($"lst_shared_loadout {(SharedSkins ? 1 : 0)}");
        sb.AppendLine($"lst_host_account {ActiveSteamAccount()}");
        sb.AppendLine($"sv_password \"{(Password ?? "").Replace("\"", "").Trim()}\"");

        // Cheat-protected settings need sv_cheats 1 while they're set (and turning it off resets them).
        sb.AppendLine("sv_cheats 1");
        sb.AppendLine($"sv_infinite_ammo {(InfiniteAmmo ? 1 : 0)}");
        sb.AppendLine($"bot_dont_shoot {(BotsDontShoot ? 1 : 0)}");
        foreach (var line in RecoilCvars()) sb.AppendLine(line);
        sb.AppendLine($"sv_cheats {(cheats ? 1 : 0)}");

        // Bots: difficulty only applies to newly added bots, so kick and re-add them.
        sb.AppendLine($"bot_difficulty {Math.Clamp(Difficulty, 0, 3)}");
        sb.AppendLine("bot_quota_mode normal");
        sb.AppendLine("bot_join_after_player 0");
        sb.AppendLine($"bot_join_team {(IsRetakes ? "any" : BotTeam switch { 1 => "T", 2 => "CT", _ => "any" })}");
        // Retakes, Arms Race and Demolition hand out their own guns.
        var rifles = !OwnsLoadout && BotWeapons == 0;
        sb.AppendLine($"bot_knives_only {(BotWeapons == 3 && !OwnsLoadout ? 1 : 0)}");
        sb.AppendLine($"bot_pistols_only {(BotWeapons == 1 && !OwnsLoadout ? 1 : 0)}");
        sb.AppendLine($"bot_snipers_only {(BotWeapons == 2 && !OwnsLoadout ? 1 : 0)}");
        sb.AppendLine($"bot_allow_snipers {(rifles ? 0 : 1)}");
        sb.AppendLine($"bot_allow_shotguns {(rifles ? 0 : 1)}");
        sb.AppendLine($"bot_allow_sub_machine_guns {(rifles ? 0 : 1)}");
        sb.AppendLine($"bot_allow_machine_guns {(rifles ? 0 : 1)}");
        sb.AppendLine("bot_allow_rifles 1");
        sb.AppendLine("bot_allow_pistols 1");
        sb.AppendLine($"lst_bot_no_snipers {(rifles ? 1 : 0)}"); // plugin safety net: swaps snipers for AK/M4
        sb.AppendLine("bot_kick");
        // Plugin retakes only has seats for MaxPlayers. Leave one for you when bots are on.
        var quota = Math.Clamp(Bots, 0, 31);
        if (IsPluginRetakes && quota > 0)
            quota = Math.Min(quota, Math.Max(0, Math.Clamp(RetakeMaxPlayers, 2, 10) - 1));
        sb.AppendLine($"bot_quota {quota}");

        if (mode.GameType == 1 && mode.Mode == 2)
            sb.AppendLine($"mp_teammates_are_enemies {(FreeForAll ? 1 : 0)}");
        else
            sb.AppendLine("mp_teammates_are_enemies 0");

        if (UsesRetakeBuy)
        {
            var seconds = Math.Clamp(RetakeBuySeconds, 4, 20);
            sb.AppendLine("mp_maxmoney 16000");
            sb.AppendLine("mp_startmoney 16000");
            sb.AppendLine($"mp_freezetime {seconds}");
            sb.AppendLine($"mp_buytime {seconds}");
            sb.AppendLine("mp_buy_anywhere 1");
        }
        else if (MaxMoney && !OwnsLoadout)
        {
            sb.AppendLine("mp_maxmoney 65535");
            sb.AppendLine("mp_startmoney 65535");
            sb.AppendLine("mp_afterroundmoney 65535");
            sb.AppendLine("mp_buy_anywhere 1");
            sb.AppendLine("mp_buytime 9999");
        }

        if (NoRoundEnd && !OwnsLoadout)
        {
            sb.AppendLine("mp_ignore_round_win_conditions 1");
            sb.AppendLine("mp_roundtime 60");
            sb.AppendLine("mp_roundtime_defuse 60");
            sb.AppendLine("mp_freezetime 0");
            sb.AppendLine("mp_respawn_on_death_t 1");
            sb.AppendLine("mp_respawn_on_death_ct 1");
        }

        if (RoundTime > 0 && !OwnsLoadout)
        {
            sb.AppendLine($"mp_roundtime {RoundTime}");
            sb.AppendLine($"mp_roundtime_defuse {RoundTime}");
            sb.AppendLine($"mp_roundtime_hostage {RoundTime}");
        }
        if (FriendlyFire) sb.AppendLine("mp_friendlyfire 1");

        foreach (var line in HackerCommands(withCvars: false)) sb.AppendLine(line);
        sb.AppendLine($"lst_show_damage {(ShowDamage ? 1 : 0)}");
        sb.AppendLine($"lst_retake_buy {(UsesRetakeBuy ? 1 : 0)} {Math.Clamp(RetakeBuySeconds, 4, 20)}");

        // sv_skirmish_id survives map changes. 12 is Valve's Retakes. Every other mode, including the
        // plugin, clears it. Don't end warmup on either retake style: Valve's cards and the plugin's
        // own round setup both need that warmup.
        sb.AppendLine($"sv_skirmish_id {(IsValveRetakes ? 12 : 0)}");
        if (IsValveRetakes)
        {
            sb.AppendLine("mp_retake_t_count 3");
            sb.AppendLine("mp_retake_ct_count 4");
        }
        else if (IsPluginRetakes)
        {
            sb.AppendLine("retakes_enabled 1");
            sb.AppendLine($"mp_c4timer {Math.Clamp(RetakeBombTimer, 15, 60)}");
            if (!UsesRetakeBuy) sb.AppendLine("mp_freezetime 1");
        }
        else
        {
            sb.AppendLine("retakes_enabled 0");
            sb.AppendLine("mp_warmup_end");
        }
        return sb.ToString();
    }

    /// <summary>
    /// What has to match for the running retakes plugins. Buy time and the bomb timer are ordinary
    /// cvars and are not included: those apply on the next round or map. Weapon choice and instadefuse
    /// are included because they load or unload a plugin.
    /// </summary>
    public string PluginRetakeKey()
    {
        if (!IsPluginRetakes) return IsValveRetakes ? "valve" : "off";
        return string.Join('|', RetakeStyle, Math.Clamp(RetakeMaxPlayers, 2, 10), Math.Clamp(RetakeTerroristPercent, 10, 80),
            RetakeScramble, Math.Clamp(RetakeScrambleRounds, 1, 30), RetakeBalance, RetakeAutoPlant, RetakeAnnounce, RetakeAutoJoin,
            RetakeWeapons == 1 ? 1 : 0, RetakeInstadefuse);
    }

    /// <summary>Arms Race, Demolition and both retake styles bring their own guns, money and round length.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool OwnsLoadout => IsRetakes || Mode is "Arms Race" or "Demolition";

    /// <summary>
    /// Writes configs/plugins/RetakesPlugin/RetakesPlugin.json, keeping any keys this launcher doesn't
    /// own (command permissions, a newer config version). The plugin reads this when it loads.
    /// </summary>
    public void WriteRetakesConfig(GameFolder game)
    {
        var path = Path.Combine(game.Addons, "counterstrikesharp", "configs", "plugins", "RetakesPlugin", "RetakesPlugin.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonNode root;
        try
        {
            root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject() : new JsonObject();
        }
        catch (System.Text.Json.JsonException)
        {
            root = new JsonObject();
        }

        var players = Math.Clamp(RetakeMaxPlayers, 2, 10);
        var ratio = Math.Clamp(RetakeTerroristPercent, 10, 80) / 100.0;
        Set(root, "GameSettings", "MaxPlayers", players);
        Set(root, "GameSettings", "MinimumPlayers", 0);
        Set(root, "GameSettings", "ShouldBreakBreakables", false);
        Set(root, "GameSettings", "ShouldOpenDoors", false);
        // Buy mode keeps the plugin's rifles so bots stay armed (humans are stripped and buy).
        // The allocator replaces that hand-out, so fallback allocation stays off while it is loaded.
        Set(root, "GameSettings", "EnableFallbackAllocation", RetakeWeapons != 1);
        Set(root, "TeamSettings", "TerroristRatio", ratio);
        Set(root, "TeamSettings", "RoundsToScramble", Math.Clamp(RetakeScrambleRounds, 1, 30));
        Set(root, "TeamSettings", "IsScrambleEnabled", RetakeScramble);
        Set(root, "TeamSettings", "IsBalanceEnabled", RetakeBalance);
        Set(root, "TeamSettings", "ShouldPreventTeamChangesMidRound", true);
        Set(root, "MapConfigSettings", "EnableBombsiteAnnouncementCenter", RetakeAnnounce);
        Set(root, "BombSettings", "IsAutoPlantEnabled", RetakeAutoPlant);
        Set(root, "QueueSettings", "ShouldAutoJoinGame", RetakeAutoJoin);
        Set(root, "QueueSettings", "ShouldAutoJoinSpectators", !RetakeAutoJoin);
        if (root["ConfigVersion"] == null) root["ConfigVersion"] = 2;

        File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Set(JsonNode root, string section, string key, JsonNode? value)
    {
        if (root[section] is not JsonObject obj)
        {
            obj = new JsonObject();
            root[section] = obj;
        }
        obj[key] = value;
    }

    /// <summary>One line for a saved quick-start tile.</summary>
    public string Summary()
    {
        var mode = IsPluginRetakes ? "Plugin retakes" : Mode;
        return $"{mode} · {Map} · {(Bots == 0 ? "no bots" : Bots + " bots")}";
    }

    /// <summary>A detached copy (saved setups must not keep changing with the Play page).</summary>
    public ServerSettings Clone()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(this);
        return System.Text.Json.JsonSerializer.Deserialize<ServerSettings>(json) ?? new ServerSettings();
    }

    /// <summary>
    /// Copies a saved quick start onto this server. Friends, password and shared-skins stay as they are:
    /// those live on the Friends page, not in a game setup.
    /// </summary>
    public void CopyPracticeFrom(ServerSettings source)
    {
        foreach (var prop in typeof(ServerSettings).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (prop.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() != null) continue;
            if (prop.Name is nameof(Friends) or nameof(Password) or nameof(SharedSkins)) continue;
            prop.SetValue(this, prop.GetValue(source));
        }
    }

    /// <summary>Zero recoil / zero spread cvars (cheat-protected; the "off" values are CS2's defaults).</summary>
    private IEnumerable<string> RecoilCvars()
    {
        // The plugin also removes the recoil per hacker; these make it stick for everyone in hacker mode.
        var noRecoil = HackerMode != 0 && HackNoRecoil;
        yield return $"weapon_recoil_scale {(noRecoil ? 0 : 2)}";
        yield return $"weapon_recoil_view_punch_extra {(noRecoil ? "0" : "0.055")}";
        yield return $"weapon_recoil_suppression_shots {(noRecoil ? 999 : 4)}";
        yield return $"weapon_accuracy_nospread {(HackerMode != 0 && HackNoSpread ? 1 : 0)}";
    }

    /// <summary>Everything hacker mode needs, without touching bots or the round (for live changes).</summary>
    public IEnumerable<string> HackerCommands(bool withCvars = true)
    {
        if (withCvars)
        {
            yield return "sv_cheats 1";
            foreach (var line in RecoilCvars()) yield return line;
            yield return $"sv_cheats {(NeedsCheats ? 1 : 0)}";
        }
        var powers = new List<string>();
        if (HackWallhack) powers.Add("wallhack");
        if (HackAimbot) powers.Add("aimbot");
        if (HackOneTap) powers.Add("onetap");
        if (HackAmmo) powers.Add("ammo");
        if (HackSpeed) powers.Add("speed");
        if (HackBhop) powers.Add("bhop");
        if (HackNoFlash) powers.Add("noflash");
        if (HackEspInfo) powers.Add("espinfo");
        if (HackAimWalls) powers.Add("aimwalls");
        if (HackNoRecoil) powers.Add("norecoil");
        if (HackRadar) powers.Add("radar");
        yield return $"lst_hack_powers {(powers.Count > 0 ? string.Join(",", powers) : "none")}";
        yield return $"lst_aim_aggression {Math.Clamp(HackAggression, 1, 10)}";
        yield return $"lst_aim_target {(HackAimTarget == 1 ? "crosshair" : "closest")}";
        yield return $"lst_aim_key {AimKeyCommands[Math.Clamp(HackAimKey, 0, AimKeyCommands.Length - 1)]}";
        yield return $"lst_hack {HackerMode switch { 1 => "all", 2 => "one", _ => "off" }}";
    }

    /// <summary>Sends the hacker settings to the running server right away (and keeps them for map changes).</summary>
    public void ApplyHackerToRunningServer(GameFolder game)
    {
        WriteCfg(game);
        SendCommands(game, HackerCommands().ToArray());
    }

    public void WriteCfg(GameFolder game)
    {
        var cfgDir = Path.Combine(game.Csgo, "cfg");
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "lanskintester_server.cfg"), ToCfg());
    }

    /// <summary>
    /// Applies the settings to a running server through the plugin. Changing mode or map reloads the map
    /// (the plugin then runs the cfg again); otherwise the cfg is just re-run. Returns a warning when
    /// the new Workshop map can't load on this server, else null.
    /// </summary>
    public string? ApplyToRunningServer(GameFolder game, bool mapOrModeChanged, bool serverOnline, bool forFriends)
    {
        if (mapOrModeChanged) PrepareWorkshopMap(game, forFriends);
        if (WorkshopNeedsDownload && !serverOnline)
            return "This Workshop map isn't downloaded and the server is in offline LAN mode, so it can't fetch it. " +
                   "Press Subscribe (Steam downloads it, then Apply again), or Stop and Start so the server goes online to download it.";

        WriteCfg(game);
        var mode = GetMode();
        if (mapOrModeChanged)
        {
            // Skirmish id and retake sizes first: the map load reads them, and a later cfg exec is too late.
            var beforeMap = new List<string> { $"sv_skirmish_id {(IsValveRetakes ? 12 : 0)}" };
            if (IsValveRetakes) beforeMap.AddRange(["mp_retake_t_count 3", "mp_retake_ct_count 4"]);
            beforeMap.Add($"game_type {mode.GameType}");
            beforeMap.Add($"game_mode {mode.Mode}");
            beforeMap.Add(MapCommand(running: true));
            SendCommands(game, beforeMap.ToArray());
        }
        else
            SendCommands(game, "exec lanskintester_server.cfg");
        return null;
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
