//! Skin, sticker and charm data from ByMykel's CSGO-API (MIT). Downloaded once, then kept as a small cache in
//! %LocalAppData%\LanSkinTester\data. The records are also the on-disk cache format: renaming a field loses
//! users' cached data. Also writes the plugin's skins\skins.json (names + legacy flags).

use crate::util;
use serde::{de::DeserializeOwned, Deserialize, Serialize};
use serde_json::Value;
use std::collections::HashMap;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;

const API: &str = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/";

pub fn data_dir() -> PathBuf {
    util::data_dir().join("data")
}

#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct WeaponInfo {
    pub def_index: u16,
    pub name: String,
    pub category: String,
    pub image: String,
}

#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct SkinInfo {
    pub def_index: u16,
    pub paint: i32,
    pub weapon: String,
    pub finish: String,
    pub phase: Option<String>,
    pub category: String,
    pub rarity: String,
    pub rarity_color: String,
    pub min_float: f32,
    pub max_float: f32,
    pub image: String,
    pub legacy: bool,
    pub can_stat_trak: bool,
}

impl SkinInfo {
    pub fn display_finish(&self) -> String {
        match &self.phase {
            Some(p) => format!("{} ({p})", self.finish),
            None => self.finish.clone(),
        }
    }
    pub fn full_name(&self) -> String {
        format!("{} | {}", self.weapon, self.display_finish())
    }
}

#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct StickerInfo {
    pub id: i32,
    pub name: String,
    pub rarity: String,
    pub rarity_color: String,
    pub image: String,
    pub effect: String,
}

/// An agent: Team is "T" or "CT", Model the path the server sets on the player.
#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct AgentInfo {
    pub name: String,
    pub team: String,
    pub model: String,
    pub rarity: String,
    pub rarity_color: String,
    pub image: String,
}

/// A pin or a music kit.
#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct CollectibleInfo {
    pub id: i32,
    pub name: String,
    pub rarity: String,
    pub rarity_color: String,
    pub image: String,
}

/// A container: weapon case, capsule, souvenir package, music kit box... Items are what it drops; Rare are a
/// case's knives / gloves.
#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct CaseInfo {
    pub id: String,
    pub name: String,
    pub image: String,
    #[serde(rename = "Type")]
    pub kind: String,
    pub items: Vec<CaseItem>,
    pub rare: Vec<CaseItem>,
    pub rare_image: Option<String>,
}

/// Id is the item data's id ("skin-…", "sticker-123", "music_kit-32", "collectible-6110"...).
#[derive(Serialize, Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "PascalCase", default)]
pub struct CaseItem {
    pub id: String,
    pub name: String,
    pub paint: i32,
    pub rarity: String,
    pub color: String,
    pub image: String,
    pub phase: Option<String>,
}

#[derive(Default)]
pub struct ItemDatabase {
    pub weapons: Vec<WeaponInfo>,
    pub skins: Vec<SkinInfo>,
    pub stickers: Vec<StickerInfo>,
    pub charms: Vec<StickerInfo>,
    pub agents: Vec<AgentInfo>,
    pub pins: Vec<CollectibleInfo>,
    pub music_kits: Vec<CollectibleInfo>,
    pub cases: Vec<CaseInfo>,
    by_paint: HashMap<(u16, i32), usize>,
    stickers_by_id: HashMap<i32, usize>,
    weapons_by_name: OnceLock<HashMap<String, u16>>,
}

impl ItemDatabase {
    pub fn is_empty(&self) -> bool {
        self.skins.is_empty()
    }

    pub fn find(&self, def: u16, paint: i32) -> Option<&SkinInfo> {
        self.by_paint.get(&(def, paint)).map(|&i| &self.skins[i])
    }

    pub fn sticker(&self, id: i32) -> Option<&StickerInfo> {
        self.stickers_by_id.get(&id).map(|&i| &self.stickers[i])
    }

    pub fn weapon(&self, def: u16) -> Option<&WeaponInfo> {
        self.weapons.iter().find(|w| w.def_index == def)
    }

    pub fn load_cached(dir: &Path) -> ItemDatabase {
        fn load<T: DeserializeOwned>(dir: &Path, file: &str) -> Result<Vec<T>, ()> {
            match fs::read_to_string(dir.join(file)) {
                Ok(text) => util::read_json(&text).map_err(drop),
                Err(_) => Ok(vec![]),
            }
        }
        let loaded = (|| -> Result<ItemDatabase, ()> {
            Ok(ItemDatabase {
                weapons: load(dir, "weapons.json")?,
                skins: load(dir, "skins.json")?,
                stickers: load(dir, "stickers.json")?,
                charms: load(dir, "charms.json")?,
                agents: load(dir, "agents.json")?,
                pins: load(dir, "pins.json")?,
                music_kits: load(dir, "musickits.json")?,
                cases: load(dir, "cases.json")?,
                ..Default::default()
            })
        })();
        let Ok(mut db) = loaded else {
            return ItemDatabase::default();
        };
        // An older launcher saved cases without their type and pictures: download again to get them.
        if db
            .cases
            .iter()
            .any(|c| c.kind.is_empty() || c.items.iter().any(|i| i.id.is_empty()))
        {
            db.cases.clear();
        }
        for (i, s) in db.skins.iter().enumerate() {
            db.by_paint.entry((s.def_index, s.paint)).or_insert(i);
        }
        for (i, s) in db.stickers.iter().enumerate() {
            db.stickers_by_id.entry(s.id).or_insert(i);
        }
        db
    }

    /// The weapon (or knife / gloves) a "★ Karambit | Fade" style name is for, or None for stickers and such.
    pub fn weapon_of_item(&self, item_name: &str) -> Option<u16> {
        let map = self.weapons_by_name.get_or_init(|| {
            let mut m = HashMap::new();
            for w in &self.weapons {
                m.entry(w.name.to_lowercase()).or_insert(w.def_index);
            }
            m
        });
        let name = item_name
            .replace('★', "")
            .replace("StatTrak™", "")
            .replace("Souvenir", "");
        let name = name.trim();
        let base = name.split('|').next().unwrap_or(name).trim().to_lowercase();
        map.get(&base).copied()
    }

    /// The plugin reads names + legacy-model flags from its skins folder (cs2-WeaponPaints format).
    pub fn write_plugin_skin_names(&self, dir: &Path) -> std::io::Result<()> {
        fs::create_dir_all(dir)?;
        // Older launcher versions put WeaponPaints files here; one source avoids duplicates.
        for old in ["skins_en.json", "gloves_en.json"] {
            let _ = fs::remove_file(dir.join(old));
        }
        let list: Vec<Value> = self.skins.iter()
            .map(|s| serde_json::json!({ "weapon_defindex": s.def_index, "paint": s.paint, "paint_name": s.full_name(), "legacy_model": s.legacy }))
            .collect();
        fs::write(
            dir.join("skins.json"),
            serde_json::to_string(&list).unwrap(),
        )
    }

    /// Downloads the full lists and rewrites the cache (~25 MB).
    pub fn download(http: &ureq::Agent, dir: &Path, log: &dyn Fn(String)) -> Result<(), String> {
        fs::create_dir_all(dir).map_err(|e| e.to_string())?;
        let get = |file: &str, message: Option<&str>| -> Result<Vec<Value>, String> {
            if let Some(m) = message {
                log(m.into());
            }
            match serde_json::from_str(&util::get_text(http, &format!("{API}{file}"))?)
                .map_err(|e| e.to_string())?
            {
                Value::Array(a) => Ok(a),
                _ => Err(format!("{file} is not a list")),
            }
        };
        fn parse<T>(list: Vec<Value>, f: fn(&Value) -> Option<T>) -> Vec<T> {
            list.iter().filter_map(f).collect()
        }
        let weapons = parse(
            get("base_weapons.json", Some("Downloading weapon list..."))?,
            parse_weapon,
        );
        let skins = parse(
            get("skins.json", Some("Downloading skin list..."))?,
            parse_skin,
        );
        let stickers = parse(
            get(
                "stickers.json",
                Some("Downloading sticker list (big, ~20 MB)..."),
            )?,
            parse_sticker,
        );
        let charms = parse(
            get("keychains.json", Some("Downloading charm list..."))?,
            parse_sticker,
        );
        let agents = parse(
            get("agents.json", Some("Downloading agents and pins..."))?,
            parse_agent,
        );
        let pins = parse(get("collectibles.json", None)?, parse_pin);
        let music = parse(
            get("music_kits.json", Some("Downloading music kits..."))?,
            parse_music_kit,
        );
        let cases = parse(
            get("crates.json", Some("Downloading weapon cases..."))?,
            parse_case,
        );
        fn write<T: Serialize>(dir: &Path, file: &str, v: &T) -> Result<(), String> {
            fs::write(dir.join(file), serde_json::to_string(v).unwrap()).map_err(|e| e.to_string())
        }
        write(dir, "weapons.json", &weapons)?;
        write(dir, "skins.json", &skins)?;
        write(dir, "stickers.json", &stickers)?;
        write(dir, "charms.json", &charms)?;
        write(dir, "agents.json", &agents)?;
        write(dir, "pins.json", &pins)?;
        write(dir, "musickits.json", &music)?;
        write(dir, "cases.json", &cases)?;
        log(format!(
            "Item data ready: {} skins, {} stickers, {} charms, {} agents, {} pins, {} music kits.",
            skins.len(),
            stickers.len(),
            charms.len(),
            agents.len(),
            pins.len(),
            music.len()
        ));
        Ok(())
    }
}

// ---- ByMykel JSON -> compact records ----

fn s(v: &Value) -> String {
    v.as_str().unwrap_or("").to_string()
}

fn int(v: &Value) -> i64 {
    match v {
        Value::Number(n) => n.as_i64().unwrap_or(0),
        Value::String(t) => t.parse().unwrap_or(0),
        _ => 0,
    }
}

fn float(v: &Value, fallback: f32) -> f32 {
    match v {
        Value::Number(n) => n.as_f64().map_or(fallback, |f| f as f32),
        Value::String(t) => t.parse().unwrap_or(fallback),
        _ => fallback,
    }
}

fn rarity(n: &Value) -> (String, String) {
    (s(&n["rarity"]["name"]), s(&n["rarity"]["color"]))
}

fn parse_weapon(n: &Value) -> Option<WeaponInfo> {
    let def = int(&n["def_index"]);
    let category = s(&n["category"]["name"]);
    if def == 0
        || ![
            "Pistols",
            "Rifles",
            "SMGs",
            "Heavy",
            "Knives",
            "Gloves",
            "Equipment",
        ]
        .contains(&category.as_str())
    {
        return None;
    }
    if [42, 59, 5028, 5029].contains(&def) {
        return None; // default team knives/gloves
    }
    Some(WeaponInfo {
        def_index: def as u16,
        name: s(&n["name"]),
        category,
        image: s(&n["image"]),
    })
}

fn parse_agent(n: &Value) -> Option<AgentInfo> {
    let model = s(&n["model_player"]);
    let team = match s(&n["team"]["id"]).as_str() {
        "terrorists" => "T",
        "counter-terrorists" => "CT",
        _ => return None,
    };
    if !model.starts_with("agents/models/") {
        return None;
    }
    let (rarity, rarity_color) = rarity(n);
    Some(AgentInfo {
        name: s(&n["name"]),
        team: team.into(),
        model,
        rarity,
        rarity_color,
        image: s(&n["image"]),
    })
}

fn parse_pin(n: &Value) -> Option<CollectibleInfo> {
    let id = int(&n["def_index"]);
    if s(&n["type"]) != "Pin" || id <= 0 || id > u16::MAX as i64 {
        return None;
    }
    let (rarity, rarity_color) = rarity(n);
    Some(CollectibleInfo {
        id: id as i32,
        name: s(&n["name"]),
        rarity,
        rarity_color,
        image: s(&n["image"]),
    })
}

/// Each kit is listed twice, plain and StatTrak ("music_kit-3_st"); the plain one is enough.
fn parse_music_kit(n: &Value) -> Option<CollectibleInfo> {
    let id = int(&n["def_index"]);
    if id <= 0 || id > u16::MAX as i64 || s(&n["id"]).ends_with("_st") {
        return None;
    }
    let (rarity, rarity_color) = rarity(n);
    let name = s(&n["name"])
        .replace("StatTrak™ ", "")
        .replace("Music Kit | ", "");
    Some(CollectibleInfo {
        id: id as i32,
        name,
        rarity,
        rarity_color,
        image: s(&n["image"]),
    })
}

fn parse_case(n: &Value) -> Option<CaseInfo> {
    let kind = s(&n["type"]);
    let contains = n["contains"].as_array().filter(|a| !a.is_empty())?;
    if kind.is_empty() {
        return None;
    }
    let items = |list: Option<&Vec<Value>>| -> Vec<CaseItem> {
        list.map(|a| {
            a.iter()
                .map(|x| {
                    let (rarity, color) = rarity(x);
                    CaseItem {
                        id: s(&x["id"]),
                        name: s(&x["name"]),
                        paint: int(&x["paint_index"]) as i32,
                        rarity,
                        color,
                        image: s(&x["image"]),
                        phase: x["phase"].as_str().map(String::from),
                    }
                })
                .filter(|i| !i.name.is_empty())
                .collect()
        })
        .unwrap_or_default()
    };
    // The case's own "Rare Special Item" picture (the gold card the game shows in the reel).
    let rare_image = s(&n["loot_list"]["image"]);
    Some(CaseInfo {
        id: s(&n["id"]),
        name: s(&n["name"]),
        image: s(&n["image"]),
        kind,
        items: items(Some(contains)),
        rare: items(n["contains_rare"].as_array()),
        rare_image: (!rare_image.is_empty()).then_some(rare_image),
    })
}

fn parse_skin(n: &Value) -> Option<SkinInfo> {
    let def = int(&n["weapon"]["weapon_id"]);
    let paint = int(&n["paint_index"]);
    if def == 0 || paint == 0 {
        return None;
    }
    let name = s(&n["name"]).replace("★ ", "");
    let finish = match name.find('|') {
        Some(bar) => name[bar + 1..].trim().to_string(),
        None => s(&n["pattern"]["name"]),
    };
    let phase = n["phase"]
        .as_str()
        .filter(|p| !p.is_empty())
        .map(String::from);
    let (rarity, rarity_color) = rarity(n);
    Some(SkinInfo {
        def_index: def as u16,
        paint: paint as i32,
        weapon: s(&n["weapon"]["name"]),
        finish,
        phase,
        category: s(&n["category"]["name"]),
        rarity,
        rarity_color,
        min_float: float(&n["min_float"], 0.0),
        max_float: float(&n["max_float"], 1.0),
        image: s(&n["image"]),
        legacy: n["legacy_model"].as_bool().unwrap_or(false),
        can_stat_trak: n["stattrak"].as_bool().unwrap_or(false),
    })
}

fn parse_sticker(n: &Value) -> Option<StickerInfo> {
    let id = int(&n["def_index"]);
    if id == 0 {
        return None;
    }
    let mut name = s(&n["name"]);
    for prefix in ["Sticker | ", "Charm | "] {
        if let Some(rest) = name.strip_prefix(prefix) {
            name = rest.to_string();
        }
    }
    let (rarity, rarity_color) = rarity(n);
    Some(StickerInfo {
        id: id as i32,
        name,
        rarity,
        rarity_color,
        image: s(&n["image"]),
        effect: s(&n["effect"]),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_old_launchers_cache() {
        let dir = std::env::temp_dir().join(format!("lst-db-{}", util::random_hex(6)));
        fs::create_dir_all(&dir).unwrap();
        fs::write(
            dir.join("weapons.json"),
            r#"[{"DefIndex":507,"Name":"Karambit","Category":"Knives","Image":"k.png"}]"#,
        )
        .unwrap();
        fs::write(dir.join("skins.json"), r##"[{"DefIndex":7,"Paint":282,"Weapon":"AK-47","Finish":"Redline","Phase":null,"Category":"Rifles","Rarity":"Classified","RarityColor":"#d32ce6","MinFloat":0.1,"MaxFloat":0.7,"Image":"x","Legacy":true,"CanStatTrak":true,"DisplayFinish":"Redline","FullName":"AK-47 | Redline"}]"##).unwrap();
        let db = ItemDatabase::load_cached(&dir);
        assert_eq!(db.find(7, 282).unwrap().full_name(), "AK-47 | Redline");
        assert_eq!(db.weapon_of_item("★ StatTrak™ Karambit | Fade"), Some(507));
        assert_eq!(db.weapon_of_item("Sticker | x"), None);
        fs::remove_dir_all(dir).unwrap();
    }
}
