//! Game mode, bots and practice options for the local server. Turned into csgo\cfg\lanskintester_server.cfg,
//! which the plugin runs after every map start (after the game mode's own config, so these win).

use crate::game::{self, GameFolder, GameMode};
use crate::items;
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::BTreeMap;
use std::fs;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{LazyLock, RwLock};

pub const RETAKES_MODE: &str = "Retakes";
pub const RUSH_MODE: &str = "Rush";
pub const WEAPONS_EXPERT_MODE: &str = "Weapons Expert";
pub const PORT: u16 = 27015;
pub const CLIENT_CFG: &str = "lanskintester_client";
pub const SCOUTS_ON_CFG: &str = "lanskintester_scouts_on.cfg";
pub const SCOUTS_OFF_CFG: &str = "lanskintester_scouts_off.cfg";

/// Friends network: how friends reach the server.
pub const LAN: i32 = 0;
pub const VPN: i32 = 1;
pub const INTERNET: i32 = 2;

pub const DIFFICULTIES: [&str; 4] = ["Easy", "Normal", "Hard", "Expert"];
/// Index 0 "Rifles" (no snipers, shotguns, SMGs or machine guns: with max money bots otherwise buy AWPs).
pub const BOT_WEAPON_CHOICES: [&str; 5] = [
    "Rifles",
    "Pistols only",
    "Snipers only",
    "Knives only",
    "Any weapons",
];
/// "Rifles" bots: which rifles they may keep (the plugin swaps the rest), as (label, lst_bot_rifles list).
pub const BOT_RIFLE_CHOICES: [(&str, &str); 5] = [
    ("AK-47 and M4s", "ak47,m4a1,m4a1_silencer"),
    ("AK-47 only", "ak47"),
    ("M4A4 only", "m4a1"),
    ("M4A1-S only", "m4a1_silencer"),
    (
        "All rifles",
        "ak47,m4a1,m4a1_silencer,famas,galilar,aug,sg556",
    ),
];
pub const BOT_TEAMS: [&str; 3] = ["Both teams", "Terrorists", "Counter-Terrorists"];
pub const RETAKE_STYLES: [&str; 2] = ["Valve", "Plugin"];
pub const BOT_AIM_MODES: [&str; 3] = ["mixed", "head", "body"];
pub const BOT_NADE_MODES: [&str; 5] = ["off", "less", "normal", "more", "max"];
pub const BOT_FORCED_WEAPONS: [&str; 30] = [
    "elite", "p250", "fn57", "deagle", "cz75a", "r8", "bizon", "p90", "mp5sd", "mp9", "mp7",
    "mac10", "ump45", "mag7", "sawedoff", "nova", "xm1014", "famas", "galilar", "m4a1", "m4a1s",
    "ak47", "aug", "sg556", "ssg08", "awp", "scar20", "g3sg1", "negev", "m249",
];
pub const ONLY_WEAPONS: [(&str, &str); 5] = [
    ("", "Any guns"),
    ("weapon_awp", "AWP only"),
    ("weapon_ssg08", "Scout only"),
    ("weapon_deagle", "Deagle only"),
    ("knife", "Knives only"),
];
pub const HACKER_MODES: [&str; 3] = ["Off", "Everyone", "One random"];
pub const AIM_KEYS: [&str; 7] = [
    "Hold fire (Mouse 1)",
    "Always on",
    "Hold right click",
    "Hold Shift",
    "Hold E",
    "Hold F",
    "Only my own key",
];
const AIM_KEY_COMMANDS: [&str; 7] = ["fire", "always", "mouse2", "shift", "e", "f", "custom"];
pub const AIM_TARGETS: [&str; 4] = [
    "Closest enemy",
    "Nearest crosshair",
    "Lowest HP",
    "Highest threat",
];
const AIM_TARGET_COMMANDS: [&str; 4] = ["closest", "crosshair", "health", "threat"];
pub const AIM_STYLES: [&str; 3] = ["Legit", "Silent", "Rage"];
const AIM_STYLE_COMMANDS: [&str; 3] = ["legit", "silent", "rage"];
pub const AIM_BONES: [&str; 3] = ["Head", "Neck", "Chest"];
const AIM_BONE_COMMANDS: [&str; 3] = ["head", "neck", "chest"];

/// Flying Scoutsman (Bot Improver's scouts_on / scouts_off were cfg aliases): the Live Server buttons exec these.
pub const SCOUTS_ON_COMMANDS: [&str; 20] = [
    "sv_cheats 1", // weapon_accuracy_nospread is cheat-protected
    "mp_ct_default_primary weapon_ssg08",
    "mp_t_default_primary weapon_ssg08",
    "mp_ct_default_secondary \"\"",
    "mp_t_default_secondary \"\"",
    "weapon_accuracy_nospread 1",
    "sv_enablebunnyhopping 1",
    "sv_gravity 230",
    "sv_airaccelerate 20000",
    "sv_maxspeed 420",
    "sv_friction 4",
    "mp_maxmoney 0",
    "mp_free_armor 2",
    "mp_roundtime 3",
    "mp_give_player_c4 0",
    "mp_freezetime 3",
    "mp_default_team_winner_no_objective 0",
    "bot_defense_rush_chance 100",
    "nav_path_fixup_gap_segments 1",
    "mp_restartgame 1",
];

/// Back to the engine defaults for what the mode's config doesn't set; reloading the map does the rest.
pub const SCOUTS_OFF_COMMANDS: [&str; 12] = [
    "mp_ct_default_primary \"\"",
    "mp_t_default_primary \"\"",
    "mp_ct_default_secondary weapon_hkp2000",
    "mp_t_default_secondary weapon_glock",
    "weapon_accuracy_nospread 0",
    "sv_maxspeed 320",
    "sv_friction 5.2",
    "mp_free_armor 0",
    "mp_give_player_c4 1",
    "mp_default_team_winner_no_objective -1",
    "bot_defense_rush_chance 0",
    "nav_path_fixup_gap_segments 0",
];

/// !skin, !gen, !knife and !gloves in chat. Off in "unbox only" mode (no admin code on this PC).
pub static SKIN_COMMANDS: AtomicBool = AtomicBool::new(true);

fn defuse_maps() -> Vec<String> {
    [
        "de_dust2",
        "de_mirage",
        "de_inferno",
        "de_nuke",
        "de_ancient",
        "de_anubis",
        "de_vertigo",
        "de_overpass",
        "de_train",
        "cs_office",
        "cs_italy",
    ]
    .map(String::from)
    .to_vec()
}

fn mode(name: &str, game_type: i32, mode: i32, maps: Vec<String>) -> GameMode {
    GameMode {
        name: name.into(),
        game_type,
        mode,
        maps,
    }
}

/// The modes the Play page offers; Rush is added once it is found in the game's files.
pub static MODES: LazyLock<RwLock<Vec<GameMode>>> = LazyLock::new(|| {
    let s = |m: &[&str]| m.iter().map(|x| x.to_string()).collect();
    RwLock::new(vec![
        mode("Deathmatch", 1, 2, defuse_maps()),
        mode("Competitive", 0, 1, defuse_maps()),
        mode("Casual", 0, 0, defuse_maps()),
        mode(
            "Wingman",
            0,
            2,
            s(&["de_inferno", "de_nuke", "de_vertigo", "de_overpass"]),
        ),
        mode(
            "Arms Race",
            1,
            0,
            s(&["ar_baggage", "ar_shoots", "ar_pool_day"]),
        ),
        mode(
            "Demolition",
            1,
            1,
            s(&["de_lake", "de_safehouse", "de_shortdust"]),
        ),
        // CS2 dropped mode 0/3 (it starts casual): run competitive and let the plugin enforce one buy per gun.
        mode(WEAPONS_EXPERT_MODE, 0, 1, defuse_maps()),
        mode("Custom", 3, 0, defuse_maps()),
        // Valve's own Retakes: game_type 0 / game_mode 5, also selected by sv_skirmish_id 12.
        mode(
            RETAKES_MODE,
            0,
            5,
            s(&[
                "de_dust2",
                "de_mirage",
                "de_inferno",
                "de_nuke",
                "de_ancient",
                "de_anubis",
                "de_vertigo",
                "de_overpass",
                "de_train",
            ]),
        ),
    ])
});

pub fn modes() -> Vec<GameMode> {
    MODES.read().unwrap().clone()
}

/// Adds modes the launcher only knows by name from the game's own files (Rush). Returns whether one was found.
pub fn discover_modes(game: &GameFolder) -> bool {
    if modes().iter().any(|m| m.name == RUSH_MODE) {
        return false;
    }
    let Some(rush) = game::find_game_mode(game, "rush", RUSH_MODE) else {
        return false;
    };
    MODES.write().unwrap().push(rush);
    true
}

/// Field names are the launcher.json format (older launchers wrote PascalCase, enums as numbers).
#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "PascalCase", default)]
pub struct ServerSettings {
    /// 0 = Valve's built-in retakes, 1 = B3none's cs2-retakes plugin.
    pub retake_style: i32,
    /// Plugin retakes: players in the match, including you (2-10).
    pub retake_max_players: i32,
    pub retake_terrorist_percent: i32,
    pub retake_scramble: bool,
    pub retake_scramble_rounds: i32,
    pub retake_balance: bool,
    pub retake_auto_plant: bool,
    pub retake_announce: bool,
    pub retake_auto_join: bool,
    /// 0 = freeze-time buy, 1 = the retakes allocator (pistol, half-buy, rifle).
    pub retake_weapons: i32,
    pub retake_full_percent: i32,
    pub retake_force_percent: i32,
    pub retake_eco_percent: i32,
    pub retake_instadefuse: bool,
    pub retake_buy_seconds: i32,
    pub retake_bomb_timer: i32,
    pub mode: String,
    pub map: String,
    pub bots: i32,
    pub team_size_t: i32,
    #[serde(rename = "TeamSizeCT")]
    pub team_size_ct: i32,
    /// Per map (Workshop id or map name): team sizes {T, CT} saved with "Save for this map".
    pub map_team_sizes: BTreeMap<String, Vec<i32>>,
    /// Per Workshop map id: the game mode it was last played in. A map not in here starts in Custom.
    pub map_modes: BTreeMap<String, String>,
    pub difficulty: i32,
    pub bot_team: i32,
    pub bot_weapons: i32,
    /// Index into BOT_RIFLE_CHOICES.
    pub bot_rifles: i32,
    pub free_for_all: bool,
    pub bots_dont_shoot: bool,
    pub infinite_ammo: bool,
    pub max_money: bool,
    pub no_round_end: bool,
    pub allow_cheats: bool,
    pub show_damage: bool,
    pub spawn_protection: bool,
    pub gun_menu: bool,
    pub ready_up: bool,
    pub map_vote: bool,
    pub round_summary: bool,
    pub welcome_message: bool,
    pub bot_hider: bool,
    pub deathmatch_plugin: bool,
    pub bot_improver: bool,
    pub bot_controller: bool,
    pub bot_aim: i32,
    pub bot_nades: i32,
    pub bot_force_weapon: String,
    pub bot_skins: bool,
    pub calm_bots: bool,
    pub headshot_only: bool,
    pub pistols_only: bool,
    pub reload_on_kill: bool,
    pub only_weapon: String,
    /// "" (off), "zombie" (infection) or "hns" (hide and seek): the plugin's fun modes, for classic rounds.
    pub fun_mode: String,
    pub low_gravity: bool,
    pub cleanup_guns: bool,
    pub no_fall_damage: bool,
    pub auto_bhop: bool,
    pub round_time: i32,
    pub friendly_fire: bool,
    pub workshop_map: String,
    pub keep_map_rules: bool,
    /// Experimental: a knife model from a Workshop addon (its id or link, and the .vmdl path inside it).
    pub custom_knife_addon: String,
    pub custom_knife_model: String,
    pub game_args: String,
    pub friends: i32,
    pub shared_skins: bool,
    pub password: String,
    pub hacker_mode: i32,
    pub hack_wallhack: bool,
    pub hack_aimbot: bool,
    pub hack_one_tap: bool,
    pub hack_ammo: bool,
    pub hack_speed: bool,
    pub hack_bhop: bool,
    pub hack_no_flash: bool,
    pub hack_esp_info: bool,
    pub hack_aim_walls: bool,
    pub hack_no_recoil: bool,
    pub hack_no_spread: bool,
    pub hack_radar: bool,
    pub hack_box_esp: bool,
    pub hack_bone_esp: bool,
    pub hack_tags: bool,
    pub hack_trigger: bool,
    pub hack_aim_target: i32,
    pub hack_aim_style: i32,
    pub hack_aim_bone: i32,
    pub hack_aim_key: i32,
    /// Any key bound to the aimbot in CS2 on this PC (mouse4, x, alt). Empty = none.
    pub aim_bind_key: String,
    pub hack_aggression: i32,
    pub auto_start_game: bool,
}

impl Default for ServerSettings {
    fn default() -> Self {
        Self {
            retake_style: 0,
            retake_max_players: 9,
            retake_terrorist_percent: 45,
            retake_scramble: true,
            retake_scramble_rounds: 5,
            retake_balance: true,
            retake_auto_plant: true,
            retake_announce: true,
            retake_auto_join: true,
            retake_weapons: 0,
            retake_full_percent: 100,
            retake_force_percent: 0,
            retake_eco_percent: 0,
            retake_instadefuse: true,
            retake_buy_seconds: 12,
            retake_bomb_timer: 40,
            mode: "Deathmatch".into(),
            map: "de_dust2".into(),
            bots: 10,
            team_size_t: 5,
            team_size_ct: 5,
            map_team_sizes: BTreeMap::new(),
            map_modes: BTreeMap::new(),
            difficulty: 3,
            bot_team: 0,
            bot_weapons: 0,
            bot_rifles: 0,
            free_for_all: false,
            bots_dont_shoot: false,
            infinite_ammo: false,
            max_money: true,
            no_round_end: false,
            allow_cheats: true,
            show_damage: false,
            spawn_protection: true,
            gun_menu: true,
            ready_up: false,
            map_vote: true,
            round_summary: true,
            welcome_message: true,
            bot_hider: false,
            deathmatch_plugin: false,
            bot_improver: true,
            bot_controller: false,
            bot_aim: 0,
            bot_nades: 2,
            bot_force_weapon: String::new(),
            bot_skins: false,
            calm_bots: true,
            headshot_only: false,
            pistols_only: false,
            reload_on_kill: false,
            only_weapon: String::new(),
            fun_mode: String::new(),
            low_gravity: false,
            cleanup_guns: true,
            no_fall_damage: false,
            auto_bhop: false,
            round_time: 0,
            friendly_fire: false,
            workshop_map: String::new(),
            keep_map_rules: false,
            custom_knife_addon: String::new(),
            custom_knife_model: String::new(),
            game_args: "-novid".into(),
            friends: LAN,
            shared_skins: true,
            password: String::new(),
            hacker_mode: 0,
            hack_wallhack: true,
            hack_aimbot: true,
            hack_one_tap: false,
            hack_ammo: true,
            hack_speed: false,
            hack_bhop: false,
            hack_no_flash: false,
            hack_esp_info: false,
            hack_aim_walls: false,
            hack_no_recoil: false,
            hack_no_spread: false,
            hack_radar: false,
            hack_box_esp: false,
            hack_bone_esp: false,
            hack_tags: false,
            hack_trigger: false,
            hack_aim_target: 0,
            hack_aim_style: 0,
            hack_aim_bone: 0,
            hack_aim_key: 0,
            aim_bind_key: String::new(),
            hack_aggression: 5,
            auto_start_game: true,
        }
    }
}

/// A one-click setup on the Play page.
pub struct Preset {
    pub title: &'static str,
    pub subtitle: &'static str,
    pub apply: fn(&mut ServerSettings),
}

pub const PRESETS: [Preset; 6] = [
    Preset {
        title: "Competitive 5v5",
        subtitle: "9 hard bots · normal rules",
        apply: |s| {
            s.reset("Competitive", "de_mirage");
            s.bots = 9;
            s.difficulty = 2;
            s.max_money = false;
        },
    },
    Preset {
        title: "Free-for-all Deathmatch",
        subtitle: "Everyone vs everyone · 10 hard bots",
        apply: |s| {
            s.reset("Deathmatch", "de_mirage");
            s.bots = 10;
            s.difficulty = 2;
            s.free_for_all = true;
        },
    },
    Preset {
        title: "Valve Retakes",
        subtitle: "Valve's retakes · 3v4 · loadout cards",
        apply: |s| {
            s.reset(RETAKES_MODE, "de_mirage");
            s.bots = 6; // you + 6 bots fills the 3 T vs 4 CT match
            s.difficulty = 2;
            s.max_money = false;
            s.retake_style = 0;
        },
    },
    Preset {
        title: "Plugin Retakes",
        subtitle: "Retakes plugin · you buy the guns · 8 bots",
        apply: |s| {
            s.reset(RETAKES_MODE, "de_mirage");
            s.bots = 8;
            s.difficulty = 2;
            s.max_money = false;
            s.retake_style = 1;
            s.retake_weapons = 0;
            s.retake_buy_seconds = 12;
            s.retake_instadefuse = true;
        },
    },
    Preset {
        title: "Zombie Infection",
        subtitle: "Zombies infect who they kill · 9 bots",
        apply: |s| {
            s.reset("Casual", "de_dust2");
            s.bots = 9;
            s.difficulty = 2;
            s.max_money = true;
            s.fun_mode = "zombie".into();
        },
    },
    Preset {
        title: "Hide and Seek",
        subtitle: "T hide with knives, CT seek · 7 bots",
        apply: |s| {
            s.reset("Casual", "de_inferno");
            s.bots = 7;
            s.difficulty = 1;
            s.max_money = true;
            s.round_time = 4;
            s.fun_mode = "hns".into();
        },
    },
];

fn b(on: bool) -> i32 {
    on as i32
}

/// The entry at `index`, clamped into the list.
fn pick<'a>(list: &[&'a str], index: i32) -> &'a str {
    list[index.clamp(0, list.len() as i32 - 1) as usize]
}

/// A password safe in a server command and on CS2's command line: no quotes or line breaks.
pub fn clean_password(text: &str) -> String {
    text.chars()
        .filter(|&c| c != '"' && !c.is_control())
        .collect::<String>()
        .trim()
        .to_string()
}

/// A key name CS2 understands (mouse4, x, alt, f6, kp_5...), or None.
pub fn clean_key(key: &str) -> Option<String> {
    static KEY: LazyLock<Regex> = LazyLock::new(|| Regex::new("^[a-z0-9_]{1,16}$").unwrap());
    let key = key.trim().to_lowercase();
    KEY.is_match(&key).then_some(key)
}

/// What Bot Improver's weapon "commands" did: set the engine's bot_loadout. None for an unknown weapon.
pub fn bot_loadout_for(weapon: &str) -> Option<String> {
    match weapon {
        "elite" | "p250" | "fn57" | "deagle" | "cz75a" => {
            Some(format!("{weapon} vesthelm defuser"))
        }
        "r8" => Some("revolver vesthelm defuser".into()),
        "m4a1s" => Some("m4a1_silencer tec9 vesthelm defuser".into()),
        _ if BOT_FORCED_WEAPONS.contains(&weapon) => {
            Some(format!("{weapon} tec9 vesthelm defuser"))
        }
        _ => None,
    }
}

/// A mode that fits a Workshop map from its title and tags, or None (it then starts in Custom).
pub fn guess_mode(title: &str, tags: &[String]) -> Option<&'static str> {
    static AR: LazyLock<Regex> =
        LazyLock::new(|| Regex::new(r"(\bar_|arms ?race|gun ?game)").unwrap());
    static DM: LazyLock<Regex> =
        LazyLock::new(|| Regex::new(r"(\b(aim|awp|dm|1v1)_|deathmatch)").unwrap());
    static DE: LazyLock<Regex> =
        LazyLock::new(|| Regex::new(r"(\b(de|cs)_|defuse|hostage|bomb ?site)").unwrap());
    let text = format!("{title} {}", tags.join(" ")).to_lowercase();
    if AR.is_match(&text) {
        Some("Arms Race")
    } else if DM.is_match(&text) {
        Some("Deathmatch")
    } else if DE.is_match(&text) {
        Some("Casual")
    } else {
        None
    }
}

/// The Steam account id (32-bit) logged in on this PC, so the plugin knows who the host is.
pub fn active_steam_account() -> u32 {
    #[cfg(windows)]
    {
        winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER)
            .open_subkey(r"Software\Valve\Steam\ActiveProcess")
            .and_then(|k| k.get_value::<u32, _>("ActiveUser"))
            .unwrap_or(0)
    }
    #[cfg(not(windows))]
    0
}

/// Queues server console commands; the plugin runs them within a second. Appended in one write: the
/// plugin's move fails while the file is open, and a read-then-replace ran commands twice.
pub fn send_commands(game: &GameFolder, commands: &[String]) -> std::io::Result<()> {
    use std::io::Write;
    let mut f = fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(game.plugin_dir().join("server_commands.txt"))?;
    f.write_all((commands.join("\n") + "\n").as_bytes())
}

pub fn send(game: &GameFolder, command: &str) -> std::io::Result<()> {
    send_commands(game, &[command.to_string()])
}

impl ServerSettings {
    pub fn is_retakes(&self) -> bool {
        self.mode == RETAKES_MODE
    }
    pub fn is_plugin_retakes(&self) -> bool {
        self.is_retakes() && self.retake_style == 1
    }
    pub fn is_valve_retakes(&self) -> bool {
        self.is_retakes() && self.retake_style != 1
    }
    pub fn uses_allocator(&self) -> bool {
        self.is_plugin_retakes() && self.retake_weapons == 1
    }
    pub fn uses_retake_buy(&self) -> bool {
        self.is_plugin_retakes()
    }
    pub fn uses_deathmatch_plugin(&self) -> bool {
        self.deathmatch_plugin && self.mode == "Deathmatch"
    }
    pub fn uses_map_rules(&self) -> bool {
        self.keep_map_rules && self.workshop_map_id().is_some()
    }
    /// sv_lan 0: friends over Radmin / the internet, or the server downloads a Workshop map or addon itself.
    pub fn private_server(&self) -> bool {
        self.friends != LAN || self.workshop_needs_download() || self.custom_knife().is_some()
    }

    /// The custom knife's Workshop addon id and model path, when both are set and look right.
    pub fn custom_knife(&self) -> Option<(String, String)> {
        static ID: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(\d{6,})").unwrap());
        static MODEL: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"^[a-z0-9_/.\-]+\.vmdl$").unwrap());
        let id = ID.captures(&self.custom_knife_addon)?[1].to_string();
        let model = self.custom_knife_model.trim().replace('\\', "/").to_lowercase();
        let model = model.strip_suffix("_c").unwrap_or(&model).to_string();
        MODEL.is_match(&model).then_some((id, model))
    }
    pub fn workshop_needs_download(&self) -> bool {
        self.workshop_map_id().is_some()
    }
    pub fn forces_bot_weapon(&self) -> bool {
        self.bot_improver && bot_loadout_for(&self.bot_force_weapon).is_some()
    }
    fn forces_bot_sniper(&self) -> bool {
        self.forces_bot_weapon()
            && matches!(
                self.bot_force_weapon.as_str(),
                "awp" | "ssg08" | "scar20" | "g3sg1"
            )
    }
    /// Arms Race, Demolition and both retake styles bring their own guns, money and round length.
    pub fn owns_loadout(&self) -> bool {
        self.is_retakes() || matches!(self.mode.as_str(), "Arms Race" | "Demolition")
    }
    /// Places in a retakes match (plugin: its MaxPlayers, Valve: 3 T + 4 CT), or 0 for other modes.
    pub fn retake_seats(&self) -> i32 {
        if self.is_plugin_retakes() {
            self.retake_max_players.clamp(2, 10)
        } else if self.is_valve_retakes() {
            7
        } else {
            0
        }
    }
    /// Custom mode: the server must start with -maxplayers.
    pub fn needs_player_slots(&self) -> bool {
        self.get_mode().game_type == 3
    }
    fn hack_recoil_or_spread(&self) -> bool {
        self.hacker_mode != 0 && (self.hack_no_recoil || self.hack_no_spread)
    }
    fn needs_cheats(&self) -> bool {
        self.allow_cheats
            || self.infinite_ammo
            || self.reload_on_kill
            || self.low_gravity
            || self.auto_bhop
            || self.bots_dont_shoot
            || self.hack_recoil_or_spread()
            || self.forces_bot_weapon()
    }

    /// The Workshop id from an id or a steamcommunity.com/sharedfiles/...?id=123 link.
    pub fn workshop_map_id(&self) -> Option<String> {
        static ID: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(\d{6,})").unwrap());
        ID.captures(&self.workshop_map).map(|c| c[1].to_string())
    }

    pub fn map_key(&self) -> String {
        self.workshop_map_id().unwrap_or_else(|| self.map.clone())
    }

    pub fn remember_map_mode(&mut self) {
        if let Some(id) = self.workshop_map_id() {
            self.map_modes.insert(id, self.mode.clone());
        }
    }

    /// Custom by default: Workshop maps are mostly built for it (movement hubs only spawn you there).
    pub fn recall_map_mode(&mut self) {
        if let Some(id) = self.workshop_map_id() {
            self.mode = match self.map_modes.get(&id) {
                Some(m) if modes().iter().any(|x| &x.name == m) => m.clone(),
                _ => "Custom".into(),
            };
        }
    }

    pub fn get_mode(&self) -> GameMode {
        let all = modes();
        let mut mode = all
            .iter()
            .find(|m| m.name == self.mode)
            .unwrap_or(&all[0])
            .clone();
        // Plugin retakes runs on competitive rules. Valve's mode is game_type 0 / game_mode 5.
        if self.is_plugin_retakes() {
            mode.game_type = 0;
            mode.mode = 1;
        }
        mode
    }

    /// Back to plain defaults for a mode (keeps launch options and auto-start).
    fn reset(&mut self, mode: &str, map: &str) {
        let d = ServerSettings::default();
        self.mode = mode.into();
        self.map = map.into();
        self.workshop_map.clear();
        self.keep_map_rules = false;
        self.bot_team = 0;
        self.bot_weapons = 0;
        self.round_time = 0;
        for f in [
            &mut self.free_for_all,
            &mut self.bots_dont_shoot,
            &mut self.infinite_ammo,
            &mut self.no_round_end,
            &mut self.friendly_fire,
            &mut self.headshot_only,
            &mut self.pistols_only,
            &mut self.reload_on_kill,
            &mut self.low_gravity,
        ] {
            *f = false;
        }
        self.only_weapon.clear();
        self.fun_mode.clear();
        // Every quick start uses the smarter bots (and keeps their default looks).
        self.bot_improver = true;
        self.bot_skins = false;
        self.calm_bots = true;
        self.max_money = true;
        self.allow_cheats = true;
        self.show_damage = false;
        self.retake_style = 0;
        self.retake_max_players = 9;
        self.retake_terrorist_percent = 45;
        self.retake_weapons = 0;
        for f in [
            &mut self.retake_scramble,
            &mut self.retake_balance,
            &mut self.retake_auto_plant,
            &mut self.retake_announce,
            &mut self.retake_auto_join,
            &mut self.retake_instadefuse,
        ] {
            *f = true;
        }
        (
            self.retake_full_percent,
            self.retake_force_percent,
            self.retake_eco_percent,
        ) = (100, 0, 0);
        self.retake_scramble_rounds = 5;
        self.retake_buy_seconds = 12;
        self.retake_bomb_timer = 40;
        self.hacker_mode = 0;
        (self.hack_wallhack, self.hack_aimbot, self.hack_ammo) = (true, true, true);
        for f in [
            &mut self.hack_one_tap,
            &mut self.hack_speed,
            &mut self.hack_bhop,
            &mut self.hack_no_flash,
            &mut self.hack_esp_info,
            &mut self.hack_aim_walls,
            &mut self.hack_no_recoil,
            &mut self.hack_no_spread,
            &mut self.hack_radar,
            &mut self.hack_box_esp,
            &mut self.hack_bone_esp,
            &mut self.hack_tags,
            &mut self.hack_trigger,
        ] {
            *f = false;
        }
        self.hack_aim_style = 0;
        self.hack_aim_bone = 0;
        self.hack_aggression = d.hack_aggression;
    }

    /// A plain CS2 server: Casual on Dust II, no bots, stock rules, no add-on plugins except the skins plugin.
    /// Friends, password, launch options and the remembered per-map settings carry over.
    pub fn stock(old: &ServerSettings) -> ServerSettings {
        ServerSettings {
            friends: old.friends,
            password: old.password.clone(),
            shared_skins: old.shared_skins,
            game_args: old.game_args.clone(),
            auto_start_game: old.auto_start_game,
            aim_bind_key: old.aim_bind_key.clone(),
            map_modes: old.map_modes.clone(),
            map_team_sizes: old.map_team_sizes.clone(),
            mode: "Casual".into(),
            bots: 0,
            keep_map_rules: true,
            bot_improver: false,
            calm_bots: false,
            max_money: false,
            allow_cheats: false,
            spawn_protection: false,
            gun_menu: false,
            map_vote: false,
            round_summary: false,
            welcome_message: false,
            cleanup_guns: false,
            ..Default::default()
        }
    }

    /// A Workshop map from the manager runs as shipped (stock rules, no bots, no extra plugins).
    pub fn for_workshop(old: &ServerSettings, id: &str) -> ServerSettings {
        let mut s = Self::stock(old);
        s.workshop_map = id.into();
        s.recall_map_mode();
        s
    }

    /// Copies a saved quick start onto this server. Friends, password and shared skins live on the Friends page.
    pub fn copy_practice_from(&mut self, source: &ServerSettings) {
        let (friends, password, shared) = (self.friends, self.password.clone(), self.shared_skins);
        *self = source.clone();
        (self.friends, self.password, self.shared_skins) = (friends, password, shared);
    }

    /// One line for a saved quick-start tile.
    pub fn summary(&self) -> String {
        let mode = if self.is_plugin_retakes() {
            "Plugin retakes"
        } else {
            &self.mode
        };
        let bots = if self.bots == 0 {
            "no bots".into()
        } else {
            format!("{} bots", self.bots)
        };
        format!("{mode} · {} · {bots}", self.map)
    }

    /// The console command that loads the map. A Workshop map is not asked for at startup (that runs before the
    /// server has logged on to Steam and it then idles with no map): it boots on the normal map first.
    fn map_command(&self, running: bool) -> String {
        match self.workshop_map_id() {
            Some(id) if running => format!("host_workshop_map {id}"),
            _ => format!(
                "{} {}",
                if running { "changelevel" } else { "map" },
                self.map
            ),
        }
    }

    pub fn start_arguments(&self) -> String {
        let mode = self.get_mode();
        // -condebug: console.log for crash reports. sv_hibernate_when_empty 0: an empty server would sleep and not
        // run the launcher's commands. sv_skirmish_id 12 is Valve's Retakes. Custom needs -maxplayers for any slots.
        let skirmish = if self.is_valve_retakes() { 12 } else { 0 };
        let retake = if self.is_valve_retakes() {
            " +mp_retake_t_count 3 +mp_retake_ct_count 4"
        } else {
            ""
        };
        format!(
            "-dedicated -insecure -condebug +sv_hibernate_when_empty 0 +sv_lan {} -port {PORT} +game_type {} +game_mode {} +sv_skirmish_id {skirmish}{retake}{} +{}",
            b(!self.private_server()), mode.game_type, mode.mode, if self.needs_player_slots() { " -maxplayers 64" } else { "" }, self.map_command(false)
        )
    }

    /// 127.0.0.1, not "localhost": that can resolve to IPv6 (::1), where the server doesn't listen.
    pub fn game_arguments(&self, game: &GameFolder) -> String {
        self.join_arguments(game, &format!("127.0.0.1:{PORT}"), &self.password)
    }

    /// CS2 with VAC off for this run only, joining a server. -netconport lets the launcher type in its console.
    pub fn join_arguments(&self, game: &GameFolder, address: &str, password: &str) -> String {
        let extra = self.game_args.replace("+connect", "");
        let clean = clean_password(password);
        let pass = if clean.is_empty() {
            String::new()
        } else {
            format!("+password \"{clean}\" ")
        };
        let exec = if self.write_client_cfg(game) {
            format!("+exec {CLIENT_CFG} ")
        } else {
            String::new()
        };
        format!(
            "-insecure -netconport {} -netconpassword {} {} {exec}{pass}+connect {address}",
            crate::console::PORT,
            crate::console::password(),
            extra.trim()
        )
        .replace("  ", " ")
    }

    /// Writes the game's own config for this PC's player (the aimbot key). Returns whether CS2 should run it.
    pub fn write_client_cfg(&self, game: &GameFolder) -> bool {
        let file = game.cfg_dir().join(format!("{CLIENT_CFG}.cfg"));
        let Some(key) = clean_key(&self.aim_bind_key) else {
            let _ = fs::remove_file(file);
            return false;
        };
        fs::create_dir_all(game.cfg_dir()).is_ok()
            && fs::write(file, format!("// Written by the LAN Skin Tester launcher.\n// Hacker mode: hold this key for the aimbot (does nothing on other servers).\nbind \"{key}\" \"+lst_aim\"\n")).is_ok()
    }

    pub fn bot_improver_commands(&self) -> Vec<String> {
        if !self.bot_improver {
            return vec![];
        }
        vec![
            format!("bot_aim {}", pick(&BOT_AIM_MODES, self.bot_aim)),
            format!("bot_nades {}", pick(&BOT_NADE_MODES, self.bot_nades)),
        ]
    }

    /// bot_loadout is cheat-protected: it goes inside the cfg's sv_cheats block.
    pub fn bot_loadout_command(&self) -> String {
        let loadout = if self.forces_bot_weapon() {
            bot_loadout_for(&self.bot_force_weapon).unwrap_or_default()
        } else {
            String::new()
        };
        format!("bot_loadout \"{loadout}\"")
    }

    /// Changes the forced weapon on a running server, leaving sv_cheats as the cfg does.
    pub fn bot_weapon_commands(&self) -> Vec<String> {
        vec![
            "sv_cheats 1".into(),
            self.bot_loadout_command(),
            format!("sv_cheats {}", b(self.needs_cheats())),
        ]
    }

    /// Zero recoil / zero spread cvars (cheat-protected; the "off" values are CS2's defaults).
    fn recoil_cvars(&self) -> Vec<String> {
        let no_recoil = self.hacker_mode != 0 && self.hack_no_recoil;
        vec![
            format!("weapon_recoil_scale {}", if no_recoil { 0 } else { 2 }),
            format!(
                "weapon_recoil_view_punch_extra {}",
                if no_recoil { "0" } else { "0.055" }
            ),
            format!(
                "weapon_recoil_suppression_shots {}",
                if no_recoil { 999 } else { 4 }
            ),
            format!(
                "weapon_accuracy_nospread {}",
                b(self.hacker_mode != 0 && self.hack_no_spread)
            ),
        ]
    }

    /// Everything hacker mode needs, without touching bots or the round (for live changes).
    pub fn hacker_commands(&self, with_cvars: bool) -> Vec<String> {
        let mut out = vec![];
        if with_cvars {
            out.push("sv_cheats 1".into());
            out.extend(self.recoil_cvars());
            out.push(format!("sv_cheats {}", b(self.needs_cheats())));
        }
        let powers: Vec<&str> = [
            (self.hack_wallhack, "wallhack"),
            (self.hack_aimbot, "aimbot"),
            (self.hack_one_tap, "onetap"),
            (self.hack_ammo, "ammo"),
            (self.hack_speed, "speed"),
            (self.hack_bhop, "bhop"),
            (self.hack_no_flash, "noflash"),
            (self.hack_esp_info, "espinfo"),
            (self.hack_aim_walls, "aimwalls"),
            (self.hack_no_recoil, "norecoil"),
            (self.hack_radar, "radar"),
            (self.hack_box_esp, "boxesp"),
            (self.hack_bone_esp, "boneesp"),
            (self.hack_tags, "tags"),
            (self.hack_trigger, "trigger"),
        ]
        .into_iter()
        .filter(|p| p.0)
        .map(|p| p.1)
        .collect();
        out.push(format!(
            "lst_hack_powers {}",
            if powers.is_empty() {
                "none".into()
            } else {
                powers.join(",")
            }
        ));
        out.push(format!(
            "lst_aim_aggression {}",
            self.hack_aggression.clamp(1, 10)
        ));
        out.push(format!(
            "lst_aim_target {}",
            pick(&AIM_TARGET_COMMANDS, self.hack_aim_target)
        ));
        out.push(format!(
            "lst_aim_style {}",
            pick(&AIM_STYLE_COMMANDS, self.hack_aim_style)
        ));
        out.push(format!(
            "lst_aim_bone {}",
            pick(&AIM_BONE_COMMANDS, self.hack_aim_bone)
        ));
        out.push(format!(
            "lst_aim_key {}",
            pick(&AIM_KEY_COMMANDS, self.hack_aim_key)
        ));
        out.push(format!(
            "lst_hack {}",
            match self.hacker_mode {
                1 => "all",
                2 => "one",
                _ => "off",
            }
        ));
        out
    }

    fn plugin_extras(&self, out: &mut Vec<String>) {
        out.extend(self.hacker_commands(false));
        out.push(format!("lst_fire_relay {}", crate::relay::PORT));
        out.push(format!(
            "lst_skin_commands {}",
            b(SKIN_COMMANDS.load(Ordering::Relaxed))
        ));
    }

    fn rule_lines(&self, out: &mut Vec<String>) {
        out.push(format!("lst_show_damage {}", b(self.show_damage)));
        out.push(format!("lst_hsonly {}", b(self.headshot_only)));
        out.push(format!("lst_pistols {}", b(self.pistols_only)));
        out.push(format!("lst_reload_on_kill {}", b(self.reload_on_kill)));
        out.push(format!(
            "lst_only_weapon {}",
            if self.only_weapon.is_empty() {
                "off"
            } else {
                &self.only_weapon
            }
        ));
        out.push(format!("lst_cleanup {}", b(self.cleanup_guns)));
        out.push(format!("lst_nofall {}", b(self.no_fall_damage)));
        out.push(format!(
            "lst_funmode {}",
            match self.fun_mode.as_str() {
                "zombie" | "hns" => &self.fun_mode,
                _ => "off",
            }
        ));
    }

    pub fn to_cfg(&self) -> String {
        let mode = self.get_mode();
        let mut o: Vec<String> = vec![
            "// Written by the LAN Skin Tester launcher - changes here are overwritten.".into(),
            "sv_hibernate_when_empty 0".into(), // keep running with nobody connected (launcher commands, bots)
            format!("sv_lan {}", b(!self.private_server())),
            format!("lst_private_server {}", b(self.private_server())),
            format!("lst_shared_loadout {}", b(self.shared_skins)),
            format!("lst_host_account {}", active_steam_account()),
            format!("sv_password \"{}\"", self.password.replace('"', "").trim()),
            // MultiAddonManager mounts the addon and has every player download it; the plugin puts the model on.
            format!("mm_extra_addons \"{}\"", self.custom_knife().map(|k| k.0).unwrap_or_default()),
            format!("lst_custom_knife \"{}\"", self.custom_knife().map(|k| k.1).unwrap_or_default()),
        ];
        if self.uses_map_rules() {
            // The map's own config decides bots, money, rounds and cheats: only the plugin's own settings are sent.
            self.plugin_extras(&mut o);
            self.rule_lines(&mut o);
            let q = |on: bool| b(on);
            o.push(format!(
                "lst_qol spawn={} guns={} ready={} vote={} summary={} welcome={} maps=",
                q(self.spawn_protection),
                q(self.gun_menu),
                q(self.ready_up),
                q(self.map_vote),
                q(self.round_summary),
                q(self.welcome_message)
            ));
            o.extend(
                [
                    "lst_retake_buy 0",
                    "lst_weapons_expert 0",
                    "lst_retake_seats 0",
                    "lst_team_sizes 0 0",
                ]
                .map(String::from),
            );
            o.push(format!("lst_bot_calm {}", b(self.calm_bots)));
            o.extend(
                [
                    "lst_bot_skins 0",
                    "lst_bot_no_snipers 0",
                    "sv_skirmish_id 0",
                ]
                .map(String::from),
            );
            return o.join("\r\n") + "\r\n";
        }

        // Cheat-protected settings need sv_cheats 1 while they're set (and turning it off resets them).
        o.push("sv_cheats 1".into());
        // 1 = the magazine never empties; 2 = spare ammo never runs out (you still reload).
        o.push(format!(
            "sv_infinite_ammo {}",
            if self.infinite_ammo {
                1
            } else if self.reload_on_kill {
                2
            } else {
                0
            }
        ));
        o.push(format!("bot_dont_shoot {}", b(self.bots_dont_shoot)));
        o.push(format!(
            "sv_gravity {}",
            if self.low_gravity { 220 } else { 800 }
        ));
        o.push(format!(
            "sv_airaccelerate {}",
            if self.low_gravity || self.auto_bhop {
                2000
            } else {
                12
            }
        ));
        o.push(format!("sv_autobunnyhopping {}", b(self.auto_bhop)));
        o.push(format!("sv_enablebunnyhopping {}", b(self.auto_bhop)));
        o.extend(self.recoil_cvars());
        o.push(self.bot_loadout_command());
        o.push(format!("sv_cheats {}", b(self.needs_cheats())));

        // Bots: difficulty only applies to newly added bots, so kick and re-add them.
        o.push(format!("bot_difficulty {}", self.difficulty.clamp(0, 3)));
        o.push("bot_quota_mode normal".into());
        o.push("bot_join_after_player 0".into());
        let team = if self.is_retakes() {
            "any"
        } else {
            match self.bot_team {
                1 => "T",
                2 => "CT",
                _ => "any",
            }
        };
        o.push(format!("bot_join_team {team}"));
        // Retakes, Arms Race and Demolition hand out their own guns. A forced sniper (Bot Improver) wins over Rifles.
        let owns = self.owns_loadout();
        let rifles = !owns && self.bot_weapons == 0 && !self.forces_bot_sniper();
        o.push(format!(
            "bot_knives_only {}",
            b(self.bot_weapons == 3 && !owns)
        ));
        o.push(format!(
            "bot_pistols_only {}",
            b((self.bot_weapons == 1 || self.pistols_only) && !owns)
        ));
        o.push(format!(
            "bot_snipers_only {}",
            b(self.bot_weapons == 2 && !owns)
        ));
        for kind in ["snipers", "shotguns", "sub_machine_guns", "machine_guns"] {
            o.push(format!("bot_allow_{kind} {}", b(!rifles)));
        }
        o.push("bot_allow_rifles 1".into());
        o.push("bot_allow_pistols 1".into());
        o.push(format!("lst_bot_no_snipers {}", b(rifles))); // plugin safety net: swaps the rifles not allowed
        o.push(format!(
            "lst_bot_rifles {}",
            BOT_RIFLE_CHOICES
                [self.bot_rifles.clamp(0, BOT_RIFLE_CHOICES.len() as i32 - 1) as usize]
                .1
        ));
        // Bot Hider's identity gives the scoreboard name too, so it matches the profile (new bots only).
        if self.bot_hider || self.bot_improver {
            o.push("bh_namesource 1".into());
        }
        o.push(format!("lst_bot_calm {}", b(self.calm_bots)));
        // Bot Improver brings its own (BotRandomizer, agents too); otherwise the plugin's random skins.
        o.push(format!(
            "lst_bot_skins {}",
            b(self.bot_skins && !self.bot_improver)
        ));
        o.push("bot_kick".into());
        // Retakes only have so many seats: leave one for you; the plugin lets one more bot go per friend.
        let wanted = self.bots.clamp(0, 31);
        let seats = self.retake_seats();
        let quota = if seats > 0 {
            wanted.min((seats - 1).max(0))
        } else {
            wanted
        };
        o.push(format!("bot_quota {quota}"));
        o.push(format!(
            "lst_retake_seats {seats} {wanted} {}",
            b(self.is_valve_retakes())
        ));
        // A side that already has more players must not grey out in the team menu: retakes picks the sides.
        o.push(format!(
            "mp_limitteams {}",
            if self.is_retakes() { 0 } else { 2 }
        ));
        // Team sizes only for classic rounds: deathmatch and arms race are free-for-all, retakes have seats.
        let sized = !self.is_retakes() && matches!(mode.game_type, 0 | 3);
        let (size_t, size_ct) = match self.map_team_sizes.get(&self.map_key()) {
            Some(s) if s.len() == 2 => (s[0], s[1]),
            _ => (self.team_size_t, self.team_size_ct),
        };
        o.push(format!(
            "lst_team_sizes {} {}",
            if sized { size_t.clamp(1, 32) } else { 0 },
            if sized { size_ct.clamp(1, 32) } else { 0 }
        ));
        o.push(format!(
            "mp_teammates_are_enemies {}",
            b(self.free_for_all && mode.game_type == 1 && mode.mode == 2)
        ));

        let buy = self.retake_buy_seconds.clamp(4, 20);
        if self.uses_retake_buy() {
            o.extend([
                "mp_maxmoney 16000".into(),
                "mp_startmoney 16000".into(),
                format!("mp_freezetime {buy}"),
                format!("mp_buytime {buy}"),
                "mp_buy_anywhere 1".into(),
            ]);
        } else if self.is_valve_retakes() {
            // Valve's cards give the guns; the buy menu stays open for changes at the start of the round.
            o.extend([
                "mp_maxmoney 16000".into(),
                "mp_startmoney 16000".into(),
                "mp_buy_anywhere 1".into(),
                format!("mp_buytime {buy}"),
            ]);
        } else if self.max_money && !owns {
            o.extend(
                ["mp_maxmoney", "mp_startmoney", "mp_afterroundmoney"]
                    .map(|c| format!("{c} 65535")),
            );
            o.extend(["mp_buy_anywhere 1", "mp_buytime 9999"].map(String::from));
        }
        if self.no_round_end && !owns {
            o.extend(
                [
                    "mp_ignore_round_win_conditions 1",
                    "mp_roundtime 60",
                    "mp_roundtime_defuse 60",
                    "mp_freezetime 0",
                    "mp_respawn_on_death_t 1",
                    "mp_respawn_on_death_ct 1",
                ]
                .map(String::from),
            );
        }
        if self.round_time > 0 && !owns {
            o.extend(
                [
                    "mp_roundtime",
                    "mp_roundtime_defuse",
                    "mp_roundtime_hostage",
                ]
                .map(|c| format!("{c} {}", self.round_time)),
            );
        }
        if self.friendly_fire {
            o.push("mp_friendlyfire 1".into());
        }

        self.plugin_extras(&mut o);
        o.extend(self.bot_improver_commands());
        self.rule_lines(&mut o);
        // Greys the other guns out in the buy menu; the plugin also takes away any that get through.
        let prohibited: Vec<String> = if !self.only_weapon.is_empty() {
            items::WEAPONS
                .iter()
                .filter(|w| w.1 != self.only_weapon && w.0 != 31)
                .map(|w| w.0.to_string())
                .collect()
        } else if self.pistols_only {
            items::WEAPONS
                .iter()
                .filter(|w| !items::PISTOLS.contains(&w.0) && w.0 != 31)
                .map(|w| w.0.to_string())
                .collect()
        } else {
            vec![]
        };
        o.push(format!("mp_items_prohibited \"{}\"", prohibited.join(",")));
        let dm = self.uses_deathmatch_plugin();
        o.push(format!(
            "lst_qol spawn={} guns={} ready={} vote={} summary={} welcome={} maps={}",
            b(self.spawn_protection && !dm),
            b(self.gun_menu && !dm && !self.is_plugin_retakes()),
            b(self.ready_up),
            b(self.map_vote),
            b(self.round_summary),
            b(self.welcome_message),
            mode.maps.join(",")
        ));
        o.push(format!(
            "lst_retake_buy {} {buy} {} {} {} {}",
            b(self.uses_retake_buy()),
            b(self.uses_allocator()),
            self.retake_full_percent.clamp(0, 100),
            self.retake_force_percent.clamp(0, 100),
            self.retake_eco_percent.clamp(0, 100)
        ));
        o.push(format!(
            "lst_weapons_expert {}",
            b(self.mode == WEAPONS_EXPERT_MODE)
        ));

        // sv_skirmish_id survives map changes. Don't end warmup on either retake style: both need that warmup.
        o.push(format!(
            "sv_skirmish_id {}",
            if self.is_valve_retakes() { 12 } else { 0 }
        ));
        if self.is_valve_retakes() {
            o.extend(["mp_retake_t_count 3", "mp_retake_ct_count 4"].map(String::from));
        } else if self.is_plugin_retakes() {
            o.push("retakes_enabled 1".into());
            o.push(format!(
                "mp_c4timer {}",
                self.retake_bomb_timer.clamp(15, 60)
            ));
            if !self.uses_retake_buy() {
                o.push("mp_freezetime 1".into());
            }
        } else {
            o.extend(["retakes_enabled 0", "mp_warmup_end"].map(String::from));
        }
        o.join("\r\n") + "\r\n"
    }

    /// What has to match for the running retakes plugins (loading/unloading or load-time settings).
    pub fn plugin_retake_key(&self) -> String {
        if !self.is_plugin_retakes() {
            return if self.is_valve_retakes() {
                "valve"
            } else {
                "off"
            }
            .into();
        }
        let t = |v: bool| if v { "True" } else { "False" };
        [
            self.retake_style.to_string(),
            self.retake_max_players.clamp(2, 10).to_string(),
            self.retake_terrorist_percent.clamp(10, 80).to_string(),
            t(self.retake_scramble).into(),
            self.retake_scramble_rounds.clamp(1, 30).to_string(),
            t(self.retake_balance).into(),
            t(self.retake_auto_plant).into(),
            t(self.retake_announce).into(),
            t(self.retake_auto_join).into(),
            b(self.retake_weapons == 1).to_string(),
            t(self.retake_instadefuse).into(),
        ]
        .join("|")
    }

    /// Writes RetakesPlugin.json, keeping any keys this launcher doesn't own. The plugin reads it when it loads.
    pub fn write_retakes_config(&self, game: &GameFolder) -> std::io::Result<()> {
        let path = game
            .css()
            .join("configs")
            .join("plugins")
            .join("RetakesPlugin")
            .join("RetakesPlugin.json");
        fs::create_dir_all(path.parent().unwrap())?;
        let mut root = fs::read_to_string(&path)
            .ok()
            .and_then(|t| serde_json::from_str::<Value>(&t).ok())
            .filter(Value::is_object)
            .unwrap_or(json!({}));
        let mut set = |section: &str, key: &str, value: Value| {
            if !root[section].is_object() {
                root[section] = json!({});
            }
            root[section][key] = value;
        };
        set(
            "GameSettings",
            "MaxPlayers",
            json!(self.retake_max_players.clamp(2, 10)),
        );
        set("GameSettings", "MinimumPlayers", json!(0));
        set("GameSettings", "ShouldBreakBreakables", json!(false));
        set("GameSettings", "ShouldOpenDoors", json!(false));
        // Buy mode keeps the plugin's rifles so bots stay armed; the allocator replaces that hand-out.
        set(
            "GameSettings",
            "EnableFallbackAllocation",
            json!(self.retake_weapons != 1),
        );
        set(
            "TeamSettings",
            "TerroristRatio",
            json!(self.retake_terrorist_percent.clamp(10, 80) as f64 / 100.0),
        );
        set(
            "TeamSettings",
            "RoundsToScramble",
            json!(self.retake_scramble_rounds.clamp(1, 30)),
        );
        set(
            "TeamSettings",
            "IsScrambleEnabled",
            json!(self.retake_scramble),
        );
        set(
            "TeamSettings",
            "IsBalanceEnabled",
            json!(self.retake_balance),
        );
        set(
            "TeamSettings",
            "ShouldPreventTeamChangesMidRound",
            json!(true),
        );
        set(
            "MapConfigSettings",
            "EnableBombsiteAnnouncementCenter",
            json!(self.retake_announce),
        );
        set(
            "BombSettings",
            "IsAutoPlantEnabled",
            json!(self.retake_auto_plant),
        );
        set(
            "QueueSettings",
            "ShouldAutoJoinGame",
            json!(self.retake_auto_join),
        );
        set(
            "QueueSettings",
            "ShouldAutoJoinSpectators",
            json!(!self.retake_auto_join),
        );
        if root.get("ConfigVersion").is_none() {
            root["ConfigVersion"] = json!(2);
        }
        fs::write(path, serde_json::to_string_pretty(&root).unwrap())
    }

    pub fn write_cfg(&self, game: &GameFolder) -> std::io::Result<()> {
        let dir = game.cfg_dir();
        fs::create_dir_all(&dir)?;
        fs::write(dir.join("lanskintester_server.cfg"), self.to_cfg())?;
        fs::write(
            dir.join(SCOUTS_ON_CFG),
            SCOUTS_ON_COMMANDS.join("\r\n") + "\r\n",
        )?;
        fs::write(
            dir.join(SCOUTS_OFF_CFG),
            SCOUTS_OFF_COMMANDS.join("\r\n") + "\r\n",
        )
    }

    /// Sends the hacker settings to the running server right away (and keeps them for map changes).
    pub fn apply_hacker_to_running_server(&self, game: &GameFolder) -> std::io::Result<()> {
        self.write_cfg(game)?;
        send_commands(game, &self.hacker_commands(true))
    }

    /// Applies the settings to a running server through the plugin. Changing mode or map reloads the map; otherwise
    /// the cfg is re-run. Returns a warning when the new Workshop map can't load on this server.
    pub fn apply_to_running_server(
        &self,
        game: &GameFolder,
        map_or_mode_changed: bool,
        server_online: bool,
    ) -> std::io::Result<Option<String>> {
        if map_or_mode_changed {
            game::remove_workshop_copies(game);
        }
        if self.workshop_needs_download() && !server_online {
            return Ok(Some("This Workshop map isn't downloaded and the server is in offline LAN mode, so it can't fetch it. \
                Press Subscribe (Steam downloads it, then Apply again), or Stop and Start so the server goes online to download it.".into()));
        }
        self.write_cfg(game)?;
        if !map_or_mode_changed {
            send(game, "exec lanskintester_server.cfg")?;
            return Ok(None);
        }
        // Skirmish id and retake sizes first: the map load reads them, and a later cfg exec is too late.
        let mode = self.get_mode();
        let mut before = vec![format!(
            "sv_skirmish_id {}",
            if self.is_valve_retakes() { 12 } else { 0 }
        )];
        if self.is_valve_retakes() {
            before.extend(["mp_retake_t_count 3", "mp_retake_ct_count 4"].map(String::from));
        }
        before.push(format!("game_type {}", mode.game_type));
        before.push(format!("game_mode {}", mode.mode));
        before.push(self.map_command(true));
        send_commands(game, &before)?;
        Ok(None)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn preset(title: &str) -> ServerSettings {
        let mut s = ServerSettings::default();
        (PRESETS.iter().find(|p| p.title == title).unwrap().apply)(&mut s);
        s
    }

    fn lines(cfg: &str) -> Vec<String> {
        cfg.lines()
            .map(|l| l.trim().to_string())
            .filter(|l| !l.is_empty() && !l.starts_with("//"))
            .collect()
    }

    fn arg(commands: &[String], name: &str) -> String {
        commands
            .iter()
            .find(|c| c.starts_with(&format!("{name} ")))
            .unwrap()[name.len() + 1..]
            .to_string()
    }

    #[test]
    fn plugin_retakes_leave_seats_for_friends() {
        let cfg = preset("Plugin Retakes").to_cfg();
        for want in [
            "bot_quota 8",
            "lst_retake_seats 9 8 0",
            "mp_limitteams 0",
            "retakes_enabled 1",
            "lst_qol spawn=1 guns=0",
        ] {
            assert!(cfg.contains(want), "{want}");
        }
    }

    #[test]
    fn valve_retakes_and_other_modes() {
        let cfg = preset("Valve Retakes").to_cfg();
        for want in [
            "bot_quota 6",
            "lst_retake_seats 7 6 1",
            "mp_limitteams 0",
            "sv_skirmish_id 12",
        ] {
            assert!(cfg.contains(want), "{want}");
        }
        let cfg = preset("Free-for-all Deathmatch").to_cfg();
        for want in [
            "lst_retake_seats 0 10 0",
            "mp_limitteams 2",
            "retakes_enabled 0",
        ] {
            assert!(cfg.contains(want), "{want}");
        }
    }

    #[test]
    fn too_many_bots_for_retakes_still_leave_you_a_seat() {
        let mut s = preset("Plugin Retakes");
        s.bots = 20;
        s.retake_max_players = 5;
        let cfg = s.to_cfg();
        assert!(cfg.contains("bot_quota 4") && cfg.contains("lst_retake_seats 5 20 0"));
    }

    #[test]
    fn every_forced_weapon_has_a_loadout() {
        assert!(BOT_FORCED_WEAPONS
            .iter()
            .all(|w| bot_loadout_for(w).is_some_and(|l| !l.is_empty())));
        assert_eq!(bot_loadout_for(""), None);
        assert_eq!(bot_loadout_for("bot_buy"), None);
        assert_eq!(
            bot_loadout_for("m4a1s").unwrap(),
            "m4a1_silencer tec9 vesthelm defuser"
        );
        assert_eq!(bot_loadout_for("r8").unwrap(), "revolver vesthelm defuser");
    }

    #[test]
    fn forced_weapon_is_set_while_cheats_are_on() {
        let mut s = preset("Competitive 5v5");
        s.allow_cheats = false;
        s.bot_improver = true;
        s.bot_force_weapon = "ak47".into();
        let l = lines(&s.to_cfg());
        let at = l
            .iter()
            .position(|x| x == "bot_loadout \"ak47 tec9 vesthelm defuser\"")
            .expect("bot_loadout missing");
        assert_eq!(
            l[..at]
                .iter()
                .rev()
                .find(|x| x.starts_with("sv_cheats"))
                .unwrap(),
            "sv_cheats 1"
        );
        assert_eq!(
            l[at..].iter().find(|x| x.starts_with("sv_cheats")).unwrap(),
            "sv_cheats 1"
        );
    }

    #[test]
    fn no_forced_weapon_clears_the_loadout() {
        let mut s = preset("Competitive 5v5");
        s.bot_improver = true;
        assert!(s.to_cfg().contains("bot_loadout \"\""));
        s.bot_improver = false;
        s.bot_force_weapon = "awp".into();
        assert!(s.to_cfg().contains("bot_loadout \"\""));
        assert!(!s.forces_bot_weapon());
    }

    #[test]
    fn forced_sniper_is_not_swapped_for_a_rifle() {
        let mut s = preset("Free-for-all Deathmatch");
        assert!(s.to_cfg().contains("lst_bot_no_snipers 1"));
        assert!(s
            .to_cfg()
            .contains("lst_bot_rifles ak47,m4a1,m4a1_silencer"));
        s.bot_rifles = 99;
        assert!(s
            .to_cfg()
            .contains("lst_bot_rifles ak47,m4a1,m4a1_silencer,famas"));
        s.bot_improver = true;
        s.bot_force_weapon = "awp".into();
        let cfg = s.to_cfg();
        assert!(cfg.contains("lst_bot_no_snipers 0") && cfg.contains("bot_allow_snipers 1"));
    }

    #[test]
    fn bot_improver_commands_and_live_weapon_change() {
        let mut s = ServerSettings {
            bot_improver: true,
            bot_force_weapon: "ak47".into(),
            ..Default::default()
        };
        assert_eq!(
            s.bot_improver_commands(),
            ["bot_aim mixed", "bot_nades normal"]
        );
        s.bot_improver = false;
        assert!(s.bot_improver_commands().is_empty());
        let mut s = ServerSettings {
            allow_cheats: false,
            bot_improver: true,
            bot_force_weapon: "awp".into(),
            ..Default::default()
        };
        assert_eq!(
            s.bot_weapon_commands(),
            [
                "sv_cheats 1",
                "bot_loadout \"awp tec9 vesthelm defuser\"",
                "sv_cheats 1"
            ]
        );
        s.bot_force_weapon.clear();
        assert_eq!(
            s.bot_weapon_commands(),
            ["sv_cheats 1", "bot_loadout \"\"", "sv_cheats 0"]
        );
    }

    #[test]
    fn flying_scoutsman_off_undoes_what_on_changed() {
        let name = |l: &&str| l.split(' ').next().unwrap().to_string();
        let off: Vec<String> = SCOUTS_OFF_COMMANDS.iter().map(name).collect();
        let reload = [
            "sv_cheats",
            "mp_maxmoney",
            "mp_roundtime",
            "mp_freezetime",
            "mp_restartgame",
            "sv_gravity",
            "sv_airaccelerate",
            "sv_enablebunnyhopping",
        ];
        for n in SCOUTS_ON_COMMANDS
            .iter()
            .map(name)
            .filter(|n| !reload.contains(&n.as_str()))
        {
            assert!(off.contains(&n), "Flying Scoutsman off doesn't reset {n}");
        }
    }

    #[test]
    fn game_joins_the_local_server_with_vac_off() {
        let game = GameFolder::new(
            std::env::temp_dir().join(format!("lst-game-{}", crate::util::random_hex(6))),
        );
        let s = ServerSettings {
            game_args: "-windowed +connect 1.2.3.4".into(),
            password: "abc".into(),
            ..Default::default()
        };
        let args = s.game_arguments(&game);
        assert!(
            args.starts_with("-insecure ")
                && args.ends_with("+connect 127.0.0.1:27015")
                && args.contains("+password \"abc\""),
            "{args}"
        );
        assert_eq!(args.matches("+connect").count(), 1);
    }

    #[test]
    fn server_starts_with_a_map_and_the_right_mode() {
        for p in &PRESETS {
            let s = preset(p.title);
            let args = s.start_arguments();
            let mode = s.get_mode();
            assert!(args.contains("-dedicated") && args.contains("-insecure"));
            assert!(args.contains(&format!(
                "+game_type {} +game_mode {}",
                mode.game_type, mode.mode
            )));
            assert!(Regex::new(r"\+map \S+").unwrap().is_match(&args));
            assert!(args.contains(&format!(
                "+sv_skirmish_id {}",
                if s.is_valve_retakes() { 12 } else { 0 }
            )));
            assert_eq!(mode.game_type == 3, args.contains("-maxplayers 64"));
        }
    }

    #[test]
    fn custom_mode_starts_with_player_slots() {
        let mut s = ServerSettings::for_workshop(&ServerSettings::default(), "3355497176");
        assert!(s.start_arguments().contains("-maxplayers 64"));
        s.mode = "Casual".into();
        assert!(!s.start_arguments().contains("-maxplayers"));
    }

    #[test]
    fn reset_gives_a_plain_server_and_keeps_friends_settings() {
        let mut old = preset("Plugin Retakes");
        old.password = "pw".into();
        old.friends = VPN;
        old.game_args = "-novid -w 1920".into();
        let s = ServerSettings::stock(&old);
        assert_eq!(
            (
                s.mode.as_str(),
                s.bots,
                s.password.as_str(),
                s.friends,
                s.game_args.as_str()
            ),
            ("Casual", 0, "pw", VPN, "-novid -w 1920")
        );
        let cfg = s.to_cfg();
        assert!(
            cfg.contains("lst_qol spawn=0 guns=0 ready=0 vote=0 summary=0 welcome=0")
                && cfg.contains("retakes_enabled 0")
        );
        assert!(!cfg.contains("mp_maxmoney 65535"));
    }

    #[test]
    fn custom_knife_needs_an_id_and_a_model() {
        let mut s = ServerSettings::default();
        assert_eq!(s.custom_knife(), None);
        s.custom_knife_addon = "https://steamcommunity.com/sharedfiles/filedetails/?id=3090239773".into();
        s.custom_knife_model = "Models\\Weapons\\Custom\\Knife.vmdl_c".into();
        assert_eq!(s.custom_knife(), Some(("3090239773".into(), "models/weapons/custom/knife.vmdl".into())));
        assert!(s.private_server());
        assert!(s.to_cfg().contains("mm_extra_addons \"3090239773\""));
        s.custom_knife_model = "knife; quit".into();
        assert_eq!(s.custom_knife(), None);
    }

    #[test]
    fn workshop_map_keeps_its_own_rules() {
        let mut s = ServerSettings::for_workshop(
            &ServerSettings {
                password: "pw".into(),
                ..Default::default()
            },
            "3070244462",
        );
        assert_eq!(s.mode, "Custom");
        let cfg = s.to_cfg();
        for line in [
            "bot_kick",
            "bot_quota",
            "mp_limitteams",
            "mp_warmup_end",
            "sv_cheats",
            "mp_items_prohibited",
            "mp_maxmoney",
        ] {
            assert!(!cfg.contains(line), "{line}");
        }
        assert!(
            cfg.contains("sv_password \"pw\"")
                && cfg.contains("lst_only_weapon off")
                && cfg.contains("lst_retake_seats 0")
        );
        s.keep_map_rules = false;
        assert!(s.to_cfg().contains("mp_warmup_end"));
    }

    #[test]
    fn hacker_commands() {
        let s = ServerSettings {
            hacker_mode: 1,
            hack_wallhack: false,
            hack_aimbot: false,
            hack_ammo: false,
            ..Default::default()
        };
        assert_eq!(arg(&s.hacker_commands(true), "lst_hack_powers"), "none");
        for (mode, want) in [(0, "off"), (1, "all"), (2, "one")] {
            assert_eq!(
                arg(
                    &ServerSettings {
                        hacker_mode: mode,
                        ..Default::default()
                    }
                    .hacker_commands(true),
                    "lst_hack"
                ),
                want
            );
        }
        let mut s = ServerSettings {
            allow_cheats: false,
            hacker_mode: 1,
            hack_no_recoil: true,
            hack_no_spread: true,
            ..Default::default()
        };
        let l = s.hacker_commands(true);
        assert_eq!(l[0], "sv_cheats 1");
        assert!(
            l.contains(&"weapon_accuracy_nospread 1".into())
                && l.contains(&"weapon_recoil_scale 0".into())
        );
        assert_eq!(
            l.iter().rev().find(|x| x.starts_with("sv_cheats")).unwrap(),
            "sv_cheats 1"
        );
        s.hacker_mode = 0;
        let l = s.hacker_commands(true);
        assert!(
            l.contains(&"weapon_accuracy_nospread 0".into())
                && l.contains(&"weapon_recoil_scale 2".into())
        );
        assert_eq!(
            l.iter().rev().find(|x| x.starts_with("sv_cheats")).unwrap(),
            "sv_cheats 0"
        );
    }

    #[test]
    fn workshop_mode_from_title() {
        for (title, mode) in [
            ("aim_redline", Some("Deathmatch")),
            ("de_cache remake", Some("Casual")),
            ("Arms Race Pool", Some("Arms Race")),
            ("surf_utopia", None),
            ("kz_bhop_hub", None),
        ] {
            assert_eq!(guess_mode(title, &[]), mode, "{title}");
        }
    }

    #[test]
    fn passwords_lose_quotes_and_line_breaks() {
        assert_eq!(clean_password(" a\"b\nquit "), "abquit");
    }

    #[test]
    fn settings_file_format_is_pascal_case() {
        let json = serde_json::to_string(&ServerSettings::default()).unwrap();
        assert!(
            json.contains("\"TeamSizeCT\":5")
                && json.contains("\"Friends\":0")
                && json.contains("\"HackAggression\":5")
        );
        let back: ServerSettings =
            crate::util::read_json(r#"{"Mode":"Casual","WorkshopMapId":null,"Password":null}"#)
                .unwrap();
        assert_eq!((back.mode.as_str(), back.bots), ("Casual", 10));
    }

    /// What the launcher writes for the server, for the C# tests to check against the plugin
    /// (tests/LanSkinTester.Tests/launcher-commands.json). Regenerate with LST_WRITE_COMMANDS=1 cargo test.
    #[test]
    fn launcher_commands_fixture_is_current() {
        let mut all: Vec<String> = vec![];
        for p in &PRESETS {
            let mut s = preset(p.title);
            all.extend(lines(&s.to_cfg()));
            s.bot_improver = true;
            s.bot_hider = true;
            for w in ["", "ak47", "awp", "deagle", "r8", "m4a1s"] {
                s.bot_force_weapon = w.into();
                all.extend(lines(&s.to_cfg()));
                all.extend(s.bot_weapon_commands());
            }
        }
        let mut every = ServerSettings {
            hacker_mode: 1,
            ..Default::default()
        };
        for f in [
            &mut every.hack_one_tap,
            &mut every.hack_speed,
            &mut every.hack_bhop,
            &mut every.hack_no_flash,
            &mut every.hack_esp_info,
            &mut every.hack_aim_walls,
            &mut every.hack_no_recoil,
            &mut every.hack_radar,
            &mut every.hack_box_esp,
            &mut every.hack_bone_esp,
            &mut every.hack_tags,
            &mut every.hack_trigger,
        ] {
            *f = true;
        }
        all.extend(every.hacker_commands(true));
        for mode in 0..3 {
            all.extend(
                ServerSettings {
                    hacker_mode: mode,
                    ..Default::default()
                }
                .hacker_commands(true),
            );
        }
        all.extend(
            ServerSettings {
                hacker_mode: 1,
                hack_wallhack: false,
                hack_aimbot: false,
                hack_ammo: false,
                ..Default::default()
            }
            .hacker_commands(true),
        );
        all.extend(
            SCOUTS_ON_COMMANDS
                .iter()
                .chain(SCOUTS_OFF_COMMANDS.iter())
                .map(|s| s.to_string()),
        );
        all.extend(lines(
            &ServerSettings::for_workshop(&ServerSettings::default(), "3070244462").to_cfg(),
        ));
        let account = format!("lst_host_account {}", active_steam_account());
        let mut seen = std::collections::BTreeSet::new();
        let commands: Vec<String> = all
            .into_iter()
            .map(|l| {
                if l == account {
                    "lst_host_account 0".into()
                } else {
                    l
                }
            })
            .filter(|l| seen.insert(l.clone()))
            .collect();
        // Plugin retakes: the cfg's bot_quota for every seat count and bot count (the plugin must agree).
        let mut quotas = vec![];
        for max in 2..=10 {
            for bots in [0, 1, 6, 8, 20] {
                let s = ServerSettings {
                    mode: RETAKES_MODE.into(),
                    retake_style: 1,
                    retake_max_players: max,
                    bots,
                    ..Default::default()
                };
                let quota: i32 = lines(&s.to_cfg())
                    .iter()
                    .find_map(|l| l.strip_prefix("bot_quota ")?.parse().ok())
                    .unwrap();
                quotas.push([max, bots, quota]);
            }
        }
        let fixture = json!({
            "commands": commands,
            "allPowers": arg(&every.hacker_commands(false), "lst_hack_powers"),
            "aimKeys": AIM_KEY_COMMANDS, "aimStyles": AIM_STYLE_COMMANDS, "aimBones": AIM_BONE_COMMANDS, "aimTargets": AIM_TARGET_COMMANDS,
            "retakeQuotas": quotas,
        });
        let text = serde_json::to_string_pretty(&fixture).unwrap() + "\n";
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/LanSkinTester.Tests/launcher-commands.json");
        if std::env::var("LST_WRITE_COMMANDS").is_ok() {
            fs::write(&path, &text).unwrap();
        }
        assert_eq!(
            fs::read_to_string(&path)
                .unwrap_or_default()
                .replace('\r', ""),
            text,
            "run LST_WRITE_COMMANDS=1 cargo test to update {}",
            path.display()
        );
    }
}
