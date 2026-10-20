using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
        // CS2 dropped mode 0/3 (it starts casual): run competitive and let the plugin enforce one buy per gun.
        new(WeaponsExpertMode, 0, 1, DefuseMaps),
        new("Custom", 3, 0, DefuseMaps),
        // Valve's own Retakes: game_type 0 / game_mode 5, also selected by sv_skirmish_id 12.
        new(RetakesMode, 0, 5, ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_ancient", "de_anubis", "de_vertigo", "de_overpass", "de_train"]),
    ];

    public const string RetakesMode = "Retakes";
    public const string RushMode = "Rush";
    public const string WeaponsExpertMode = "Weapons Expert";

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

    [JsonIgnore]
    public bool IsRetakes => Mode == RetakesMode;

    /// <summary>0 = Valve's built-in retakes, 1 = B3none's cs2-retakes plugin.</summary>
    public int RetakeStyle { get; set; }

    public static readonly string[] RetakeStyles = ["Valve", "Plugin"];

    [JsonIgnore]
    public bool IsPluginRetakes => IsRetakes && RetakeStyle == 1;

    [JsonIgnore]
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

    /// <summary>How often a plugin retakes round is a full buy, a force buy or an eco (weights, e.g. 100/0/0 = full buys only).</summary>
    public int RetakeFullPercent { get; set; } = 100;
    public int RetakeForcePercent { get; set; }
    public int RetakeEcoPercent { get; set; }

    /// <summary>Plugin retakes: instadefuse when nothing can stop the defuse.</summary>
    public bool RetakeInstadefuse { get; set; } = true;

    [JsonIgnore]
    public bool UsesAllocator => IsPluginRetakes && RetakeWeapons == 1;

    [JsonIgnore]
    public bool UsesRetakeBuy => IsPluginRetakes;

    /// <summary>Seconds of freeze time to buy, at the start of a retakes round.</summary>
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

    /// <summary>Most players per side in classic modes (people and bots).</summary>
    public int TeamSizeT { get; set; } = 5;
    public int TeamSizeCT { get; set; } = 5;

    /// <summary>Per map (Workshop id or map name): team sizes {T, CT} saved with "Save for this map".</summary>
    public Dictionary<string, int[]> MapTeamSizes { get; set; } = new();

    /// <summary>Per Workshop map id: the game mode it was last played in. A map not in here starts in Custom.</summary>
    public Dictionary<string, string> MapModes { get; set; } = new();

    [JsonIgnore]
    public string MapKey => WorkshopMapId ?? Map;

    public void RememberMapMode() { if (WorkshopMapId is { } id) MapModes[id] = Mode; }

    // Custom by default: Workshop maps are mostly built for it (movement hubs only spawn you there).
    public void RecallMapMode()
    {
        if (WorkshopMapId is { } id) Mode = MapModes.TryGetValue(id, out var m) && Modes.Any(x => x.Name == m) ? m : "Custom";
    }
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

    /// <summary>Built-in server plugins (lst_qol): kept across quick starts, they're how you like your server.</summary>
    public bool SpawnProtection { get; set; } = true;
    public bool GunMenu { get; set; } = true;
    public bool ReadyUp { get; set; }
    public bool MapVote { get; set; } = true;
    public bool RoundSummary { get; set; } = true;
    public bool WelcomeMessage { get; set; } = true;

    /// <summary>Downloaded plugins: Bot Hider (bots look like players) and NockyCZ's Deathmatch (Deathmatch mode only).</summary>
    public bool BotHider { get; set; }
    public bool DeathmatchPlugin { get; set; }

    /// <summary>Downloaded bot plugins: ed0ard's Bot Improver suite (includes BotAI) and XBribo's Bot Controller.</summary>
    public bool BotImprover { get; set; } = true;
    public bool BotController { get; set; }

    /// <summary>Bot Improver's console commands: bot_aim, bot_nades and the forced weapon ("ak47"; "" = normal buying).</summary>
    public static readonly string[] BotAimModes = ["mixed", "head", "body"];
    public static readonly string[] BotNadeModes = ["off", "less", "normal", "more", "max"];
    public static readonly string[] BotForcedWeapons =
    [
        "elite", "p250", "fn57", "deagle", "cz75a", "r8", "bizon", "p90", "mp5sd", "mp9", "mp7", "mac10", "ump45",
        "mag7", "sawedoff", "nova", "xm1014", "famas", "galilar", "m4a1", "m4a1s", "ak47", "aug", "sg556",
        "ssg08", "awp", "scar20", "g3sg1", "negev", "m249",
    ];
    public int BotAim { get; set; }
    public int BotNades { get; set; } = 2;
    public string BotForceWeapon { get; set; } = "";

    /// <summary>Bot Improver's random bot skins, knives, gloves and agents. Off: bots look like normal CS2 bots.</summary>
    public bool BotSkins { get; set; }

    /// <summary>Bots jump at most once every few seconds (no bunny-hopping around).</summary>
    public bool CalmBots { get; set; } = true;

    /// <summary>Bot Improver settings as console commands; none while it is off (its commands only exist when it is loaded).</summary>
    public IEnumerable<string> BotImproverCommands()
    {
        if (!BotImprover) yield break;
        yield return $"bot_aim {Pick(BotAimModes, BotAim)}";
        yield return $"bot_nades {Pick(BotNadeModes, BotNades)}";
    }

    /// <summary>
    /// What Bot Improver's weapon "commands" (ak47, awp, bot_buy...) do. They are aliases from one of its
    /// cfg files, which the launcher doesn't install, and each one only sets the engine's bot_loadout:
    /// the items every bot gets at round start. Null for an unknown weapon.
    /// </summary>
    public static string? BotLoadoutFor(string weapon) => weapon switch
    {
        "elite" or "p250" or "fn57" or "deagle" or "cz75a" => $"{weapon} vesthelm defuser",
        "r8" => "revolver vesthelm defuser",
        "m4a1s" => "m4a1_silencer tec9 vesthelm defuser",
        _ when BotForcedWeapons.Contains(weapon) => $"{weapon} tec9 vesthelm defuser",
        _ => null,
    };

    /// <summary>Every bot gets one weapon (Bot Improver's "Bots Buy").</summary>
    [JsonIgnore]
    public bool ForcesBotWeapon => BotImprover && BotLoadoutFor(BotForceWeapon) != null;

    private bool ForcesBotSniper => ForcesBotWeapon && BotForceWeapon is "awp" or "ssg08" or "scar20" or "g3sg1";

    /// <summary>bot_loadout is cheat-protected: it goes inside the cfg's sv_cheats block, and keeps sv_cheats on.</summary>
    public string BotLoadoutCommand() => $"bot_loadout \"{(ForcesBotWeapon ? BotLoadoutFor(BotForceWeapon) : "")}\"";

    /// <summary>Changes the forced weapon on a running server, leaving sv_cheats as the cfg does.</summary>
    public string[] BotWeaponCommands() => ["sv_cheats 1", BotLoadoutCommand(), $"sv_cheats {B(NeedsCheats)}"];

    /// <summary>
    /// Flying Scoutsman (Bot Improver's scouts_on / scouts_off, also cfg aliases): scouts and knives, low
    /// gravity, no spread. The Live Server buttons exec these files.
    /// </summary>
    public const string ScoutsOnCfg = "lanskintester_scouts_on.cfg";
    public const string ScoutsOffCfg = "lanskintester_scouts_off.cfg";

    public static readonly string[] ScoutsOnCommands =
    [
        "sv_cheats 1", // weapon_accuracy_nospread is cheat-protected
        "mp_ct_default_primary weapon_ssg08", "mp_t_default_primary weapon_ssg08",
        "mp_ct_default_secondary \"\"", "mp_t_default_secondary \"\"",
        "weapon_accuracy_nospread 1", "sv_enablebunnyhopping 1", "sv_gravity 230", "sv_airaccelerate 20000",
        "sv_maxspeed 420", "sv_friction 4", "mp_maxmoney 0", "mp_free_armor 2", "mp_roundtime 3",
        "mp_give_player_c4 0", "mp_freezetime 3", "mp_default_team_winner_no_objective 0",
        "bot_defense_rush_chance 100", "nav_path_fixup_gap_segments 1", "mp_restartgame 1",
    ];

    /// <summary>Back to the engine defaults for what the mode's config doesn't set; reloading the map does the rest.</summary>
    public static readonly string[] ScoutsOffCommands =
    [
        "mp_ct_default_primary \"\"", "mp_t_default_primary \"\"",
        "mp_ct_default_secondary weapon_hkp2000", "mp_t_default_secondary weapon_glock",
        "weapon_accuracy_nospread 0", "sv_maxspeed 320", "sv_friction 5.2", "mp_free_armor 0", "mp_give_player_c4 1",
        "mp_default_team_winner_no_objective -1", "bot_defense_rush_chance 0", "nav_path_fixup_gap_segments 0",
    ];

    /// <summary>Rules (reset by quick starts): only headshots hurt, and only pistols can be bought or picked up.</summary>
    public bool HeadshotOnly { get; set; }
    public bool PistolsOnly { get; set; }

    /// <summary>A kill refills the gun's magazine at once, and the spare ammo never runs out.</summary>
    public bool ReloadOnKill { get; set; }

    /// <summary>Everyone (bots too) gets only this gun and a knife: weapon_awp, weapon_ssg08, weapon_deagle, knife, or "".</summary>
    public string OnlyWeapon { get; set; } = "";
    public static readonly (string Value, string Label)[] OnlyWeapons =
        [("", "Any guns"), ("weapon_awp", "AWP only"), ("weapon_ssg08", "Scout only"), ("weapon_deagle", "Deagle only"), ("knife", "Knives only")];

    /// <summary>Low gravity and fast air movement (Flying Scoutsman style).</summary>
    public bool LowGravity { get; set; }

    /// <summary>!skin, !gen, !knife and !gloves in chat. Off in "unbox only" mode (no admin code on this PC).</summary>
    public static bool SkinCommands { get; set; } = true;

    /// <summary>Server helpers: dropped guns disappear after 15 s, no fall damage, everyone bunnyhops by holding jump.</summary>
    public bool CleanupGuns { get; set; } = true;
    public bool NoFallDamage { get; set; }
    public bool AutoBhop { get; set; }

    [JsonIgnore]
    public bool UsesDeathmatchPlugin => DeathmatchPlugin && Mode == "Deathmatch";

    /// <summary>Round length in minutes; 0 = the game mode's default.</summary>
    public int RoundTime { get; set; }
    public bool FriendlyFire { get; set; }

    /// <summary>Steam Workshop map id (overrides Map). Needs internet: the server downloads it.</summary>
    public string WorkshopMap { get; set; } = "";

    /// <summary>
    /// A Workshop map keeps its own config (aim_botz and other training maps set bots, buying and rounds
    /// themselves): the launcher's config then only sets the network and switches its own plugin's extras off.
    /// </summary>
    public bool KeepMapRules { get; set; }

    [JsonIgnore]
    public bool UsesMapRules => KeepMapRules && WorkshopMapId != null;

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
    [JsonIgnore]
    public bool PrivateServer => Friends != FriendsNetwork.Lan || WorkshopNeedsDownload;

    [JsonIgnore]
    public bool WorkshopNeedsDownload => WorkshopMapId != null;

    /// <summary>
    /// Workshop maps always load the official way (host_workshop_map, server online). Copying the
    /// downloaded .vpk into csgo\maps broke maps that Steam stores as a split archive (_dir/_000)
    /// and crashed the server, so that is no longer done; copies an older launcher made are cleaned up.
    /// </summary>
    public static void PrepareWorkshopMap(GameFolder game)
    {
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
        WorkshopMapId != null && running ? $"host_workshop_map {WorkshopMapId}" : $"{(running ? "changelevel" : "map")} {Map}";

    /// <summary>Everyone uses the launcher's loadout (off = each friend picks skins in chat; the host keeps the launcher's).</summary>
    public bool SharedSkins { get; set; } = true;

    /// <summary>Optional server password for friends.</summary>
    public string Password { get; set; } = "";

    /// <summary>A password safe in a server command and on CS2's command line: no quotes or line breaks.</summary>
    public static string CleanPassword(string? text) => new string((text ?? "").Where(c => c != '"' && !char.IsControl(c)).ToArray()).Trim();

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

    /// <summary>3D box around players, a pose skeleton, and a name / HP / gear plate. Chams is <see cref="HackWallhack"/>.</summary>
    public bool HackBoxEsp { get; set; }
    public bool HackBoneEsp { get; set; }
    public bool HackTags { get; set; }

    /// <summary>Fires when an enemy crosses the crosshair, after a short random delay.</summary>
    public bool HackTrigger { get; set; }

    public static readonly string[] AimKeys = ["Hold fire (Mouse 1)", "Always on", "Hold right click", "Hold Shift", "Hold E", "Hold F", "Only my own key"];
    private static readonly string[] AimKeyCommands = ["fire", "always", "mouse2", "shift", "e", "f", "custom"];

    public static readonly string[] AimTargets = ["Closest enemy", "Nearest crosshair", "Lowest HP", "Highest threat"];
    private static readonly string[] AimTargetCommands = ["closest", "crosshair", "health", "threat"];

    /// <summary>Who the aimbot goes for (index into <see cref="AimTargets"/>).</summary>
    public int HackAimTarget { get; set; }

    public static readonly string[] AimStyles = ["Legit", "Silent", "Rage"];
    private static readonly string[] AimStyleCommands = ["legit", "silent", "rage"];

    /// <summary>How the aimbot moves: 0 smooth, 1 bullet-only, 2 snap and shoot.</summary>
    public int HackAimStyle { get; set; }

    public static readonly string[] AimBones = ["Head", "Neck", "Chest"];
    private static readonly string[] AimBoneCommands = ["head", "neck", "chest"];

    /// <summary>Which body part the aimbot and triggerbot use.</summary>
    public int HackAimBone { get; set; }

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
        new("Competitive 5v5", "9 hard bots · normal rules", s =>
        {
            s.Reset("Competitive", "de_mirage");
            s.Bots = 9;
            s.Difficulty = 2;
            s.MaxMoney = false;
        }),
        new("Free-for-all Deathmatch", "Everyone vs everyone · 10 hard bots", s =>
        {
            s.Reset("Deathmatch", "de_mirage");
            s.Bots = 10;
            s.Difficulty = 2;
            s.FreeForAll = true;
        }),
        new("Valve Retakes", "Valve's retakes · 3v4 · loadout cards", s =>
        {
            s.Reset(RetakesMode, "de_mirage");
            s.Bots = 6; // you + 6 bots fills the 3 T vs 4 CT match
            s.Difficulty = 2;
            s.MaxMoney = false;
            s.RetakeStyle = 0;
        }),
        new("Plugin Retakes", "Retakes plugin · you buy the guns · 8 bots", s =>
        {
            s.Reset(RetakesMode, "de_mirage");
            s.Bots = 8;
            s.Difficulty = 2;
            s.MaxMoney = false;
            s.RetakeStyle = 1;
            s.RetakeWeapons = 0;
            s.RetakeBuySeconds = 12;
            s.RetakeInstadefuse = true;
        }),
    ];

    /// <summary>Back to plain defaults for a mode (keeps launch options and auto-start).</summary>
    private void Reset(string mode, string map)
    {
        Mode = mode;
        Map = map;
        WorkshopMap = "";
        KeepMapRules = false;
        BotTeam = BotWeapons = RoundTime = 0;
        FreeForAll = BotsDontShoot = InfiniteAmmo = NoRoundEnd = FriendlyFire = HeadshotOnly = PistolsOnly = ReloadOnKill = LowGravity = false;
        OnlyWeapon = "";
        // Every quick start uses the smarter bots (and keeps their default looks).
        BotImprover = true;
        BotSkins = false;
        CalmBots = true;
        MaxMoney = AllowCheats = true;
        ShowDamage = false;
        RetakeStyle = 0;
        RetakeMaxPlayers = 9;
        RetakeTerroristPercent = 45;
        RetakeWeapons = 0;
        RetakeScramble = RetakeBalance = RetakeAutoPlant = RetakeAnnounce = RetakeAutoJoin = RetakeInstadefuse = true;
        (RetakeFullPercent, RetakeForcePercent, RetakeEcoPercent) = (100, 0, 0);
        RetakeScrambleRounds = 5;
        RetakeBuySeconds = 12;
        RetakeBombTimer = 40;
        HackerMode = 0;
        HackWallhack = HackAimbot = HackAmmo = true;
        HackOneTap = HackSpeed = HackBhop = HackNoFlash = HackEspInfo = HackAimWalls = HackNoRecoil = HackNoSpread = HackRadar = false;
        HackBoxEsp = HackBoneEsp = HackTags = HackTrigger = false;
        HackAimStyle = HackAimBone = 0;
        HackAggression = 5;
    }

    /// <summary>
    /// A Workshop map from the manager runs as shipped: stock rules, no bots, and no add-on plugins except the
    /// skins plugin (extras like gun menus and bot plugins can cover or break a map's own menu). Friends,
    /// password and launch options carry over, and so do the remembered per-map modes and team sizes.
    /// </summary>
    public static ServerSettings ForWorkshop(ServerSettings old, string id)
    {
        var s = Stock(old);
        s.WorkshopMap = id;
        s.RecallMapMode();
        return s;
    }

    /// <summary>
    /// A plain CS2 server: Casual on Dust II, no bots, stock money and rules, no cheats and no add-on plugins
    /// except the skins plugin. Friends, password, launch options and the remembered per-map settings carry over.
    /// </summary>
    public static ServerSettings Stock(ServerSettings old) => new()
    {
        Friends = old.Friends, Password = old.Password, SharedSkins = old.SharedSkins, GameArgs = old.GameArgs,
        AutoStartGame = old.AutoStartGame, AimBindKey = old.AimBindKey, MapModes = old.MapModes, MapTeamSizes = old.MapTeamSizes,
        Mode = "Casual", Bots = 0, KeepMapRules = true, BotImprover = false, CalmBots = false, MaxMoney = false, AllowCheats = false,
        SpawnProtection = false, GunMenu = false, MapVote = false, RoundSummary = false, WelcomeMessage = false, CleanupGuns = false,
    };

    /// <summary>
    /// A mode that fits a Workshop map from its title and tags, or null (it then starts in Custom: surf, kz and
    /// movement hubs only spawn you there).
    /// </summary>
    public static string? GuessMode(string title, IEnumerable<string> tags)
    {
        var text = (title + " " + string.Join(' ', tags)).ToLowerInvariant();
        if (Regex.IsMatch(text, @"(\bar_|arms ?race|gun ?game)")) return "Arms Race";
        if (Regex.IsMatch(text, @"(\b(aim|awp|dm|1v1)_|deathmatch)")) return "Deathmatch";
        if (Regex.IsMatch(text, @"(\b(de|cs)_|defuse|hostage|bomb ?site)")) return "Casual";
        return null;
    }

    public GameMode GetMode()
    {
        var mode = Modes.FirstOrDefault(m => m.Name == Mode) ?? Modes[0];
        // Plugin retakes runs on competitive rules. Valve's mode is game_type 0 / game_mode 5.
        if (IsPluginRetakes) return mode with { GameType = 0, Mode = 1 };
        return mode;
    }

    private bool HackRecoilOrSpread => HackerMode != 0 && (HackNoRecoil || HackNoSpread);

    private bool NeedsCheats => AllowCheats || InfiniteAmmo || ReloadOnKill || LowGravity || AutoBhop || BotsDontShoot || HackRecoilOrSpread
                                || ForcesBotWeapon;

    public string StartArguments()
    {
        var mode = GetMode();
        // -condebug: the server writes csgo\console.log, which the launcher shows if the server crashes.
        // sv_hibernate_when_empty 0: an empty server otherwise sleeps after a few seconds, and then the
        // plugin can't run the launcher's commands (e.g. switching to a Workshop map) until someone joins.
        // sv_skirmish_id survives map changes. 12 is Valve's Retakes war game; every other mode clears it.
        // Retake team sizes have to be set before the map loads (they decide the slot count).
        // Custom has no player count of its own: without -maxplayers the server has 0 slots and nobody can pick a team.
        var skirmish = IsValveRetakes ? 12 : 0;
        var retake = IsValveRetakes ? " +mp_retake_t_count 3 +mp_retake_ct_count 4" : "";
        return $"-dedicated -insecure -condebug +sv_hibernate_when_empty 0 +sv_lan {B(!PrivateServer)} -port {Port} +game_type {mode.GameType} +game_mode {mode.Mode} +sv_skirmish_id {skirmish}{retake}{(NeedsPlayerSlots ? " -maxplayers 64" : "")} +{MapCommand(running: false)}";
    }

    /// <summary>Custom mode: the server must start with -maxplayers (switching to it on a running server needs a restart).</summary>
    [JsonIgnore]
    public bool NeedsPlayerSlots => GetMode().GameType == 3;

    /// <summary>The Workshop id from an id or a steamcommunity.com/sharedfiles/...?id=123 link.</summary>
    public string? WorkshopMapId
    {
        get
        {
            var m = WorkshopIdPattern.Match(WorkshopMap ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }
    }

    private static readonly Regex WorkshopIdPattern = new(@"(\d{6,})");

    public const int Port = 27015;

    /// <summary>127.0.0.1, not "localhost": that can resolve to IPv6 (::1), where the server doesn't listen.</summary>
    public string GameArguments(GameFolder game) => JoinArguments(game, $"127.0.0.1:{Port}", Password);

    /// <summary>CS2 with VAC off for this run only (nothing is saved in Steam), joining a server.</summary>
    public string JoinArguments(GameFolder game, string address, string? password)
    {
        var extra = (GameArgs ?? "").Replace("+connect", "").Trim();
        var pass = CleanPassword(password) is { Length: > 0 } clean ? $"+password \"{clean}\" " : "";
        var exec = WriteClientCfg(game) ? $"+exec {ClientCfg} " : "";
        // -netconport: the launcher can type in this CS2's console (sends your skins on a friend's server).
        return $"-insecure -netconport {ClientConsole.Port} -netconpassword {ClientConsole.Password} {extra} {exec}{pass}+connect {address}".Replace("  ", " ");
    }

    public const string ClientCfg = "lanskintester_client";

    /// <summary>A key name CS2 understands (mouse4, x, alt, f6, kp_5...), or null.</summary>
    public static string? CleanKey(string? key)
    {
        key = (key ?? "").Trim().ToLowerInvariant();
        return Regex.IsMatch(key, "^[a-z0-9_]{1,16}$") ? key : null;
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

    private static int B(bool on) => on ? 1 : 0;

    /// <summary>The entry at <paramref name="index"/>, clamped into the list.</summary>
    private static string Pick(string[] list, int index) => list[Math.Clamp(index, 0, list.Length - 1)];

    public string ToCfg()
    {
        var mode = GetMode();
        var cheats = NeedsCheats;
        var sb = new StringBuilder();
        sb.AppendLine("// Written by the LAN Skin Tester launcher - changes here are overwritten.");
        sb.AppendLine("sv_hibernate_when_empty 0"); // keep running with nobody connected (launcher commands, bots)
        sb.AppendLine($"sv_lan {B(!PrivateServer)}");
        sb.AppendLine($"lst_private_server {B(PrivateServer)}");
        sb.AppendLine($"lst_shared_loadout {B(SharedSkins)}");
        sb.AppendLine($"lst_host_account {ActiveSteamAccount()}");
        sb.AppendLine($"sv_password \"{(Password ?? "").Replace("\"", "").Trim()}\"");
        if (UsesMapRules) return MapRulesCfg(sb);

        // Cheat-protected settings need sv_cheats 1 while they're set (and turning it off resets them).
        sb.AppendLine("sv_cheats 1");
        // 1 = the magazine never empties; 2 = spare ammo never runs out (you still reload).
        sb.AppendLine($"sv_infinite_ammo {(InfiniteAmmo ? 1 : ReloadOnKill ? 2 : 0)}");
        sb.AppendLine($"bot_dont_shoot {B(BotsDontShoot)}");
        sb.AppendLine($"sv_gravity {(LowGravity ? 220 : 800)}");
        sb.AppendLine($"sv_airaccelerate {(LowGravity || AutoBhop ? 2000 : 12)}");
        sb.AppendLine($"sv_autobunnyhopping {B(AutoBhop)}");
        sb.AppendLine($"sv_enablebunnyhopping {B(AutoBhop)}");
        foreach (var line in RecoilCvars()) sb.AppendLine(line);
        sb.AppendLine(BotLoadoutCommand());
        sb.AppendLine($"sv_cheats {B(cheats)}");

        // Bots: difficulty only applies to newly added bots, so kick and re-add them.
        sb.AppendLine($"bot_difficulty {Math.Clamp(Difficulty, 0, 3)}");
        sb.AppendLine("bot_quota_mode normal");
        sb.AppendLine("bot_join_after_player 0");
        sb.AppendLine($"bot_join_team {(IsRetakes ? "any" : BotTeam switch { 1 => "T", 2 => "CT", _ => "any" })}");
        // Retakes, Arms Race and Demolition hand out their own guns. A forced sniper (Bot Improver) wins over Rifles.
        var rifles = !OwnsLoadout && BotWeapons == 0 && !ForcesBotSniper;
        sb.AppendLine($"bot_knives_only {B(BotWeapons == 3 && !OwnsLoadout)}");
        sb.AppendLine($"bot_pistols_only {B((BotWeapons == 1 || PistolsOnly) && !OwnsLoadout)}");
        sb.AppendLine($"bot_snipers_only {B(BotWeapons == 2 && !OwnsLoadout)}");
        foreach (var kind in new[] { "snipers", "shotguns", "sub_machine_guns", "machine_guns" })
            sb.AppendLine($"bot_allow_{kind} {B(!rifles)}");
        sb.AppendLine("bot_allow_rifles 1");
        sb.AppendLine("bot_allow_pistols 1");
        sb.AppendLine($"lst_bot_no_snipers {B(rifles)}"); // plugin safety net: swaps snipers for AK/M4
        // Bot Hider gives every bot a Steam identity; take the display name from that same identity, so the
        // name on the scoreboard matches the profile you see when you click the bot. (Only new bots: set
        // before the kick and re-add below.)
        if (BotHider || BotImprover) sb.AppendLine("bh_namesource 1");
        sb.AppendLine($"lst_bot_calm {B(CalmBots)}");
        sb.AppendLine("bot_kick");
        // Retakes only have so many seats: leave one for you. The plugin (lst_retake_seats) lets one more
        // bot go for every friend who connects, so everybody gets a team.
        var quota = Math.Clamp(Bots, 0, 31);
        var seats = RetakeSeats;
        if (seats > 0) quota = Math.Min(quota, Math.Max(0, seats - 1));
        sb.AppendLine($"bot_quota {quota}");
        sb.AppendLine($"lst_retake_seats {seats} {Math.Clamp(Bots, 0, 31)} {B(IsValveRetakes)}");
        // A side that already has more players must not grey out in the team menu: retakes picks the sides.
        sb.AppendLine($"mp_limitteams {(IsRetakes ? 0 : 2)}");
        // Team sizes only for classic rounds: deathmatch and arms race are free-for-all, retakes have seats.
        var sized = !IsRetakes && mode.GameType is 0 or 3;
        var (sizeT, sizeCT) = MapTeamSizes.TryGetValue(MapKey, out var saved) && saved.Length == 2 ? (saved[0], saved[1]) : (TeamSizeT, TeamSizeCT);
        sb.AppendLine($"lst_team_sizes {(sized ? Math.Clamp(sizeT, 1, 32) : 0)} {(sized ? Math.Clamp(sizeCT, 1, 32) : 0)}");

        sb.AppendLine($"mp_teammates_are_enemies {B(FreeForAll && mode is { GameType: 1, Mode: 2 })}");

        var buySeconds = Math.Clamp(RetakeBuySeconds, 4, 20);
        if (UsesRetakeBuy)
        {
            sb.AppendLine("mp_maxmoney 16000");
            sb.AppendLine("mp_startmoney 16000");
            sb.AppendLine($"mp_freezetime {buySeconds}");
            sb.AppendLine($"mp_buytime {buySeconds}");
            sb.AppendLine("mp_buy_anywhere 1");
        }
        else if (IsValveRetakes)
        {
            // Valve's cards give the guns; the buy menu stays open for changes at the start of the round.
            sb.AppendLine("mp_maxmoney 16000");
            sb.AppendLine("mp_startmoney 16000");
            sb.AppendLine("mp_buy_anywhere 1");
            sb.AppendLine($"mp_buytime {buySeconds}");
        }
        else if (MaxMoney && !OwnsLoadout)
        {
            foreach (var cvar in new[] { "mp_maxmoney", "mp_startmoney", "mp_afterroundmoney" }) sb.AppendLine($"{cvar} 65535");
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
            foreach (var cvar in new[] { "mp_roundtime", "mp_roundtime_defuse", "mp_roundtime_hostage" })
                sb.AppendLine($"{cvar} {RoundTime}");
        }
        if (FriendlyFire) sb.AppendLine("mp_friendlyfire 1");

        foreach (var line in HackerCommands(withCvars: false)) sb.AppendLine(line);
        sb.AppendLine($"lst_fire_relay {FireRelay.Port}");
        sb.AppendLine($"lst_skin_commands {B(SkinCommands)}"); // the triggerbot presses fire through the launcher on this PC
        foreach (var line in BotImproverCommands()) sb.AppendLine(line);
        sb.AppendLine($"lst_show_damage {B(ShowDamage)}");
        sb.AppendLine($"lst_hsonly {B(HeadshotOnly)}");
        sb.AppendLine($"lst_pistols {B(PistolsOnly)}");
        sb.AppendLine($"lst_reload_on_kill {B(ReloadOnKill)}");
        sb.AppendLine($"lst_only_weapon {(OnlyWeapon == "" ? "off" : OnlyWeapon)}");
        sb.AppendLine($"lst_cleanup {B(CleanupGuns)}");
        sb.AppendLine($"lst_nofall {B(NoFallDamage)}");
        // Greys the other guns out in the buy menu; the plugin also takes away any that get through.
        var prohibited = OnlyWeapon != "" ? ItemData.Weapons.Where(w => w.Value != OnlyWeapon && w.Key != 31).Select(w => w.Key)
            : PistolsOnly ? ItemData.Weapons.Keys.Where(d => !ItemData.Pistols.Contains(d) && d != 31) : Enumerable.Empty<ushort>();
        sb.AppendLine($"mp_items_prohibited \"{string.Join(",", prohibited)}\"");
        sb.AppendLine($"lst_qol spawn={B(SpawnProtection && !UsesDeathmatchPlugin)} guns={B(GunMenu && !UsesDeathmatchPlugin && !IsPluginRetakes)} ready={B(ReadyUp)} vote={B(MapVote)} " +
                      $"summary={B(RoundSummary)} welcome={B(WelcomeMessage)} maps={string.Join(',', mode.Maps)}");
        sb.AppendLine($"lst_retake_buy {B(UsesRetakeBuy)} {buySeconds} {B(UsesAllocator)} " +
                      $"{Math.Clamp(RetakeFullPercent, 0, 100)} {Math.Clamp(RetakeForcePercent, 0, 100)} {Math.Clamp(RetakeEcoPercent, 0, 100)}");
        sb.AppendLine($"lst_weapons_expert {B(Mode == WeaponsExpertMode)}");

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
    /// The map's own config decides bots, money, rounds and cheats: only the plugin's own settings are sent
    /// (its extras off unless picked on Live Server), so nothing the map set up gets kicked or restarted.
    /// </summary>
    private string MapRulesCfg(StringBuilder sb)
    {
        foreach (var line in HackerCommands(withCvars: false)) sb.AppendLine(line);
        sb.AppendLine($"lst_fire_relay {FireRelay.Port}");
        sb.AppendLine($"lst_skin_commands {B(SkinCommands)}");
        sb.AppendLine($"lst_show_damage {B(ShowDamage)}");
        sb.AppendLine($"lst_hsonly {B(HeadshotOnly)}");
        sb.AppendLine($"lst_pistols {B(PistolsOnly)}");
        sb.AppendLine($"lst_reload_on_kill {B(ReloadOnKill)}");
        sb.AppendLine($"lst_only_weapon {(OnlyWeapon == "" ? "off" : OnlyWeapon)}");
        sb.AppendLine($"lst_cleanup {B(CleanupGuns)}");
        sb.AppendLine($"lst_nofall {B(NoFallDamage)}");
        sb.AppendLine($"lst_qol spawn={B(SpawnProtection)} guns={B(GunMenu)} ready={B(ReadyUp)} vote={B(MapVote)} summary={B(RoundSummary)} welcome={B(WelcomeMessage)} maps=");
        sb.AppendLine("lst_retake_buy 0");
        sb.AppendLine("lst_weapons_expert 0");
        sb.AppendLine("lst_retake_seats 0");
        sb.AppendLine("lst_team_sizes 0 0");
        sb.AppendLine($"lst_bot_calm {B(CalmBots)}");
        sb.AppendLine("lst_bot_no_snipers 0");
        sb.AppendLine("sv_skirmish_id 0");
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

    /// <summary>Places in a retakes match (plugin: its MaxPlayers, Valve: 3 T + 4 CT), or 0 for other modes.</summary>
    [JsonIgnore]
    public int RetakeSeats => IsPluginRetakes ? Math.Clamp(RetakeMaxPlayers, 2, 10) : IsValveRetakes ? 7 : 0;

    /// <summary>Arms Race, Demolition and both retake styles bring their own guns, money and round length.</summary>
    [JsonIgnore]
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
        catch (JsonException)
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

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
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
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<ServerSettings>(json) ?? new ServerSettings();
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
            if (prop.GetCustomAttribute<JsonIgnoreAttribute>() != null) continue;
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
        yield return $"weapon_accuracy_nospread {B(HackerMode != 0 && HackNoSpread)}";
    }

    /// <summary>Everything hacker mode needs, without touching bots or the round (for live changes).</summary>
    public IEnumerable<string> HackerCommands(bool withCvars = true)
    {
        if (withCvars)
        {
            yield return "sv_cheats 1";
            foreach (var line in RecoilCvars()) yield return line;
            yield return $"sv_cheats {B(NeedsCheats)}";
        }
        var powers = new (bool On, string Name)[]
        {
            (HackWallhack, "wallhack"), (HackAimbot, "aimbot"), (HackOneTap, "onetap"), (HackAmmo, "ammo"), (HackSpeed, "speed"),
            (HackBhop, "bhop"), (HackNoFlash, "noflash"), (HackEspInfo, "espinfo"), (HackAimWalls, "aimwalls"), (HackNoRecoil, "norecoil"),
            (HackRadar, "radar"), (HackBoxEsp, "boxesp"), (HackBoneEsp, "boneesp"), (HackTags, "tags"), (HackTrigger, "trigger"),
        }.Where(p => p.On).Select(p => p.Name).ToList();
        yield return $"lst_hack_powers {(powers.Count > 0 ? string.Join(",", powers) : "none")}";
        yield return $"lst_aim_aggression {Math.Clamp(HackAggression, 1, 10)}";
        yield return $"lst_aim_target {Pick(AimTargetCommands, HackAimTarget)}";
        yield return $"lst_aim_style {Pick(AimStyleCommands, HackAimStyle)}";
        yield return $"lst_aim_bone {Pick(AimBoneCommands, HackAimBone)}";
        yield return $"lst_aim_key {Pick(AimKeyCommands, HackAimKey)}";
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
        File.WriteAllLines(Path.Combine(cfgDir, ScoutsOnCfg), ScoutsOnCommands);
        File.WriteAllLines(Path.Combine(cfgDir, ScoutsOffCfg), ScoutsOffCommands);
    }

    /// <summary>
    /// Applies the settings to a running server through the plugin. Changing mode or map reloads the map
    /// (the plugin then runs the cfg again); otherwise the cfg is just re-run. Returns a warning when
    /// the new Workshop map can't load on this server, else null.
    /// </summary>
    public string? ApplyToRunningServer(GameFolder game, bool mapOrModeChanged, bool serverOnline)
    {
        if (mapOrModeChanged) PrepareWorkshopMap(game);
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
        // Appended in one write: the plugin's move fails while the file is open, and a read-then-replace ran
        // commands twice when the plugin took the file in between.
        File.AppendAllText(Path.Combine(game.PluginDir, "server_commands.txt"), string.Join("\n", commands) + "\n");
    }
}
