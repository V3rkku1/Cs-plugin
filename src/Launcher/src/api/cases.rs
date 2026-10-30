//! Cases and Inventory pages: open CS2 containers like in the game (reel, real odds, floats, StatTrak), keep
//! what you unbox in an inventory and equip it on the local server. Without the owner's admin code only what
//! was unboxed here can be equipped; the code unlocks every skin. Nothing here touches a Steam inventory.

mod extras;

use super::{arg, bad, ok, unknown, App, State, R};
use crate::itemdb::{CaseInfo, CaseItem};
use crate::items::{
    is_glove_type, is_knife_type, LoadoutStore, PlayerLoadout, SkinSettings, StickerSettings,
};
use crate::odds;
use crate::prices::{self, CURRENCIES};
use crate::server::SKIN_COMMANDS;
use crate::session;
use crate::util;
use rand::Rng;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::collections::{BTreeMap, HashSet};
use std::fs;
use std::path::PathBuf;
use std::sync::atomic::Ordering;
use std::sync::Arc;

/// SHA-256 of "lst-cases-v1:" + the code in capitals. Only the owner knows the code.
pub const UNLOCK_HASH: &str = "5e0d1df55fe4b0fc7528ff9f2b62aec673bb31d82ed6c9bfc7179bf3fd557f69";
/// Most items kept: past this, opening stops (the inventory file and pages get slow beyond it).
const MAX_INVENTORY: usize = 50_000;
/// Guns only one side can buy: their skin is the same for both sides.
const ONE_SIDE_GUNS: [u16; 18] = [
    4, 30, 17, 29, 7, 13, 39, 11, 61, 32, 3, 34, 27, 16, 60, 10, 8, 38,
];

/// An unboxed item (inventory.json, PascalCase like older launchers wrote it). Def/Paint for weapons, knives and
/// gloves; CollectibleId for pins and music kits.
#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct InventoryItem {
    pub uid: String,
    pub kind: String,
    pub name: String,
    pub image: String,
    pub grade: String,
    pub color: String,
    pub def: u16,
    pub paint: i32,
    pub wear: f32,
    pub seed: i32,
    pub stat_trak: bool,
    /// Kills counted on the server (the plugin keeps them in the loadout; copied back here).
    pub stat_trak_count: i32,
    pub souvenir: bool,
    pub phase: Option<String>,
    pub case_name: String,
    pub collectible_id: i32,
    pub opened: String,
    pub item_id: Option<String>,
    pub team: Option<String>,
    pub stickers: Option<Vec<AppliedSticker>>,
}

/// A sticker stuck on a gun: which one, where (slot, offset, rotation, scale) and how scraped.
#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct AppliedSticker {
    pub slot: i32,
    pub id: i32,
    pub name: String,
    pub image: String,
    pub offset_x: f32,
    pub offset_y: f32,
    pub rotation: f32,
    pub scale: f32,
    pub wear: f32,
}

/// The inventory, read once and kept (calls that use it hold the inventory gate).
#[derive(Default)]
pub struct Inv {
    items: Option<Vec<InventoryItem>>,
}

pub fn inventory_file() -> PathBuf {
    util::data_dir().join("inventory.json")
}

fn removed_file() -> PathBuf {
    util::data_dir().join("inventory-removed.json")
}

impl Inv {
    pub fn forget(&mut self) {
        self.items = None;
    }

    pub fn items(&mut self, app: &App) -> R<&mut Vec<InventoryItem>> {
        if self.items.is_none() {
            let file = inventory_file();
            let text = match fs::read_to_string(&file) {
                Ok(t) => t,
                Err(e) if e.kind() == std::io::ErrorKind::NotFound => "[]".into(),
                // Not cached: an empty list now would be saved over the real one with the next change.
                Err(e) => {
                    return Err(bad(format!(
                        "Could not read your inventory (is another program using it?): {e}"
                    )))
                }
            };
            self.items = Some(match util::read_json(&text) {
                Ok(items) => items,
                Err(e) => {
                    let aside = util::set_aside(&file);
                    app.log(format!("Your inventory file is damaged ({e}). {}", match aside {
                        Some(a) => format!("It was kept as {}; starting with an empty inventory.", a.file_name().unwrap().to_string_lossy()),
                        None => "It couldn't be moved aside, so the next change replaces it.".into(),
                    }));
                    vec![]
                }
            });
        }
        Ok(self.items.as_mut().unwrap())
    }

    pub fn save(&mut self, app: &App) {
        let Some(items) = &self.items else { return };
        let result = fs::create_dir_all(util::data_dir()).and_then(|_| {
            util::safe_write(&inventory_file(), &serde_json::to_string(items).unwrap())
        });
        if let Err(e) = result {
            app.log(format!("Could not save the inventory: {e}"));
        }
    }

    /// Refuses an opening that would go past the cap: dropping the oldest could take equipped guns.
    fn ensure_room(&mut self, app: &App, adding: usize) -> R<()> {
        if self.items(app)?.len() + adding > MAX_INVENTORY {
            return Err(bad(format!("Your inventory is full ({MAX_INVENTORY} items). Remove Duplicates or delete items to open more.")));
        }
        Ok(())
    }
}

pub fn new_uid() -> String {
    util::random_hex(6).to_lowercase()
}

fn now() -> String {
    chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true)
}

fn find<'a>(items: &'a [InventoryItem], uid: &str) -> R<&'a InventoryItem> {
    items
        .iter()
        .find(|i| i.uid == uid)
        .ok_or_else(|| bad("That item isn't in your inventory any more."))
}

/// An inventory item as the pages show it: its price, whether it's equipped, its name split like CS2 shows it.
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub struct InventoryView {
    item: Value,
    price: Option<f64>,
    equipped: bool,
    market_name: String,
    weapon: String,
    finish: String,
    category: &'static str,
    #[serde(rename = "equippedT")]
    equipped_t: bool,
    #[serde(rename = "equippedCT")]
    equipped_ct: bool,
}

fn kind_name(kind: &str) -> &'static str {
    match kind {
        "agent" => "Agent",
        "sticker" => "Sticker",
        "patch" => "Patch",
        "graffiti" => "Graffiti",
        "music" => "Music Kit",
        "pin" => "Pin",
        "charm" => "Charm",
        _ => "Item",
    }
}

pub fn market(i: &InventoryItem) -> String {
    prices::market_name(
        &i.name,
        i.kind == "skin",
        i.paint != 0,
        i.wear,
        i.stat_trak,
        i.souvenir,
    )
}

pub fn price_of(app: &App, st: &State, i: &InventoryItem) -> Option<f64> {
    app.prices.ensure(&st.settings.currency);
    app.prices.price(&market(i))
}

fn matches(s: Option<&SkinSettings>, i: &InventoryItem) -> bool {
    s.is_some_and(|s| {
        s.paint == i.paint
            && s.seed == i.seed
            && (s.wear - if i.wear > 0.0 { i.wear } else { 0.01 }).abs() < 0.0000005
    })
}

/// What a player of that side (2 = T, 3 = CT) wears, the same way the plugin picks it.
fn worn_on(l: &PlayerLoadout, i: &InventoryItem, team: i32) -> bool {
    (!is_knife_type(i.def) || l.knife_for(team) == i.def)
        && (!is_glove_type(i.def) || l.gloves_for(team) == i.def)
        && matches(l.skin_for(i.def, team), i)
}

pub fn view(app: &App, st: &State, i: &InventoryItem, l: &PlayerLoadout) -> InventoryView {
    let skin = i.kind == "skin";
    let (equipped_t, equipped_ct) = (skin && worn_on(l, i, 2), skin && worn_on(l, i, 3));
    let item_id = i.item_id.as_deref();
    let equipped = match i.kind.as_str() {
        "skin" => equipped_t || equipped_ct,
        "pin" => i.collectible_id > 0 && l.pin as i32 == i.collectible_id,
        "music" => i.collectible_id > 0 && l.music_kit as i32 == i.collectible_id,
        "agent" => {
            item_id.is_some()
                && (l.agent_ct.as_deref() == item_id || l.agent_t.as_deref() == item_id)
        }
        _ => false,
    };
    let name = i.name.replace('★', "");
    let name = name.trim();
    let (weapon, finish) = match name.split_once('|') {
        Some((w, f)) => (w.trim().to_string(), f.trim().to_string()),
        None if skin => (name.to_string(), "Vanilla".to_string()),
        None => (kind_name(&i.kind).to_string(), name.to_string()),
    };
    let category = match i.kind.as_str() {
        "skin" if is_knife_type(i.def) || is_glove_type(i.def) => "special",
        "skin" => "weapons",
        "sticker" | "patch" | "graffiti" => "graphics",
        _ => "other",
    };
    InventoryView {
        item: util::web(i),
        price: price_of(app, st, i),
        equipped,
        market_name: market(i),
        weapon,
        finish,
        category,
        equipped_t,
        equipped_ct,
    }
}

/// The local loadout (loadouts.json). Without the admin code, anything not in the inventory is dropped.
pub fn read_loadout(app: &App, inv: &mut Inv, st: &State) -> R<PlayerLoadout> {
    let Some(g) = st.game.as_ref().filter(|g| g.plugin_dir().is_dir()) else {
        return Ok(PlayerLoadout::default());
    };
    let mut store = LoadoutStore::open(&g.loadout_file());
    if !st.settings.admin_unlocked && prune_unowned(store.local(), inv.items(app)?) {
        store.save()?;
    }
    Ok(store.local().clone())
}

fn owned_ids(items: &[InventoryItem], kind: &str) -> HashSet<i32> {
    items
        .iter()
        .filter(|i| i.kind == kind)
        .map(|i| {
            if i.collectible_id > 0 {
                i.collectible_id
            } else {
                id_number(i.item_id.as_deref())
            }
        })
        .filter(|n| *n > 0)
        .collect()
}

/// "sticker-123" gives 123, "music_kit-3_st" gives 3.
fn id_number(item_id: Option<&str>) -> i32 {
    item_id
        .and_then(|id| id.split('-').nth(1))
        .and_then(|n| n.split('_').next())
        .and_then(|n| n.parse().ok())
        .unwrap_or(0)
}

fn prune_unowned(l: &mut PlayerLoadout, items: &[InventoryItem]) -> bool {
    let skins: HashSet<(u16, i32)> = items
        .iter()
        .filter(|i| i.kind == "skin" && i.def != 0)
        .map(|i| (i.def, i.paint))
        .collect();
    // Applying a sticker uses it up, so the ones stuck on unboxed guns count as owned too.
    let mut stickers = owned_ids(items, "sticker");
    stickers.extend(
        items
            .iter()
            .flat_map(|i| i.stickers.iter().flatten().map(|s| s.id)),
    );
    let (charms, pins, kits) = (
        owned_ids(items, "charm"),
        owned_ids(items, "pin"),
        owned_ids(items, "music"),
    );
    let mut changed = false;
    for side in [&mut l.skins_t, &mut l.skins_ct] {
        let before = side.len();
        side.retain(|def, s| s.paint == 0 || skins.contains(&(*def, s.paint)));
        changed |= side.len() != before;
    }
    let before = l.skins.len();
    // Vanilla guns (paint 0) are always fine; knives and gloves must be owned either way.
    l.skins.retain(|def, s| {
        !((s.paint != 0 || is_knife_type(*def) || is_glove_type(*def))
            && !skins.contains(&(*def, s.paint)))
    });
    changed |= l.skins.len() != before;
    for s in l.skins.values_mut() {
        let before = s.stickers.len();
        s.stickers.retain(|x| stickers.contains(&x.id));
        changed |= s.stickers.len() != before;
        if s.keychain.as_ref().is_some_and(|k| !charms.contains(&k.id)) {
            s.keychain = None;
            changed = true;
        }
    }
    for slot in [
        &mut l.knife,
        &mut l.gloves,
        &mut l.knife_t,
        &mut l.knife_ct,
        &mut l.gloves_t,
        &mut l.gloves_ct,
    ] {
        if *slot != 0 && !skins.iter().any(|o| o.0 == *slot) {
            *slot = 0;
            changed = true;
        }
    }
    if l.pin != 0 && !pins.contains(&(l.pin as i32)) {
        l.pin = 0;
        changed = true;
    }
    if l.music_kit != 0 && !kits.contains(&(l.music_kit as i32)) {
        l.music_kit = 0;
        changed = true;
    }
    if l.agent_t.is_some() || l.agent_ct.is_some() {
        (l.agent_t, l.agent_ct) = (None, None); // agents need the admin code
        changed = true;
    }
    changed
}

fn require_plugin(st: &State) -> R<LoadoutStore> {
    match &st.game {
        Some(g) if g.plugin_installed() => Ok(LoadoutStore::open(&g.loadout_file())),
        _ => Err(bad("Install the plugin first (Setup page).")),
    }
}

pub fn skin_of(item: &InventoryItem) -> SkinSettings {
    SkinSettings {
        paint: item.paint,
        wear: if item.wear > 0.0 { item.wear } else { 0.01 },
        seed: item.seed,
        stat_trak: item.stat_trak,
        stat_trak_count: item.stat_trak_count,
        stickers: item
            .stickers
            .iter()
            .flatten()
            .map(|s| StickerSettings {
                slot: s.slot,
                id: s.id,
                wear: s.wear,
                scale: s.scale,
                rotation: s.rotation,
                offset_x: s.offset_x,
                offset_y: s.offset_y,
            })
            .collect(),
        ..Default::default()
    }
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct OpenManyArgs {
    id: String,
    count: i32,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct SideArgs {
    uid: String,
    team: i32,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct CatalogQuery {
    kind: Option<String>,
    search: Option<String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct AddItemArgs {
    kind: String,
    key: String,
    wear: f32,
    seed: i32,
    stat_trak: bool,
    souvenir: bool,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct StickerArgs {
    gun: String,
    sticker: String,
    slot: i32,
    offset_x: f32,
    offset_y: f32,
    rotation: f32,
    scale: f32,
    wear: f32,
}

pub fn call(app: &Arc<App>, inv: &mut Inv, name: &str, a: &Value) -> R<Value> {
    if let Some(result) = extras::call(app, inv, name, a) {
        return result;
    }
    match name {
        "cases.state" => {
            let st = app.state();
            let items = inv.items(app)?;
            ok(cases_state(app, &st, items))
        }
        "cases.contents" => {
            let st = app.state();
            ok(contents(app, &st, &arg::<String>(a)?)?)
        }
        "cases.open" => {
            let id: String = arg(a)?;
            let mut st = app.state();
            let case = find_case(&st, &id)?;
            inv.ensure_room(app, 1)?;
            let mut rng = rand::thread_rng();
            let (drop, rare) = odds::roll(&case, &mut rng);
            // The reel: what else could have come out, by the same odds. Rare items show as the gold card.
            let (length, win) = (56, 48);
            let card = |item: &CaseItem, rare: bool| {
                if rare {
                    json!({ "name": "★ Rare Special Item ★", "image": case.rare_image.clone().unwrap_or_default(), "color": "#e4ae39", "rare": true })
                } else {
                    json!({ "name": item.name, "image": item.image, "color": item.color, "rare": false })
                }
            };
            let reel: Vec<Value> = (0..length)
                .map(|i| {
                    if i == win {
                        card(&drop, rare)
                    } else {
                        let (f, r) = odds::roll(&case, &mut rng);
                        card(&f, r)
                    }
                })
                .collect();
            let item = make_item(&st, &case, &drop, &mut rng);
            inv.items(app)?.insert(0, item.clone());
            inv.save(app);
            st.settings.cases_opened += 1;
            *st.settings.case_opens.entry(case.id.clone()).or_default() += 1;
            app.save(&st);
            let loadout = read_loadout(app, inv, &st)?;
            ok(json!({ "reel": reel, "win": win, "item": view(app, &st, &item, &loadout) }))
        }
        "cases.openMany" => {
            let a: OpenManyArgs = arg(a)?;
            let mut st = app.state();
            let case = find_case(&st, &a.id)?;
            let count = a.count.clamp(1, 50) as usize;
            inv.ensure_room(app, count)?;
            let mut rng = rand::thread_rng();
            let new: Vec<InventoryItem> = (0..count)
                .map(|_| {
                    let (d, _) = odds::roll(&case, &mut rng);
                    make_item(&st, &case, &d, &mut rng)
                })
                .collect();
            inv.items(app)?.splice(0..0, new.iter().cloned());
            inv.save(app);
            st.settings.cases_opened += count as i32;
            *st.settings.case_opens.entry(case.id.clone()).or_default() += count as i32;
            app.save(&st);
            let loadout = read_loadout(app, inv, &st)?;
            ok(new
                .iter()
                .map(|i| view(app, &st, i, &loadout))
                .collect::<Vec<_>>())
        }
        "cases.inventory" => {
            let st = app.state();
            let loadout = read_loadout(app, inv, &st)?;
            if extras::sync_stat_trak(inv.items(app)?, &loadout) {
                inv.save(app);
            }
            let items: Vec<InventoryView> = inv
                .items(app)?
                .iter()
                .map(|i| view(app, &st, i, &loadout))
                .collect();
            let total: f64 = items.iter().filter_map(|v| v.price).sum();
            let undo = last_removed().len();
            ok(
                json!({ "items": items, "total": (total * 100.0).round() / 100.0 + 0.0, "currency": st.settings.currency, "admin": st.settings.admin_unlocked, "undo": undo,
                    "presets": st.settings.loadout_presets.keys().collect::<Vec<_>>() }),
            )
        }
        "cases.currency" => {
            let currency: String = arg(a)?;
            if CURRENCIES.contains(&currency.as_str()) {
                let mut st = app.state();
                st.settings.currency = currency;
                app.save(&st);
                drop(st);
                app.changed();
            }
            ok(())
        }
        "cases.unequip" => {
            let st = app.state();
            unequip(app, inv, &st, &arg::<String>(a)?)?;
            ok(())
        }
        "cases.equip" => {
            let st = app.state();
            equip(app, inv, &st, &arg::<String>(a)?)?;
            ok(())
        }
        // Inspect room: equip it, empty the server of bots and put it in your hands (gloves just show).
        // Skipped: a fixed camera and lighting choice (map-specific); third person / noclip are on Live › Practice.
        "cases.tryInGame" => {
            let uid: String = arg(a)?;
            let st = app.state();
            equip(app, inv, &st, &uid)?;
            let def = find(inv.items(app)?, &uid)?.def;
            let (Some(game), Some(_)) = (st.game.clone(), st.server_status()) else {
                app.toast(
                    "Equipped. Start the server (Play) to try it in game.",
                    "info",
                );
                return ok(());
            };
            let mut commands = vec!["bot_kick".to_string()];
            if is_knife_type(def) {
                commands.push("lst_give weapon_knife".into());
            } else if let Some(classname) = crate::items::classname(def) {
                commands.push(format!("lst_give {classname}"));
            }
            crate::server::send_commands(&game, &commands)
                .map_err(|e| bad(format!("Could not send: {e}")))?;
            ok(())
        }
        "cases.equipSide" => {
            let a: SideArgs = arg(a)?;
            let st = app.state();
            let item = inv
                .items(app)?
                .iter()
                .find(|i| i.uid == a.uid && i.kind == "skin" && i.def != 0)
                .cloned()
                .ok_or_else(|| bad("That skin isn't in your inventory any more."))?;
            if !(a.team == 2 || a.team == 3) || ONE_SIDE_GUNS.contains(&item.def) {
                equip(app, inv, &st, &a.uid)?;
                return ok(());
            }
            let mut store = require_plugin(&st)?;
            let l = store.local();
            let t = a.team == 2;
            // Knife / gloves per side: the other side keeps the shared one it had.
            if is_knife_type(item.def) {
                let (mine, other) = if t {
                    (&mut l.knife_t, &mut l.knife_ct)
                } else {
                    (&mut l.knife_ct, &mut l.knife_t)
                };
                if *other == 0 {
                    *other = l.knife;
                }
                *mine = item.def;
            }
            if is_glove_type(item.def) {
                let (mine, other) = if t {
                    (&mut l.gloves_t, &mut l.gloves_ct)
                } else {
                    (&mut l.gloves_ct, &mut l.gloves_t)
                };
                if *other == 0 {
                    *other = l.gloves;
                }
                *mine = item.def;
            }
            // The other side keeps the shared skin it had before this one became side-specific.
            if let Some(shared) = l.skins.get(&item.def).cloned() {
                l.skins_t.entry(item.def).or_insert(shared.clone());
                l.skins_ct.entry(item.def).or_insert(shared);
            }
            if t { &mut l.skins_t } else { &mut l.skins_ct }.insert(item.def, skin_of(&item));
            store.save()?;
            app.toast(
                if t {
                    "Equipped for Terrorists."
                } else {
                    "Equipped for Counter-Terrorists."
                },
                "good",
            );
            app.changed();
            ok(())
        }
        "cases.unequipSide" => {
            let a: SideArgs = arg(a)?;
            let st = app.state();
            let item = inv
                .items(app)?
                .iter()
                .find(|i| i.uid == a.uid && i.kind == "skin" && i.def != 0)
                .cloned()
                .ok_or_else(|| bad("That skin isn't in your inventory any more."))?;
            if !(a.team == 2 || a.team == 3) || ONE_SIDE_GUNS.contains(&item.def) {
                unequip(app, inv, &st, &a.uid)?;
                return ok(());
            }
            let mut store = require_plugin(&st)?;
            let l = store.local();
            let t = a.team == 2;
            if is_knife_type(item.def) || is_glove_type(item.def) {
                // This side back to the default knife / gloves; the other side keeps what it wears.
                let knife = is_knife_type(item.def);
                let (shared, kt, kct) = if knife {
                    (&mut l.knife, &mut l.knife_t, &mut l.knife_ct)
                } else {
                    (&mut l.gloves, &mut l.gloves_t, &mut l.gloves_ct)
                };
                let (mine, other) = if t { (kt, kct) } else { (kct, kt) };
                if *other == 0 {
                    *other = *shared;
                }
                (*shared, *mine) = (0, 0);
                if t { &mut l.skins_t } else { &mut l.skins_ct }.remove(&item.def);
                store.save()?;
                app.toast(
                    if knife {
                        "Default knife on that side."
                    } else {
                        "Default gloves on that side."
                    },
                    "info",
                );
                app.changed();
                return ok(());
            }
            // The shared skin becomes the other side's own, so it stays there.
            if let Some(shared) = l.skins.remove(&item.def) {
                if t { &mut l.skins_ct } else { &mut l.skins_t }
                    .entry(item.def)
                    .or_insert(shared);
            }
            // An empty (default) skin on this side wins over anything shared.
            if t { &mut l.skins_t } else { &mut l.skins_ct }
                .insert(item.def, SkinSettings::default());
            store.save()?;
            app.toast("Default gun on that side.", "info");
            app.changed();
            ok(())
        }
        "cases.remove" => {
            let uid: String = arg(a)?;
            inv.items(app)?.retain(|i| i.uid != uid);
            inv.save(app);
            app.changed();
            ok(())
        }
        "cases.undoRemove" => {
            let items = inv.items(app)?;
            let back: Vec<InventoryItem> = last_removed()
                .into_iter()
                .filter(|r| !items.iter().any(|i| i.uid == r.uid))
                .collect();
            let n = back.len();
            items.splice(0..0, back);
            inv.save(app);
            let _ = fs::remove_file(removed_file());
            app.toast(
                format!(
                    "{n} item{} back in your inventory.",
                    if n == 1 { "" } else { "s" }
                ),
                "good",
            );
            app.changed();
            ok(())
        }
        "cases.removeMany" => {
            let uids: HashSet<String> = arg::<Vec<String>>(a)?.into_iter().collect();
            let items = inv.items(app)?;
            let batch: Vec<&InventoryItem> =
                items.iter().filter(|i| uids.contains(&i.uid)).collect();
            if batch.is_empty() {
                return ok(0); // keep the last Undo Remove
            }
            // Kept for Undo Remove (the last batch only).
            util::safe_write(&removed_file(), &serde_json::to_string(&batch).unwrap())?;
            let before = items.len();
            items.retain(|i| !uids.contains(&i.uid));
            let removed = before - items.len();
            inv.save(app);
            app.toast(
                format!(
                    "Removed {removed} duplicate{}.",
                    if removed == 1 { "" } else { "s" }
                ),
                "good",
            );
            app.changed();
            ok(removed)
        }
        "cases.unlock" => {
            let code: String = arg(a)?;
            let hash = util::hex(&Sha256::digest(
                format!("lst-cases-v1:{}", code.trim().to_uppercase()).as_bytes(),
            ))
            .to_lowercase();
            let same = hash.len() == UNLOCK_HASH.len()
                && hash
                    .bytes()
                    .zip(UNLOCK_HASH.bytes())
                    .fold(0u8, |d, (x, y)| d | (x ^ y))
                    == 0;
            if !same {
                app.toast("That code doesn't work.", "bad");
                return ok(false);
            }
            set_admin(app, true);
            app.toast(
                "Admin: every skin, knife, glove, sticker and agent is unlocked on this PC.",
                "good",
            );
            ok(true)
        }
        "cases.lock" => {
            set_admin(app, false);
            app.toast("Back to unbox only.", "info");
            ok(())
        }
        "inventory.presetSave" => {
            let name: String = arg(a)?;
            let name: String = name.trim().chars().take(30).collect();
            if name.is_empty() {
                return Err(bad("Give the preset a name."));
            }
            let mut st = app.state();
            require_plugin(&st)?;
            if st.settings.loadout_presets.len() >= 30
                && !st.settings.loadout_presets.contains_key(&name)
            {
                return Err(bad("30 presets at most: delete one first."));
            }
            let loadout = read_loadout(app, inv, &st)?;
            st.settings.loadout_presets.insert(name.clone(), loadout);
            app.save(&st);
            drop(st);
            app.toast(format!("Saved your loadout as \"{name}\"."), "good");
            app.changed();
            ok(())
        }
        "inventory.presetApply" => {
            let name: String = arg(a)?;
            let st = app.state();
            let mut store = require_plugin(&st)?;
            let mut loadout = st
                .settings
                .loadout_presets
                .get(&name)
                .cloned()
                .ok_or_else(|| bad("That preset is gone."))?;
            // Skins traded up or removed since the preset was saved are left out (unless admin).
            if !st.settings.admin_unlocked {
                prune_unowned(&mut loadout, inv.items(app)?);
            }
            *store.local() = loadout;
            store.save()?;
            drop(st);
            app.toast(format!("Loadout \"{name}\" equipped."), "good");
            app.changed();
            ok(())
        }
        "inventory.presetDelete" => {
            let name: String = arg(a)?;
            let mut st = app.state();
            st.settings.loadout_presets.remove(&name);
            app.save(&st);
            drop(st);
            app.changed();
            ok(())
        }
        "inventory.weaponPics" => {
            let st = app.state();
            let mut pics = BTreeMap::new();
            for w in &st.db.weapons {
                pics.entry(w.def_index.to_string())
                    .or_insert(w.image.clone());
            }
            ok(pics)
        }
        "inventory.catalog" => {
            let st = app.state();
            require_admin(&st, "Adding items")?;
            ok(catalog(&st, arg(a)?))
        }
        "inventory.add" => {
            let a: AddItemArgs = arg(a)?;
            let st = app.state();
            require_admin(&st, "Adding items")?;
            let c = resolve_catalog(&st, &a.kind, &a.key).ok_or_else(|| bad("Unknown item."))?;
            let wear = if c.kind == "skin" && c.paint != 0 {
                a.wear.clamp(c.min.max(0.000001), c.max.min(0.999999))
            } else {
                0.0
            };
            let item = InventoryItem {
                uid: new_uid(),
                kind: c.kind.clone(),
                name: c.name,
                image: c.image,
                grade: c.grade,
                color: c.color,
                def: c.def,
                paint: c.paint,
                wear,
                seed: a.seed.clamp(0, 1000),
                stat_trak: a.stat_trak && c.can_stat_trak,
                stat_trak_count: 0,
                souvenir: a.souvenir && c.kind == "skin",
                phase: None,
                case_name: "Admin".into(),
                collectible_id: if c.kind == "pin" || c.kind == "music" {
                    c.id
                } else {
                    0
                },
                opened: now(),
                item_id: Some(if c.kind == "agent" {
                    c.model.clone().unwrap_or_default()
                } else {
                    c.key
                }),
                team: c.team,
                stickers: None,
            };
            inv.items(app)?.insert(0, item.clone());
            inv.save(app);
            app.changed();
            let loadout = read_loadout(app, inv, &st)?;
            ok(view(app, &st, &item, &loadout))
        }
        "inventory.stickers" => {
            let list: Vec<Value> = inv.items(app)?.iter().filter(|i| i.kind == "sticker" && sticker_id(i) > 0)
                .map(|i| json!({ "uid": i.uid, "id": sticker_id(i), "name": i.name, "image": i.image, "color": i.color }))
                .collect();
            ok(list)
        }
        "inventory.applySticker" => {
            let a: StickerArgs = arg(a)?;
            let st = app.state();
            let items = inv.items(app)?;
            let gun = gun_for(items, &a.gun)?;
            let sticker = items
                .iter()
                .find(|i| i.uid == a.sticker && i.kind == "sticker")
                .cloned()
                .ok_or_else(|| bad("That sticker isn't in your inventory any more."))?;
            let mut list = gun.stickers.clone().unwrap_or_default();
            if list.len() >= 5 {
                return Err(bad("A gun holds up to 5 stickers. Scrape one off first."));
            }
            // The slot the page picked (nearest to where you clicked) when it is free, else the first free one.
            let free = |n: i32| list.iter().all(|s| s.slot != n);
            let slot = if (0..=4).contains(&a.slot) && free(a.slot) {
                a.slot
            } else {
                (0..5).find(|n| free(*n)).unwrap()
            };
            list.push(AppliedSticker {
                slot,
                id: sticker_id(&sticker),
                name: sticker.name.clone(),
                image: sticker.image.clone(),
                offset_x: a.offset_x.clamp(-1.0, 1.0),
                offset_y: a.offset_y.clamp(-1.0, 1.0),
                rotation: a.rotation.clamp(-180.0, 180.0),
                scale: a.scale.clamp(0.5, 2.0),
                wear: 0.0,
            });
            items.retain(|i| i.uid != sticker.uid); // used up, like in the game
            let updated = InventoryItem {
                stickers: Some(list),
                ..gun.clone()
            };
            replace_gun(app, inv, &st, &gun, updated)?;
            app.toast("Sticker applied.", "good");
            ok(())
        }
        "inventory.placeSticker" => {
            let a: StickerArgs = arg(a)?;
            let st = app.state();
            let gun = gun_for(inv.items(app)?, &a.gun)?;
            let mut list = gun.stickers.clone().unwrap_or_default();
            let at = list
                .iter()
                .position(|s| s.slot == a.slot)
                .ok_or_else(|| bad("No sticker in that slot."))?;
            // Scraped to 100% it comes off for good.
            if a.wear >= 1.0 {
                list.remove(at);
                app.toast("Sticker scraped off.", "info");
            } else {
                let s = &mut list[at];
                (s.offset_x, s.offset_y) =
                    (a.offset_x.clamp(-1.0, 1.0), a.offset_y.clamp(-1.0, 1.0));
                (s.rotation, s.scale, s.wear) = (
                    a.rotation.clamp(-180.0, 180.0),
                    a.scale.clamp(0.5, 2.0),
                    a.wear.clamp(0.0, 1.0),
                );
            }
            let updated = InventoryItem {
                stickers: Some(list),
                ..gun.clone()
            };
            replace_gun(app, inv, &st, &gun, updated)?;
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

fn set_admin(app: &App, on: bool) {
    let mut st = app.state();
    st.settings.admin_unlocked = on;
    st.settings.admin_hash = if on {
        UNLOCK_HASH.into()
    } else {
        String::new()
    };
    SKIN_COMMANDS.store(on, Ordering::Relaxed);
    app.save(&st);
    if let Some(g) = st.game.clone() {
        let _ = st.settings.server.write_cfg(&g);
        super::friends::send_if_running(app, &st, &format!("lst_skin_commands {}", on as i32));
    }
    drop(st);
    app.changed();
}

fn require_admin(st: &State, what: &str) -> R<()> {
    if st.settings.admin_unlocked {
        Ok(())
    } else {
        Err(bad(format!(
            "{what} needs the admin code (Cases page). Without it, skins come from opening cases."
        )))
    }
}

/// The free weekly drop: a random weapon skin from the whole pool, rarer grades much less likely, so a Covert
/// like AWP | Medusa is a lucky drop. Knives and gloves stay case-only, as in CS2.
const CARE_PACKAGE_ID: &str = "care-package";

/// The drop pool: every painted weapon skin, knives and gloves left out (those stay case-only, as in CS2).
fn care_package_items(skins: &[crate::itemdb::SkinInfo]) -> Vec<CaseItem> {
    skins
        .iter()
        .filter(|s| {
            s.paint != 0 && s.category != "Knives" && s.category != "Gloves" && !s.finish.is_empty()
        })
        .map(|s| CaseItem {
            id: format!("skin-{}", s.paint),
            name: s.full_name(),
            paint: s.paint,
            rarity: s.rarity.clone(),
            color: s.rarity_color.clone(),
            image: s.image.clone(),
            phase: s.phase.clone(),
        })
        .collect()
}

fn care_package(st: &State) -> CaseInfo {
    let items = care_package_items(&st.db.skins);
    // Show AWP | Medusa on the package when it's in the pool, else whatever is first.
    let cover = items
        .iter()
        .find(|i| i.name == "AWP | Medusa")
        .or_else(|| items.first())
        .map_or_else(String::new, |i| i.image.clone());
    CaseInfo {
        id: CARE_PACKAGE_ID.into(),
        name: "Care Package".into(),
        image: cover,
        kind: "Drop".into(),
        items,
        rare: vec![],
        rare_image: None,
    }
}

fn find_case(st: &State, id: &str) -> R<CaseInfo> {
    if id == CARE_PACKAGE_ID {
        return Ok(care_package(st));
    }
    st.db
        .cases
        .iter()
        .find(|c| c.id == id)
        .cloned()
        .ok_or_else(|| bad("That case isn't available."))
}

fn cases_state(app: &App, st: &State, items: &[InventoryItem]) -> Value {
    // Newest first, like the game's store; weapon cases before the rest.
    let mut rows: Vec<(usize, &CaseInfo)> = st.db.cases.iter().enumerate().collect();
    rows.sort_by_key(|(i, c)| ((c.kind != "Case") as u8, std::cmp::Reverse(*i)));
    let mut rows: Vec<Value> = rows.iter().map(|(_, c)| {
        let wish = extras::wish_odds(st, c);
        json!({
            "id": c.id, "name": c.name, "image": c.image, "type": c.kind, "count": c.items.len() + c.rare.len(), "rare": !c.rare.is_empty(),
            "wish": wish.as_ref().map(|w| w.0.clone()), "wishChance": wish.map(|w| w.1),
        })
    }).collect();
    // The free Care Package drop, first, whenever the skin pool is loaded.
    if !st.db.skins.is_empty() {
        let cp = care_package(st);
        rows.insert(0, json!({ "id": cp.id, "name": cp.name, "image": cp.image, "type": cp.kind, "count": cp.items.len(), "rare": false }));
    }
    let value: f64 = items.iter().filter_map(|i| price_of(app, st, i)).sum();
    // What the openings would have cost for real: a key per weapon case, and the containers.
    let opens: Vec<(&CaseInfo, i32)> = st
        .settings
        .case_opens
        .iter()
        .filter_map(|(id, n)| Some((st.db.cases.iter().find(|c| &c.id == id)?, *n)))
        .collect();
    let keys: i32 = opens
        .iter()
        .filter(|o| o.0.kind == "Case")
        .map(|o| o.1)
        .sum();
    let cost: f64 = opens
        .iter()
        .map(|o| o.1 as f64 * app.prices.price(&o.0.name).unwrap_or(0.0))
        .sum();
    // Steam's fixed key price (not on the market); profit counts only what came out of a container.
    let key_price = match st.settings.currency.as_str() {
        "EUR" => 2.35,
        "GBP" => 2.05,
        _ => 2.49,
    };
    let spent = cost + keys as f64 * key_price;
    let unboxed: f64 = items
        .iter()
        .filter(|i| !i.case_name.is_empty())
        .filter_map(|i| price_of(app, st, i))
        .sum();
    let round = |x: f64| (x * 100.0).round() / 100.0 + 0.0; // + 0.0: an empty sum is -0.0
    json!({
        "dbEmpty": st.db.cases.is_empty(), "unlocked": st.settings.admin_unlocked, "wishlist": st.settings.wishlist, "opened": st.settings.cases_opened, "cases": rows,
        "inventoryCount": items.len(), "currency": st.settings.currency, "inventoryValue": round(value), "keysUsed": keys, "casesCost": round(cost),
        "spent": round(spent), "profit": round(unboxed - spent),
    })
}

/// The cheapest wear of a skin (what the game's case preview would make you think of), or the item's price.
fn cheapest_price(app: &App, name: &str, souvenir: bool) -> Option<f64> {
    if !name.contains('|')
        || name.starts_with("Sticker")
        || name.starts_with("Patch")
        || name.starts_with("Music Kit")
    {
        return app.prices.price(name);
    }
    [0.03, 0.1, 0.25, 0.4, 0.6]
        .iter()
        .filter_map(|w| {
            app.prices
                .price(&prices::market_name(name, true, true, *w, false, souvenir))
        })
        .min_by(f64::total_cmp)
}

fn contents(app: &App, st: &State, id: &str) -> R<Value> {
    let case = find_case(st, id)?;
    app.prices.ensure(&st.settings.currency);
    let mut odds = odds::odds(&case);
    odds.sort_by_key(|o| std::cmp::Reverse(if o.0 == "Rare" { 99 } else { odds::rank(&o.0) }));
    let groups: Vec<Value> = odds.iter().map(|(grade, chance)| {
        let rare = grade == "Rare";
        let items: Vec<&CaseItem> = if rare { case.rare.iter().collect() } else { case.items.iter().filter(|i| &i.rarity == grade).collect() };
        // The Care Package draws from every skin: show its odds, not a grid of a thousand cards.
        let items: Vec<&CaseItem> = if case.kind == "Drop" { vec![] } else { items };
        // Doppler phases are one finish in the list.
        let mut seen = HashSet::new();
        let shown: Vec<Value> = items.iter().filter(|i| seen.insert(i.name.clone()))
            .map(|i| json!({ "name": i.name, "image": i.image, "color": i.color, "phase": null,
                "price": if rare { None } else { cheapest_price(app, &i.name, case.kind.starts_with("Souvenir")) } }))
            .collect();
        let color = if rare { "#e4ae39".to_string() } else { items.first().map_or("#b0c3d9".into(), |i| i.color.clone()) };
        json!({ "grade": if rare { "Rare Special Item" } else { grade }, "color": color, "chance": chance, "items": shown,
            "image": if rare { case.rare_image.clone() } else { None } })
    }).collect();
    Ok(
        json!({ "id": case.id, "name": case.name, "image": case.image, "type": case.kind, "groups": groups, "currency": st.settings.currency }),
    )
}

fn make_item(st: &State, case: &CaseInfo, drop: &CaseItem, rng: &mut impl Rng) -> InventoryItem {
    let prefix = drop.id.split('-').next().unwrap_or("");
    let kind = match prefix {
        "skin" => "skin",
        "sticker" => "sticker",
        "music_kit" => "music",
        "collectible" => "pin",
        "patch" => "patch",
        "graffiti" => "graffiti",
        "agent" => "agent",
        "keychain" => "charm",
        other => other,
    };
    let (mut def, mut wear, mut seed, mut stat_trak) = (0u16, 0f32, 0, false);
    if kind == "skin" {
        if let Some(weapon) = st.db.weapon_of_item(&drop.name) {
            def = weapon;
            if drop.paint != 0 {
                let skin = st.db.find(def, drop.paint);
                wear = odds::roll_wear(
                    skin.map_or(0.0, |s| s.min_float),
                    skin.map_or(1.0, |s| s.max_float),
                    rng,
                );
            }
            seed = rng.gen_range(0..=1000);
            let gloves = drop.name.contains("Gloves") || drop.name.contains("Wraps");
            stat_trak = case.kind == "Case" && !gloves && rng.gen::<f64>() < 0.10;
        }
    }
    let collectible = if kind == "pin" || kind == "music" {
        id_number(Some(&drop.id))
    } else {
        0
    };
    if kind == "music" {
        stat_trak = drop.id.ends_with("_st");
    }
    InventoryItem {
        uid: new_uid(),
        kind: kind.into(),
        name: drop.name.replace("StatTrak™ ", ""),
        image: drop.image.clone(),
        grade: drop.rarity.clone(),
        color: drop.color.clone(),
        def,
        paint: drop.paint,
        wear,
        seed,
        stat_trak,
        stat_trak_count: 0,
        souvenir: case.kind.starts_with("Souvenir"),
        phase: drop.phase.clone(),
        case_name: case.name.clone(),
        collectible_id: collectible,
        opened: now(),
        item_id: Some(drop.id.clone()),
        team: None,
        stickers: None,
    }
}

fn after_equip(app: &App) {
    app.toast(
        if session::cs2_running() {
            "Equipped. It changes in game in about a second."
        } else {
            "Equipped. You'll have it next time you play."
        },
        "good",
    );
    app.changed();
}

/// Puts an unboxed item on: weapon skin, knife or gloves into the loadout; pin, music kit or agent too.
fn equip(app: &App, inv: &mut Inv, st: &State, uid: &str) -> R<()> {
    let item = find(inv.items(app)?, uid)?.clone();
    let mut store = require_plugin(st)?;
    let l = store.local();
    match item.kind.as_str() {
        "skin" if item.def != 0 => {
            if is_knife_type(item.def) {
                (l.knife, l.knife_t, l.knife_ct) = (item.def, 0, 0); // both sides
            }
            if is_glove_type(item.def) {
                (l.gloves, l.gloves_t, l.gloves_ct) = (item.def, 0, 0);
            }
            let skin = skin_of(&item);
            if skin.has_customization() || is_knife_type(item.def) {
                l.skins.insert(item.def, skin);
            }
            l.skins_t.remove(&item.def);
            l.skins_ct.remove(&item.def);
        }
        "pin" if item.collectible_id > 0 => l.pin = item.collectible_id as u16,
        "music" if item.collectible_id > 0 => l.music_kit = item.collectible_id as u16,
        "agent" if item.item_id.is_some() => {
            if item.team.as_deref() == Some("CT") {
                l.agent_ct = item.item_id.clone()
            } else {
                l.agent_t = item.item_id.clone()
            }
        }
        "sticker" | "patch" => {
            return Err(bad(
                "Stickers go on a gun: click the gun in your Inventory and press Stickers.",
            ))
        }
        _ => return Err(bad("This item can't be equipped.")),
    }
    store.save()?;
    after_equip(app);
    Ok(())
}

/// Takes an equipped item off: the gun goes back to default, or the knife / gloves / pin / music kit / agent.
fn unequip(app: &App, inv: &mut Inv, st: &State, uid: &str) -> R<()> {
    let item = find(inv.items(app)?, uid)?.clone();
    let mut store = require_plugin(st)?;
    let l = store.local();
    match item.kind.as_str() {
        "skin" => {
            for skins in [&mut l.skins, &mut l.skins_t, &mut l.skins_ct] {
                skins.remove(&item.def);
            }
            for slot in [
                &mut l.knife,
                &mut l.knife_t,
                &mut l.knife_ct,
                &mut l.gloves,
                &mut l.gloves_t,
                &mut l.gloves_ct,
            ] {
                if *slot == item.def {
                    *slot = 0;
                }
            }
        }
        "pin" => l.pin = 0,
        "music" => l.music_kit = 0,
        "agent" => {
            if l.agent_ct == item.item_id {
                l.agent_ct = None;
            }
            if l.agent_t == item.item_id {
                l.agent_t = None;
            }
        }
        _ => {}
    }
    store.save()?;
    app.toast("Unequipped.", "info");
    app.changed();
    Ok(())
}

fn last_removed() -> Vec<InventoryItem> {
    fs::read_to_string(removed_file())
        .ok()
        .and_then(|t| util::read_json(&t).ok())
        .unwrap_or_default()
}

// -------------------------------------------------------------------------------------------------
// Admin: put any item into the inventory (Inventory page › Add Item)
// -------------------------------------------------------------------------------------------------

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct CatalogItem {
    kind: String,
    key: String,
    name: String,
    image: String,
    grade: String,
    color: String,
    def: u16,
    paint: i32,
    min: f32,
    max: f32,
    can_stat_trak: bool,
    id: i32,
    team: Option<String>,
    model: Option<String>,
}

fn entry(
    kind: &str,
    key: String,
    name: String,
    image: &str,
    grade: &str,
    color: &str,
) -> CatalogItem {
    CatalogItem {
        kind: kind.into(),
        key,
        name,
        image: image.into(),
        grade: grade.into(),
        color: color.into(),
        def: 0,
        paint: 0,
        min: 0.0,
        max: 0.0,
        can_stat_trak: false,
        id: 0,
        team: None,
        model: None,
    }
}

fn star(def: u16) -> &'static str {
    if is_knife_type(def) || is_glove_type(def) {
        "★ "
    } else {
        ""
    }
}

fn skin_entry(s: &crate::itemdb::SkinInfo) -> CatalogItem {
    CatalogItem {
        def: s.def_index,
        paint: s.paint,
        min: s.min_float,
        max: s.max_float,
        can_stat_trak: s.can_stat_trak,
        ..entry(
            "skin",
            format!("skin-{}-{}", s.def_index, s.paint),
            format!("{}{}", star(s.def_index), s.full_name()),
            &s.image,
            &s.rarity,
            &s.rarity_color,
        )
    }
}

fn knife_entry(w: &crate::itemdb::WeaponInfo) -> CatalogItem {
    CatalogItem {
        def: w.def_index,
        can_stat_trak: true,
        ..entry(
            "skin",
            format!("skin-{}-0", w.def_index),
            format!("★ {}", w.name),
            &w.image,
            "Covert",
            "#eb4b4b",
        )
    }
}

fn catalog(st: &State, q: CatalogQuery) -> Vec<CatalogItem> {
    let db = &st.db;
    let words: Vec<String> = q
        .search
        .unwrap_or_default()
        .to_lowercase()
        .split_whitespace()
        .map(String::from)
        .collect();
    let hit = |name: &str| {
        let n = name.to_lowercase();
        words.iter().all(|w| n.contains(w.as_str()))
    };
    let mut list: Vec<CatalogItem> = match q.kind.as_deref() {
        Some("sticker") => {
            let mut s: Vec<_> = db.stickers.iter().filter(|s| hit(&s.name)).collect();
            s.sort_by_key(|s| std::cmp::Reverse(s.id));
            s.into_iter()
                .take(180)
                .map(|s| CatalogItem {
                    id: s.id,
                    ..entry(
                        "sticker",
                        format!("sticker-{}", s.id),
                        format!("Sticker | {}", s.name),
                        &s.image,
                        &s.rarity,
                        &s.rarity_color,
                    )
                })
                .collect()
        }
        Some("agent") => db
            .agents
            .iter()
            .filter(|a| hit(&a.name))
            .take(180)
            .map(|a| CatalogItem {
                team: Some(a.team.clone()),
                model: Some(a.model.clone()),
                ..entry(
                    "agent",
                    a.model.clone(),
                    a.name.clone(),
                    &a.image,
                    &a.rarity,
                    &a.rarity_color,
                )
            })
            .collect(),
        Some("pin") => db
            .pins
            .iter()
            .filter(|p| hit(&p.name))
            .take(180)
            .map(|p| CatalogItem {
                id: p.id,
                ..entry(
                    "pin",
                    format!("collectible-{}", p.id),
                    p.name.clone(),
                    &p.image,
                    &p.rarity,
                    &p.rarity_color,
                )
            })
            .collect(),
        Some("music") => db
            .music_kits
            .iter()
            .filter(|m| hit(&m.name))
            .take(180)
            .map(|m| CatalogItem {
                id: m.id,
                can_stat_trak: true,
                ..entry(
                    "music",
                    format!("music_kit-{}", m.id),
                    format!("Music Kit | {}", m.name),
                    &m.image,
                    &m.rarity,
                    &m.rarity_color,
                )
            })
            .collect(),
        _ => {
            let mut skins: Vec<_> = db.skins.iter().filter(|s| hit(&s.full_name())).collect();
            skins.sort_by(|a, b| {
                a.weapon
                    .cmp(&b.weapon)
                    .then_with(|| a.display_finish().cmp(&b.display_finish()))
            });
            // Vanilla knives too.
            skins
                .into_iter()
                .map(skin_entry)
                .chain(
                    db.weapons
                        .iter()
                        .filter(|w| w.category == "Knives" && hit(&w.name))
                        .map(knife_entry),
                )
                .take(180)
                .collect()
        }
    };
    list.truncate(180);
    list
}

/// One catalog entry by its key ("skin-7-44", "sticker-123", an agent model, "collectible-6110", "music_kit-32").
fn resolve_catalog(st: &State, kind: &str, key: &str) -> Option<CatalogItem> {
    let db = &st.db;
    let parts: Vec<&str> = key.split('-').collect();
    let num = |i: usize| {
        parts
            .get(i)
            .and_then(|p| p.parse::<i32>().ok())
            .unwrap_or(-1)
    };
    match kind {
        "skin" => {
            let d: u16 = parts.get(1).filter(|_| parts.len() == 3)?.parse().ok()?;
            let paint = num(2);
            if paint == 0 && is_knife_type(d) {
                if let Some(knife) = db.weapon(d) {
                    return Some(knife_entry(knife));
                }
            }
            db.find(d, paint).map(skin_entry)
        }
        "sticker" => db.sticker(num(1)).map(|s| CatalogItem {
            id: s.id,
            ..entry(
                "sticker",
                key.into(),
                format!("Sticker | {}", s.name),
                &s.image,
                &s.rarity,
                &s.rarity_color,
            )
        }),
        "agent" => db
            .agents
            .iter()
            .find(|a| a.model == key)
            .map(|a| CatalogItem {
                team: Some(a.team.clone()),
                model: Some(a.model.clone()),
                ..entry(
                    "agent",
                    key.into(),
                    a.name.clone(),
                    &a.image,
                    &a.rarity,
                    &a.rarity_color,
                )
            }),
        "pin" => db
            .pins
            .iter()
            .find(|p| p.id == num(1))
            .map(|p| CatalogItem {
                id: p.id,
                ..entry(
                    "pin",
                    key.into(),
                    p.name.clone(),
                    &p.image,
                    &p.rarity,
                    &p.rarity_color,
                )
            }),
        "music" => db
            .music_kits
            .iter()
            .find(|m| m.id == num(1))
            .map(|m| CatalogItem {
                id: m.id,
                can_stat_trak: true,
                ..entry(
                    "music",
                    key.into(),
                    format!("Music Kit | {}", m.name),
                    &m.image,
                    &m.rarity,
                    &m.rarity_color,
                )
            }),
        _ => None,
    }
}

// -------------------------------------------------------------------------------------------------
// Stickers on guns: applying uses up the sticker, scraping it all the way removes it. Up to 5.
// -------------------------------------------------------------------------------------------------

fn sticker_id(i: &InventoryItem) -> i32 {
    i.item_id
        .as_deref()
        .and_then(|id| id.split('-').nth(1))
        .and_then(|n| n.parse().ok())
        .unwrap_or(0)
}

fn gun_for(items: &[InventoryItem], uid: &str) -> R<InventoryItem> {
    let gun = items
        .iter()
        .find(|i| i.uid == uid)
        .ok_or_else(|| bad("That gun isn't in your inventory any more."))?;
    if gun.kind != "skin" || gun.def == 0 || is_glove_type(gun.def) || is_knife_type(gun.def) {
        return Err(bad("Stickers only go on guns."));
    }
    Ok(gun.clone())
}

/// Saves the changed gun; when it is equipped, the game gets the new stickers too.
fn replace_gun(
    app: &App,
    inv: &mut Inv,
    st: &State,
    old: &InventoryItem,
    updated: InventoryItem,
) -> R<()> {
    let items = inv.items(app)?;
    if let Some(i) = items.iter().position(|i| i.uid == old.uid) {
        items[i] = updated.clone();
    }
    inv.save(app);
    if let Some(g) = st.game.as_ref().filter(|g| g.plugin_installed()) {
        let mut store = LoadoutStore::open(&g.loadout_file());
        let l = store.local();
        // The gun may be on the shared slot and/or one side: update each place it is worn.
        let mut worn = false;
        for skins in [&mut l.skins, &mut l.skins_t, &mut l.skins_ct] {
            if matches(skins.get(&old.def), old) {
                skins.insert(updated.def, skin_of(&updated));
                worn = true;
            }
        }
        if worn {
            store.save()?;
        }
    }
    app.changed();
    Ok(())
}

// -------------------------------------------------------------------------------------------------
// Backups ("backup.", not "cases."): the file dialog must not hold the inventory gate, so it is taken after.
// -------------------------------------------------------------------------------------------------

pub fn backup(app: &Arc<App>, name: &str) -> R<Value> {
    match name {
        "backup.exportInventory" => {
            let default = format!("inventory-{}.json", chrono::Local::now().format("%Y-%m-%d"));
            let Some(file) = app.pick(move |d| {
                d.set_title("Save your inventory")
                    .add_filter("Inventory", &["json"])
                    .set_file_name(default)
                    .save_file()
            }) else {
                return ok(());
            };
            let mut inv = app.inv();
            let text = serde_json::to_string(inv.items(app)?).unwrap();
            fs::write(&file, text).map_err(|e| bad(format!("Could not save: {e}")))?;
            app.toast(
                format!(
                    "Inventory saved to {}.",
                    file.file_name().unwrap_or_default().to_string_lossy()
                ),
                "good",
            );
            ok(())
        }
        "backup.importInventory" => {
            // A hand-edited file could hold any item, which would get around "unbox only".
            require_admin(&app.state(), "Importing")?;
            let Some(file) = app.pick(|d| {
                d.set_title("Add a saved inventory")
                    .add_filter("Inventory", &["json"])
                    .pick_file()
            }) else {
                return ok(());
            };
            let items: Vec<InventoryItem> = fs::read_to_string(&file)
                .map_err(|e| e.to_string())
                .and_then(|t| util::read_json::<Vec<InventoryItem>>(&t).map_err(|e| e.to_string()))
                .map_err(|e| bad(format!("That isn't a saved inventory: {e}")))?
                .into_iter()
                .filter(|i| !i.uid.is_empty() && !i.kind.is_empty() && !i.name.is_empty())
                .collect();
            let mut inv = app.inv();
            let have = inv.items(app)?;
            let mut uids: HashSet<String> = have.iter().map(|i| i.uid.clone()).collect();
            let total = items.len();
            let add: Vec<InventoryItem> = items
                .into_iter()
                .filter(|i| uids.insert(i.uid.clone()))
                .collect();
            inv.ensure_room(app, add.len())?;
            let n = add.len();
            inv.items(app)?.splice(0..0, add);
            inv.save(app);
            drop(inv);
            app.toast(
                format!(
                    "{n} item{} added ({} already here).",
                    if n == 1 { "" } else { "s" },
                    total - n
                ),
                "good",
            );
            app.changed();
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::itemdb::SkinInfo;

    #[test]
    fn care_package_pool_is_weapon_skins_only() {
        let skin =
            |weapon: &str, finish: &str, paint: i32, category: &str, rarity: &str| SkinInfo {
                def_index: 9,
                paint,
                weapon: weapon.into(),
                finish: finish.into(),
                category: category.into(),
                rarity: rarity.into(),
                ..Default::default()
            };
        let skins = vec![
            skin("AWP", "Medusa", 446, "Rifles", "Covert"),
            skin("AK-47", "Redline", 282, "Rifles", "Classified"),
            skin("Karambit", "Fade", 38, "Knives", "Covert"), // knife: out
            skin("★", "Gloves", 10006, "Gloves", "Extraordinary"), // gloves: out
            skin("AWP", "", 0, "Rifles", "Covert"),           // vanilla: out
        ];
        let items = care_package_items(&skins);
        let names: Vec<&str> = items.iter().map(|i| i.name.as_str()).collect();
        assert_eq!(names, ["AWP | Medusa", "AK-47 | Redline"]);
        assert_eq!(items[0].id, "skin-446");
    }

    fn gun(def: u16, paint: i32) -> InventoryItem {
        InventoryItem {
            kind: "skin".into(),
            def,
            paint,
            wear: 0.2,
            ..Default::default()
        }
    }

    #[test]
    fn unbox_only_keeps_what_you_own() {
        let mut l = PlayerLoadout {
            knife: 507,
            gloves: 5030,
            agent_t: Some("x".into()),
            ..Default::default()
        };
        l.skins.insert(
            7,
            SkinSettings {
                paint: 282,
                ..Default::default()
            },
        );
        l.skins.insert(
            9,
            SkinSettings {
                paint: 344,
                ..Default::default()
            },
        );
        l.skins.insert(4, SkinSettings::default()); // vanilla gun: always fine
        let items = vec![gun(7, 282), gun(507, 0)];
        assert!(prune_unowned(&mut l, &items));
        assert_eq!(l.skins.keys().copied().collect::<Vec<_>>(), [4, 7]);
        assert_eq!((l.knife, l.gloves, l.agent_t.is_none()), (507, 0, true));
        assert!(!prune_unowned(&mut l, &items));
    }

    #[test]
    fn inventory_file_format() {
        let json = serde_json::to_string(&InventoryItem {
            stat_trak: true,
            stat_trak_count: 0,
            item_id: Some("skin-x".into()),
            ..gun(7, 282)
        })
        .unwrap();
        assert!(
            json.contains("\"StatTrak\":true")
                && json.contains("\"ItemId\":\"skin-x\"")
                && json.contains("\"CollectibleId\":0")
        );
        assert_eq!(id_number(Some("music_kit-3_st")), 3);
    }
}
