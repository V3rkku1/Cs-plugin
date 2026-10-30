//! More to do with cases and the inventory: a wishlist (which case has the skins you want, and the odds), the
//! collection book (how much of each case you own), a random loadout, case battles against bots, the upgrade
//! game (risk a skin for a better one), gift codes to give a skin to a friend, and StatTrak™ counts brought back
//! from the server.

use super::super::{arg, bad, ok, App, State, R};
use super::{
    cheapest_price, find, find_case, make_item, new_uid, now, price_of, put_on, read_loadout,
    require_plugin, resolve_catalog, skin_entry, view, Inv, InventoryItem,
};
use crate::itemdb::{CaseInfo, CaseItem};
use crate::items::{is_glove_type, is_knife_type};
use crate::odds;
use crate::util;
use base64::Engine;
use rand::seq::SliceRandom;
use rand::Rng;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::collections::{HashMap, HashSet};
use std::sync::Arc;

/// The calls this file answers, or None to let cases.rs answer.
pub fn call(app: &Arc<App>, inv: &mut Inv, name: &str, a: &Value) -> Option<R<Value>> {
    Some(match name {
        "cases.wish" => wish(app, arg(a).ok()?),
        "cases.collections" => collections(app, inv),
        "inventory.random" => random_loadout(app, inv),
        "cases.battle" => battle(app, inv, arg(a).ok()?),
        "cases.upgradeTargets" => upgrade_targets(app, inv, &arg::<String>(a).ok()?),
        "cases.upgrade" => upgrade(app, inv, arg(a).ok()?),
        "inventory.gift" => gift(app, inv, &arg::<String>(a).ok()?),
        "inventory.redeem" => redeem(app, inv, &arg::<String>(a).ok()?),
        _ => return None,
    })
}

// -------------------------------------------------------------------------------------------------
// Wishlist
// -------------------------------------------------------------------------------------------------

/// Each item of a case with its own chance (its grade's chance shared by the grade's items).
pub fn item_chances(case: &CaseInfo) -> Vec<(&CaseItem, f64)> {
    let odds = odds::odds(case);
    let chance = |grade: &str| odds.iter().find(|o| o.0 == grade).map_or(0.0, |o| o.1);
    let mut list: Vec<(&CaseItem, f64)> = vec![];
    for item in &case.items {
        let n = case
            .items
            .iter()
            .filter(|i| i.rarity == item.rarity)
            .count()
            .max(1);
        list.push((item, chance(&item.rarity) / n as f64));
    }
    for item in &case.rare {
        list.push((item, chance("Rare") / case.rare.len().max(1) as f64));
    }
    list
}

/// The wished items a case can give and the chance of getting any of them in one opening.
pub fn wish_odds(st: &State, case: &CaseInfo) -> Option<(Vec<String>, f64)> {
    if st.settings.wishlist.is_empty() {
        return None;
    }
    let mut names = vec![];
    let mut chance = 0.0;
    for (item, c) in item_chances(case) {
        let name = item.name.replace("StatTrak™ ", "");
        if st.settings.wishlist.contains(&name) {
            chance += c;
            if !names.contains(&name) {
                names.push(name);
            }
        }
    }
    (!names.is_empty()).then_some((names, chance))
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct WishArgs {
    name: String,
    on: bool,
}

fn wish(app: &App, a: WishArgs) -> R<Value> {
    let mut st = app.state();
    let list = &mut st.settings.wishlist;
    list.retain(|n| *n != a.name);
    if a.on && !a.name.is_empty() {
        list.push(a.name);
    }
    app.save(&st);
    drop(st);
    app.changed();
    ok(())
}

// -------------------------------------------------------------------------------------------------
// Collection book
// -------------------------------------------------------------------------------------------------

fn collections(app: &App, inv: &mut Inv) -> R<Value> {
    let st = app.state();
    let owned: HashSet<&str> = inv.items(app)?.iter().map(|i| i.name.as_str()).collect();
    let books: Vec<Value> = st
        .db
        .cases
        .iter()
        .filter(|c| c.kind == "Case" || c.kind.starts_with("Souvenir"))
        .rev()
        .map(|c| {
            let mut seen = HashSet::new();
            let items: Vec<Value> = c
                .items
                .iter()
                .filter(|i| seen.insert(i.name.clone()))
                .map(|i| json!({ "name": i.name, "image": i.image, "color": i.color, "owned": owned.contains(i.name.as_str()),
                    "wished": st.settings.wishlist.contains(&i.name) }))
                .collect();
            let have = items.iter().filter(|i| i["owned"] == true).count();
            json!({ "id": c.id, "name": c.name, "image": c.image, "owned": have, "total": items.len(), "items": items })
        })
        .collect();
    ok(books)
}

// -------------------------------------------------------------------------------------------------
// Random loadout
// -------------------------------------------------------------------------------------------------

/// A random owned skin on every gun you have one for, plus a random knife and gloves. One write, one toast.
fn random_loadout(app: &App, inv: &mut Inv) -> R<Value> {
    let st = app.state();
    let mut by_def: HashMap<u16, Vec<InventoryItem>> = HashMap::new();
    for i in inv
        .items(app)?
        .iter()
        .filter(|i| i.kind == "skin" && i.def != 0)
    {
        by_def.entry(i.def).or_default().push(i.clone());
    }
    if by_def.is_empty() {
        return Err(bad(
            "Open some cases first: you have no skins to pick from.",
        ));
    }
    let mut rng = rand::thread_rng();
    let mut knives: Vec<&InventoryItem> = vec![];
    let mut gloves: Vec<&InventoryItem> = vec![];
    let mut store = require_plugin(&st)?;
    let l = store.local();
    let mut picked = 0;
    for (def, items) in &by_def {
        if is_knife_type(*def) {
            knives.extend(items);
        } else if is_glove_type(*def) {
            gloves.extend(items);
        } else if let Some(item) = items.choose(&mut rng) {
            put_on(l, item)?;
            picked += 1;
        }
    }
    // One knife and one pair of gloves of all you own, not one of each type.
    for pool in [&knives, &gloves] {
        if let Some(item) = pool.choose(&mut rng) {
            put_on(l, item)?;
            picked += 1;
        }
    }
    store.save()?;
    drop(st);
    app.toast(format!("Random loadout: {picked} items equipped."), "good");
    app.changed();
    ok(())
}

// -------------------------------------------------------------------------------------------------
// Case battle
// -------------------------------------------------------------------------------------------------

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct BattleArgs {
    id: String,
    rounds: i32,
    bots: i32,
}

/// What an item counts for in a battle: its market price, or a little by grade when there's no price.
fn worth(app: &App, st: &State, i: &InventoryItem) -> f64 {
    price_of(app, st, i).unwrap_or_else(|| odds::rank(&i.grade) as f64 * 0.05)
}

fn small(app: &App, st: &State, i: &InventoryItem) -> Value {
    json!({ "name": i.name, "image": i.image, "color": i.color, "statTrak": i.stat_trak, "price": worth(app, st, i) })
}

/// You and 1-3 bots open the same case 1-5 times; the highest total takes every item. A draw at the top keeps
/// only your own drops.
fn battle(app: &App, inv: &mut Inv, a: BattleArgs) -> R<Value> {
    let mut st = app.state();
    let case = find_case(&st, &a.id)?;
    let rounds = a.rounds.clamp(1, 5) as usize;
    let bots = a.bots.clamp(1, 3) as usize;
    inv.ensure_room(app, rounds * (bots + 1))?;
    app.prices.ensure(&st.settings.currency);
    let mut rng = rand::thread_rng();
    const BOT_NAMES: [&str; 3] = ["Bot Ace", "Bot Viper", "Bot Nova"];
    let mut players: Vec<(String, Vec<InventoryItem>, f64)> = (0..=bots)
        .map(|p| {
            let drops: Vec<InventoryItem> = (0..rounds)
                .map(|_| {
                    let (d, _) = odds::roll(&case, &mut rng);
                    make_item(&st, &case, &d, &mut rng)
                })
                .collect();
            let total = drops.iter().map(|i| worth(app, &st, i)).sum();
            (
                if p == 0 {
                    "You".to_string()
                } else {
                    BOT_NAMES[p - 1].to_string()
                },
                drops,
                total,
            )
        })
        .collect();
    let best = players.iter().map(|p| p.2).fold(f64::MIN, f64::max);
    let leaders = players.iter().filter(|p| p.2 == best).count();
    let won = players[0].2 == best;
    let won_all = won && leaders == 1;
    let mut gained: Vec<InventoryItem> = vec![];
    if won_all {
        for p in &mut players {
            gained.extend(p.1.iter().cloned());
        }
    } else if won {
        gained = players[0].1.clone();
    }
    let view_players: Vec<Value> = players
        .iter()
        .map(|p| json!({ "name": p.0, "total": (p.2 * 100.0).round() / 100.0, "drops": p.1.iter().map(|i| small(app, &st, i)).collect::<Vec<_>>() }))
        .collect();
    inv.items(app)?.splice(0..0, gained.iter().cloned());
    inv.save(app);
    st.settings.cases_opened += rounds as i32;
    *st.settings.case_opens.entry(case.id.clone()).or_default() += rounds as i32;
    app.save(&st);
    let currency = st.settings.currency.clone();
    drop(st);
    app.changed();
    ok(
        json!({ "players": view_players, "won": won, "wonAll": won_all, "gained": gained.len(), "currency": currency }),
    )
}

// -------------------------------------------------------------------------------------------------
// Upgrade game
// -------------------------------------------------------------------------------------------------

/// The house keeps 10%: a skin worth half as much as the target has a 45% chance.
const UPGRADE_EDGE: f64 = 0.9;
const UPGRADE_MAX_CHANCE: f64 = 0.8;

fn upgrade_chance(input: f64, target: f64) -> f64 {
    (input / target * UPGRADE_EDGE).clamp(0.0, UPGRADE_MAX_CHANCE)
}

/// Skins worth 1.2 to 20 times the one you risk, cheapest first, with your chance for each.
fn upgrade_targets(app: &App, inv: &mut Inv, uid: &str) -> R<Value> {
    let st = app.state();
    let item = find(inv.items(app)?, uid)?.clone();
    if item.kind != "skin" {
        return Err(bad("Only weapon skins, knives and gloves can be upgraded."));
    }
    app.prices.ensure(&st.settings.currency);
    let value = price_of(app, &st, &item)
        .ok_or_else(|| bad("This skin has no market price yet, so it can't be upgraded."))?;
    let mut seen = HashSet::new();
    let mut list: Vec<(f64, Value)> = st
        .db
        .skins
        .iter()
        .filter(|s| s.paint != 0 && seen.insert(s.full_name()))
        .filter_map(|s| {
            // The catalog name has the ★ knives and gloves need for their market price.
            let e = skin_entry(s);
            let price = cheapest_price(app, &e.name, false)?;
            (price >= value * 1.2 && price <= value * 20.0).then(|| {
                (price, json!({ "key": e.key, "name": e.name, "image": e.image, "color": e.color, "price": price,
                    "chance": upgrade_chance(value, price) }))
            })
        })
        .collect();
    list.sort_by(|a, b| a.0.total_cmp(&b.0));
    // Spread over the whole price range, not 60 nearly equal cheap ones.
    let step = (list.len() / 60).max(1);
    let targets: Vec<Value> = list
        .into_iter()
        .step_by(step)
        .take(60)
        .map(|t| t.1)
        .collect();
    ok(json!({ "value": value, "currency": st.settings.currency, "targets": targets }))
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct UpgradeArgs {
    uid: String,
    key: String,
}

fn upgrade(app: &App, inv: &mut Inv, a: UpgradeArgs) -> R<Value> {
    let st = app.state();
    let item = find(inv.items(app)?, &a.uid)?.clone();
    let loadout = read_loadout(app, inv, &st)?;
    if view(app, &st, &item, &loadout).equipped {
        return Err(bad("Unequip it first: an equipped skin can't be risked."));
    }
    let value =
        price_of(app, &st, &item).ok_or_else(|| bad("This skin has no market price yet."))?;
    let target = resolve_catalog(&st, "skin", &a.key).ok_or_else(|| bad("Unknown target skin."))?;
    let price = cheapest_price(app, &target.name, false)
        .ok_or_else(|| bad("The target has no market price."))?;
    let chance = upgrade_chance(value, price);
    let mut rng = rand::thread_rng();
    let roll: f64 = rng.gen();
    let won = roll < chance;
    let items = inv.items(app)?;
    items.retain(|i| i.uid != item.uid);
    let mut new_item = None;
    if won {
        let got = InventoryItem {
            uid: new_uid(),
            kind: "skin".into(),
            name: target.name.clone(),
            image: target.image.clone(),
            grade: target.grade.clone(),
            color: target.color.clone(),
            def: target.def,
            paint: target.paint,
            wear: odds::roll_wear(target.min, target.max, &mut rng),
            seed: rng.gen_range(0..=1000),
            case_name: "Upgrade".into(),
            opened: now(),
            item_id: Some(target.key.clone()),
            ..Default::default()
        };
        items.insert(0, got.clone());
        new_item = Some(view(app, &st, &got, &loadout));
    }
    inv.save(app);
    drop(st);
    app.changed();
    ok(json!({ "won": won, "chance": chance, "roll": roll, "item": new_item }))
}

// -------------------------------------------------------------------------------------------------
// Gift codes: give a skin to a friend (the code carries the item; it leaves your inventory).
// -------------------------------------------------------------------------------------------------

// shortcut: the key ships in every launcher, so a determined person could forge a code; fine for friends on a LAN.
const GIFT_KEY: &[u8] = b"lst-gift-v1:9f3c2a7e51d84b06";
const GIFT_PREFIX: &str = "LSTG1-";

fn hmac(message: &[u8]) -> String {
    let mut key = [0u8; 64];
    key[..GIFT_KEY.len()].copy_from_slice(GIFT_KEY);
    let pad = |b: u8| key.iter().map(|k| k ^ b).collect::<Vec<u8>>();
    let inner = Sha256::new()
        .chain_update(pad(0x36))
        .chain_update(message)
        .finalize();
    let outer = Sha256::new()
        .chain_update(pad(0x5c))
        .chain_update(inner)
        .finalize();
    util::hex(&outer[..12]).to_lowercase()
}

#[derive(Serialize, Deserialize)]
struct Gift {
    /// One code, one redeem (per PC).
    nonce: String,
    item: InventoryItem,
}

fn encode_gift(item: &InventoryItem) -> String {
    let mut item = item.clone();
    item.uid = String::new();
    let body = serde_json::to_vec(&Gift {
        nonce: util::random_hex(8),
        item,
    })
    .unwrap();
    let body = base64::engine::general_purpose::URL_SAFE_NO_PAD.encode(body);
    format!("{GIFT_PREFIX}{body}.{}", hmac(body.as_bytes()))
}

fn decode_gift(code: &str) -> Option<Gift> {
    let code: String = code.chars().filter(|c| !c.is_whitespace()).collect();
    let (body, sig) = code.strip_prefix(GIFT_PREFIX)?.rsplit_once('.')?;
    if hmac(body.as_bytes()) != sig {
        return None;
    }
    let bytes = base64::engine::general_purpose::URL_SAFE_NO_PAD
        .decode(body)
        .ok()?;
    serde_json::from_slice(&bytes).ok()
}

/// Takes the item out of your inventory and gives back a code (also copied) for your friend to redeem.
fn gift(app: &App, inv: &mut Inv, uid: &str) -> R<Value> {
    let st = app.state();
    let item = find(inv.items(app)?, uid)?.clone();
    let loadout = read_loadout(app, inv, &st)?;
    if view(app, &st, &item, &loadout).equipped {
        return Err(bad("Unequip it first, then gift it."));
    }
    let code = encode_gift(&item);
    inv.items(app)?.retain(|i| i.uid != item.uid);
    inv.save(app);
    let copied = app.copy(&code);
    drop(st);
    app.log(format!("Gift code for {}: {code}", item.name));
    app.toast(
        if copied {
            "Gift code copied: send it to your friend."
        } else {
            "Gift code ready: copy it from the box."
        },
        "good",
    );
    app.changed();
    ok(json!({ "code": code, "name": item.name }))
}

fn redeem(app: &App, inv: &mut Inv, code: &str) -> R<Value> {
    let gift = decode_gift(code)
        .ok_or_else(|| bad("That isn't a valid gift code (it starts with LSTG1-)."))?;
    let mut st = app.state();
    if st.settings.redeemed_gifts.contains(&gift.nonce) {
        return Err(bad("This gift code was already redeemed on this PC."));
    }
    inv.ensure_room(app, 1)?;
    let mut item = gift.item;
    item.uid = new_uid();
    item.opened = now();
    let name = item.name.clone();
    inv.items(app)?.insert(0, item);
    inv.save(app);
    st.settings.redeemed_gifts.push(gift.nonce);
    let excess = st.settings.redeemed_gifts.len().saturating_sub(1000);
    st.settings.redeemed_gifts.drain(..excess);
    app.save(&st);
    drop(st);
    app.toast(format!("Gift received: {name}"), "good");
    app.changed();
    ok(json!({ "name": name }))
}

// -------------------------------------------------------------------------------------------------
// StatTrak™: the plugin counts kills in the loadout; the inventory item keeps the highest count.
// -------------------------------------------------------------------------------------------------

/// Copies StatTrak™ kill counts from the loadout to the items they belong to. Returns whether any changed.
pub fn sync_stat_trak(items: &mut [InventoryItem], loadout: &crate::items::PlayerLoadout) -> bool {
    let mut changed = false;
    for i in items.iter_mut().filter(|i| i.stat_trak && i.kind == "skin") {
        for skins in [&loadout.skins, &loadout.skins_t, &loadout.skins_ct] {
            if let Some(s) = skins.get(&i.def).filter(|s| super::matches(Some(s), i)) {
                if s.stat_trak_count > i.stat_trak_count {
                    i.stat_trak_count = s.stat_trak_count;
                    changed = true;
                }
            }
        }
    }
    changed
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn gift_codes_round_trip_and_reject_changes() {
        let item = InventoryItem {
            name: "AK-47 | Redline".into(),
            kind: "skin".into(),
            def: 7,
            paint: 282,
            wear: 0.2,
            ..Default::default()
        };
        let code = encode_gift(&item);
        let back = decode_gift(&format!(" {code}\n")).expect("valid code");
        assert_eq!(
            (back.item.name.as_str(), back.item.def, back.item.paint),
            ("AK-47 | Redline", 7, 282)
        );
        // Any change to the body breaks the signature.
        let (body, sig) = code.rsplit_once('.').unwrap();
        let tampered = format!("{}x.{sig}", body);
        assert!(decode_gift(&tampered).is_none());
        assert!(decode_gift("LSTG1-abc.def").is_none());
    }

    #[test]
    fn upgrade_chance_is_capped_and_keeps_the_edge() {
        assert!((upgrade_chance(5.0, 10.0) - 0.45).abs() < 1e-9);
        assert_eq!(upgrade_chance(9.9, 10.0), UPGRADE_MAX_CHANCE);
    }

    #[test]
    fn stat_trak_counts_come_back_from_the_loadout() {
        let mut items = vec![InventoryItem {
            kind: "skin".into(),
            stat_trak: true,
            def: 7,
            paint: 282,
            seed: 5,
            wear: 0.2,
            ..Default::default()
        }];
        let mut l = crate::items::PlayerLoadout::default();
        l.skins.insert(
            7,
            crate::items::SkinSettings {
                paint: 282,
                seed: 5,
                wear: 0.2,
                stat_trak: true,
                stat_trak_count: 12,
                ..Default::default()
            },
        );
        assert!(sync_stat_trak(&mut items, &l));
        assert_eq!(items[0].stat_trak_count, 12);
        assert!(!sync_stat_trak(&mut items, &l));
    }
}
