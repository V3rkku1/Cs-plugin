//! The plugin's item tables and file formats (loadouts.json, status.json), shared with the C# plugin:
//! names and casing must stay exactly as the plugin reads and writes them.

use base64::Engine;
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};

/// Weapon definition index -> classname, in the plugin's order.
pub const WEAPONS: &[(u16, &str)] = &[
    (1, "weapon_deagle"),
    (2, "weapon_elite"),
    (3, "weapon_fiveseven"),
    (4, "weapon_glock"),
    (7, "weapon_ak47"),
    (8, "weapon_aug"),
    (9, "weapon_awp"),
    (10, "weapon_famas"),
    (11, "weapon_g3sg1"),
    (13, "weapon_galilar"),
    (14, "weapon_m249"),
    (16, "weapon_m4a1"),
    (17, "weapon_mac10"),
    (19, "weapon_p90"),
    (23, "weapon_mp5sd"),
    (24, "weapon_ump45"),
    (25, "weapon_xm1014"),
    (26, "weapon_bizon"),
    (27, "weapon_mag7"),
    (28, "weapon_negev"),
    (29, "weapon_sawedoff"),
    (30, "weapon_tec9"),
    (31, "weapon_taser"),
    (32, "weapon_hkp2000"),
    (33, "weapon_mp7"),
    (34, "weapon_mp9"),
    (35, "weapon_nova"),
    (36, "weapon_p250"),
    (38, "weapon_scar20"),
    (39, "weapon_sg556"),
    (40, "weapon_ssg08"),
    (60, "weapon_m4a1_silencer"),
    (61, "weapon_usp_silencer"),
    (63, "weapon_cz75a"),
    (64, "weapon_revolver"),
];

/// Weapon definition index -> name as shown in game.
pub const DISPLAY_NAMES: &[(u16, &str)] = &[
    (1, "Desert Eagle"),
    (2, "Dual Berettas"),
    (3, "Five-SeveN"),
    (4, "Glock-18"),
    (7, "AK-47"),
    (8, "AUG"),
    (9, "AWP"),
    (10, "FAMAS"),
    (11, "G3SG1"),
    (13, "Galil AR"),
    (14, "M249"),
    (16, "M4A4"),
    (17, "MAC-10"),
    (19, "P90"),
    (23, "MP5-SD"),
    (24, "UMP-45"),
    (25, "XM1014"),
    (26, "PP-Bizon"),
    (27, "MAG-7"),
    (28, "Negev"),
    (29, "Sawed-Off"),
    (30, "Tec-9"),
    (31, "Zeus x27"),
    (32, "P2000"),
    (33, "MP7"),
    (34, "MP9"),
    (35, "Nova"),
    (36, "P250"),
    (38, "SCAR-20"),
    (39, "SG 553"),
    (40, "SSG 08"),
    (60, "M4A1-S"),
    (61, "USP-S"),
    (63, "CZ75-Auto"),
    (64, "R8 Revolver"),
];

const KNIVES: &[u16] = &[
    500, 503, 505, 506, 507, 508, 509, 512, 514, 515, 516, 517, 518, 519, 520, 521, 522, 523, 525,
    526,
];
const GLOVES: &[u16] = &[4725, 5027, 5030, 5031, 5032, 5033, 5034, 5035];
pub const PISTOLS: &[u16] = &[1, 2, 3, 4, 30, 32, 36, 61, 63, 64];

pub fn classname(def: u16) -> Option<&'static str> {
    WEAPONS.iter().find(|w| w.0 == def).map(|w| w.1)
}

pub fn is_knife_type(def: u16) -> bool {
    KNIVES.contains(&def)
}

pub fn is_glove_type(def: u16) -> bool {
    GLOVES.contains(&def)
}

fn wear_default() -> f32 {
    0.01
}

fn one() -> f32 {
    1.0
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq)]
#[serde(rename_all = "PascalCase", default)]
pub struct SkinSettings {
    pub paint: i32,
    #[serde(default = "wear_default")]
    pub wear: f32,
    pub seed: i32,
    pub stat_trak: bool,
    pub stat_trak_count: i32,
    pub nametag: String,
    /// Older finishes are made for the "legacy" weapon mesh. None = use the skins catalog.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub legacy: Option<bool>,
    pub stickers: Vec<StickerSettings>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub keychain: Option<KeychainSettings>,
}

impl Default for SkinSettings {
    fn default() -> Self {
        Self {
            paint: 0,
            wear: 0.01,
            seed: 0,
            stat_trak: false,
            stat_trak_count: 0,
            nametag: String::new(),
            legacy: None,
            stickers: vec![],
            keychain: None,
        }
    }
}

impl SkinSettings {
    /// Anything to put on the weapon besides the plain default look.
    pub fn has_customization(&self) -> bool {
        self.paint != 0
            || !self.stickers.is_empty()
            || self.keychain.is_some()
            || self.stat_trak
            || !self.nametag.is_empty()
    }
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct StickerSettings {
    pub slot: i32,
    pub id: i32,
    /// 0 = brand new, 1 = scraped away.
    pub wear: f32,
    #[serde(default = "one")]
    pub scale: f32,
    pub rotation: f32,
    pub offset_x: f32,
    pub offset_y: f32,
}

#[derive(Serialize, Deserialize, Clone, Debug, PartialEq, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct KeychainSettings {
    pub id: i32,
    pub offset_x: f32,
    pub offset_y: f32,
    pub offset_z: f32,
    pub seed: i32,
}

pub type Skins = BTreeMap<u16, SkinSettings>;

#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct PlayerLoadout {
    /// Weapon/knife/glove definition index -> skin.
    pub skins: Skins,
    /// Per-side skins win over `skins` for players on that side (2 = T, 3 = CT).
    pub skins_t: Skins,
    #[serde(rename = "SkinsCT")]
    pub skins_ct: Skins,
    pub knife: u16,
    pub gloves: u16,
    pub knife_t: u16,
    #[serde(rename = "KnifeCT")]
    pub knife_ct: u16,
    pub gloves_t: u16,
    #[serde(rename = "GlovesCT")]
    pub gloves_ct: u16,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub agent_t: Option<String>,
    #[serde(rename = "AgentCT", skip_serializing_if = "Option::is_none")]
    pub agent_ct: Option<String>,
    pub pin: u16,
    pub music_kit: u16,
}

impl PlayerLoadout {
    pub fn skin_for(&self, def: u16, team: i32) -> Option<&SkinSettings> {
        let side = match team {
            2 => self.skins_t.get(&def),
            3 => self.skins_ct.get(&def),
            _ => None,
        };
        side.or_else(|| self.skins.get(&def))
    }

    pub fn knife_for(&self, team: i32) -> u16 {
        let k = match team {
            2 => self.knife_t,
            3 => self.knife_ct,
            _ => 0,
        };
        if k != 0 {
            k
        } else {
            self.knife
        }
    }

    pub fn gloves_for(&self, team: i32) -> u16 {
        let g = match team {
            2 => self.gloves_t,
            3 => self.gloves_ct,
            _ => 0,
        };
        if g != 0 {
            g
        } else {
            self.gloves
        }
    }
}

/// Every player's loadout in one JSON file (the plugin's LoadoutStore format); the launcher edits "local".
pub struct LoadoutStore {
    path: PathBuf,
    loadouts: BTreeMap<String, PlayerLoadout>,
}

pub const LOCAL_KEY: &str = "local";

impl LoadoutStore {
    pub fn open(path: &Path) -> Self {
        let mut store = Self {
            path: path.to_path_buf(),
            loadouts: BTreeMap::new(),
        };
        if path.exists() {
            match fs::read_to_string(path)
                .ok()
                .and_then(|t| crate::util::read_json(&t).ok())
            {
                Some(l) => store.loadouts = l,
                None => {
                    let _ = fs::rename(path, format!("{}.broken", path.display()));
                }
            }
        }
        store
    }

    pub fn local(&mut self) -> &mut PlayerLoadout {
        self.loadouts.entry(LOCAL_KEY.into()).or_default()
    }

    /// Temp file and swap, so the plugin never reads a half-written file.
    pub fn save(&self) -> std::io::Result<()> {
        let temp = format!("{}.tmp", self.path.display());
        fs::write(
            &temp,
            serde_json::to_string_pretty(&self.loadouts).unwrap_or_default(),
        )?;
        fs::rename(temp, &self.path)
    }
}

/// A loadout as console-safe text chunks for css_myskins (the plugin's LoadoutCodec).
pub const CHUNK_LENGTH: usize = 200;
pub const MAX_CHUNKS: usize = 200;

pub fn encode_loadout(loadout: &PlayerLoadout) -> Vec<String> {
    let text = base64::engine::general_purpose::STANDARD
        .encode(serde_json::to_vec(loadout).unwrap_or_default())
        .replace('+', "-")
        .replace('/', "_");
    text.as_bytes()
        .chunks(CHUNK_LENGTH)
        .map(|c| String::from_utf8_lossy(c).into_owned())
        .collect()
}

/// What the plugin reports every 2 seconds (plugins/LanSkinTester/status.json).
#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct ServerStatus {
    pub version: String,
    pub map: String,
    pub game_type: i32,
    pub game_mode: i32,
    pub humans: i32,
    pub bots: i32,
    /// The server's player slots (0 from plugins older than this field).
    pub max_players: i32,
    pub players: Vec<String>,
    pub skins_allowed: bool,
    pub disabled_reason: String,
    /// Session stats, bots too, best first (empty from older plugins).
    pub stats: Vec<PlayerStats>,
    /// Unix time of the last write; old files mean "server not running".
    pub time: i64,
}

/// One player's kills, deaths, headshots and damage since the map started (Rounds: rounds played, for ADR).
#[derive(Deserialize, Serialize, Clone, Debug, Default, PartialEq)]
#[serde(
    rename_all(deserialize = "PascalCase", serialize = "camelCase"),
    default
)]
pub struct PlayerStats {
    pub name: String,
    pub bot: bool,
    pub kills: i32,
    pub deaths: i32,
    pub headshots: i32,
    pub damage: i32,
    pub rounds: i32,
}

pub const STATUS_FILE: &str = "status.json";
/// Written (unix seconds) when Metamod loads the plugin: the server has read gameinfo.gi by then.
pub const LOADED_FILE: &str = "loaded.txt";

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn loadout_keeps_the_plugins_names() {
        let mut l = PlayerLoadout {
            knife_ct: 507,
            agent_ct: Some("agents/models/x.vmdl".into()),
            ..Default::default()
        };
        l.skins_ct.insert(
            7,
            SkinSettings {
                paint: 44,
                ..Default::default()
            },
        );
        let json = serde_json::to_string(&l).unwrap();
        for name in [
            "\"SkinsCT\":{\"7\":",
            "\"KnifeCT\":507",
            "\"AgentCT\":",
            "\"MusicKit\":0",
            "\"StatTrakCount\":0",
        ] {
            assert!(json.contains(name), "{name} missing in {json}");
        }
        assert!(
            !json.contains("AgentT") && !json.contains("Keychain") && !json.contains("Legacy"),
            "nulls are left out like the plugin writes them"
        );
        let back: PlayerLoadout = crate::util::read_json(&json).unwrap();
        assert_eq!(back.skins_ct[&7].paint, 44);
        assert_eq!(back.skins_ct[&7].wear, 0.01);
    }

    #[test]
    fn chunks_are_console_safe() {
        let chunks = encode_loadout(&PlayerLoadout::default());
        assert!(chunks.iter().all(|c| c.len() <= CHUNK_LENGTH
            && c.chars()
                .all(|ch| ch.is_ascii_alphanumeric() || "-_=".contains(ch))));
    }
}
