//! Trade Up Contracts, with CS2's rules: 10 guns of one grade (Consumer to Classified), all StatTrak or all
//! normal, no souvenirs, for one item of the next grade; or 5 Covert guns for a knife or gloves of their cases.
//! Odds by the ticket rule and the float by CS2's current formula (see `odds`). A collection here is a
//! container's contents, which is what the item data has.

use super::cases::{new_uid, price_of, read_loadout, view, InventoryItem, Inv};
use super::{arg, bad, ok, unknown, App, State, R};
use crate::itemdb::{CaseInfo, CaseItem, ItemDatabase};
use crate::items::{is_glove_type, is_knife_type};
use crate::{odds, prices};
use rand::Rng;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::sync::Arc;

const LADDER: [&str; 6] = ["Consumer Grade", "Industrial Grade", "Mil-Spec Grade", "Restricted", "Classified", "Covert"];

/// The container (collection) each skin comes from, rebuilt when the item lists were updated.
#[derive(Default)]
pub struct Collections {
    db: Option<std::sync::Weak<ItemDatabase>>,
    map: HashMap<(u16, i32), usize>,
}

#[derive(Serialize, Clone)]
#[serde(rename_all = "camelCase")]
struct Outcome {
    name: String,
    image: String,
    color: String,
    grade: String,
    chance: f64,
    float: f32,
    wear: String,
    price: Option<f64>,
    collection: String,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Check {
    ok: bool,
    message: String,
    needed: usize,
    grade: Option<String>,
    stat_trak: Option<bool>,
    outcomes: Vec<Outcome>,
    input_value: Option<f64>,
    expected_value: Option<f64>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct TradeArgs {
    uids: Vec<String>,
}

/// One winning ticket: the item, its case (index into the item data's cases), its chance, and whether it's a rare.
type Ticket = (CaseItem, usize, f64, bool);

/// A weapon case first, else a souvenir package.
fn collection_of(st: &mut State, def: u16, paint: i32) -> Option<usize> {
    let fresh = st.collections.db.as_ref().and_then(|w| w.upgrade()).is_some_and(|db| Arc::ptr_eq(&db, &st.db));
    if !fresh {
        let db = st.db.clone();
        let mut map = HashMap::new();
        let order = db.cases.iter().enumerate().filter(|(_, c)| c.kind == "Case").chain(db.cases.iter().enumerate().filter(|(_, c)| c.kind.starts_with("Souvenir")));
        for (i, case) in order {
            for item in &case.items {
                if let Some(d) = db.weapon_of_item(&item.name) {
                    map.entry((d, item.paint)).or_insert(i);
                }
            }
        }
        st.collections = Collections { db: Some(Arc::downgrade(&db)), map };
    }
    st.collections.map.get(&(def, paint)).copied()
}

fn plan(app: &App, st: &mut State, inputs: &[InventoryItem]) -> (Check, Vec<Ticket>) {
    let first = inputs.first();
    let fail = |msg: String, needed: usize| (Check {
        ok: false, message: msg, needed, grade: first.map(|i| i.grade.clone()), stat_trak: first.map(|i| i.stat_trak), outcomes: vec![], input_value: None, expected_value: None,
    }, vec![]);
    let Some(first) = first else { return fail("Add 10 guns of the same grade (or 5 Covert guns for a knife or gloves).".into(), 10) };
    let grade = first.grade.clone();
    let covert = grade == "Covert";
    let needed = if covert { 5 } else { 10 };
    if inputs.iter().any(|i| i.kind != "skin" || i.def == 0 || i.paint == 0 || is_knife_type(i.def) || is_glove_type(i.def)) {
        return fail("Only guns with a finish can go in a contract (no knives, gloves, stickers or vanilla items).".into(), needed);
    }
    if inputs.iter().any(|i| i.souvenir) {
        return fail("Souvenir items can't be traded up.".into(), needed);
    }
    if inputs.iter().any(|i| i.grade != grade) {
        return fail("Every item must be the same grade.".into(), needed);
    }
    if inputs.iter().any(|i| i.stat_trak != first.stat_trak) {
        return fail("Use all StatTrak™ items or none.".into(), needed);
    }
    let Some(rank) = LADDER.iter().position(|g| *g == grade) else { return fail(format!("{grade} items can't be traded up."), needed) };
    // CS2's tickets: every input adds one ticket for each next-grade item in its own collection (for Coverts, its
    // case's knives / gloves), and one ticket wins.
    let next = if covert { "Rare" } else { LADDER[rank + 1] };
    let mut per_input: Vec<Vec<(CaseItem, usize)>> = vec![];
    for input in inputs {
        let Some(case) = collection_of(st, input.def, input.paint) else { return fail(format!("{}: its collection isn't known.", input.name), needed) };
        let c = &st.db.cases[case];
        let outs: Vec<CaseItem> = if covert {
            let mut seen = std::collections::HashSet::new();
            c.rare.iter().filter(|r| seen.insert(r.name.clone())).cloned().collect()
        } else {
            c.items.iter().filter(|x| x.rarity == next).cloned().collect()
        };
        if outs.is_empty() {
            return fail(if covert { format!("{}: its collection has no knives or gloves.", input.name) } else { format!("{}: its collection has no {next} item, so it can't go higher.", input.name) }, needed);
        }
        per_input.push(outs.into_iter().map(|o| (o, case)).collect());
    }
    let pool: Vec<Ticket> = odds::tickets(&per_input).into_iter().map(|((item, case), chance)| (item, case, chance, covert)).collect();
    let outcomes = outcomes(app, st, &pool, inputs);
    let prices: Vec<Option<f64>> = inputs.iter().map(|i| price_of(app, st, i)).collect();
    let input_value = prices.iter().all(Option::is_some).then(|| prices.iter().flatten().sum::<f64>());
    let expected = outcomes.iter().all(|o| o.price.is_some()).then(|| outcomes.iter().map(|o| o.chance * o.price.unwrap()).sum::<f64>());
    let ok = inputs.len() == needed;
    let round = |x: f64| (x * 100.0).round() / 100.0 + 0.0; // + 0.0: an empty sum is -0.0
    (Check {
        ok, message: if ok { "Ready to sign.".into() } else { format!("{}/{needed} items.", inputs.len()) }, needed, grade: Some(grade),
        stat_trak: Some(first.stat_trak), outcomes, input_value: input_value.map(round), expected_value: expected.map(round),
    }, pool)
}

/// CS2's float: the average of where each input sits in its own range, put on the new skin's range.
fn trade_float(st: &State, inputs: &[InventoryItem], def: u16, paint: i32) -> f32 {
    let ranges: Vec<(f32, f32, f32)> = inputs.iter().map(|i| st.db.find(i.def, i.paint).map_or((i.wear, 0.0, 1.0), |s| (i.wear, s.min_float, s.max_float))).collect();
    let target = st.db.find(def, paint);
    odds::trade_float(&ranges, target.map_or(0.0, |s| s.min_float), target.map_or(1.0, |s| s.max_float))
}

fn outcomes(app: &App, st: &State, pool: &[Ticket], inputs: &[InventoryItem]) -> Vec<Outcome> {
    let stat_trak = inputs.first().is_some_and(|i| i.stat_trak);
    let mut list: Vec<Outcome> = vec![];
    for (item, case, chance, _) in pool {
        if let Some(o) = list.iter_mut().find(|o| o.name == item.name) {
            o.chance += chance;
            continue;
        }
        let def = st.db.weapon_of_item(&item.name).unwrap_or(0);
        let wear = if item.paint != 0 { trade_float(st, inputs, def, item.paint) } else { 0.0 };
        let name = prices::market_name(&item.name, true, item.paint != 0, wear, stat_trak, false);
        list.push(Outcome {
            name: item.name.clone(), image: item.image.clone(), color: item.color.clone(), grade: item.rarity.clone(), chance: *chance, float: wear,
            wear: if item.paint != 0 { prices::wear_name(wear).into() } else { String::new() }, price: app.prices.price(&name), collection: st.db.cases[*case].name.clone(),
        });
    }
    list.sort_by(|a, b| b.chance.total_cmp(&a.chance).then_with(|| a.name.cmp(&b.name)));
    list
}

fn inputs(app: &App, inv: &mut Inv, uids: &[String]) -> R<Vec<InventoryItem>> {
    let items = inv.items(app)?;
    let mut seen = std::collections::HashSet::new();
    Ok(uids.iter().filter(|u| seen.insert(*u)).filter_map(|u| items.iter().find(|i| &i.uid == u).cloned()).collect())
}

pub fn call(app: &Arc<App>, inv: &mut Inv, name: &str, a: &Value) -> R<Value> {
    let a: TradeArgs = arg(a)?;
    let inputs = inputs(app, inv, &a.uids)?;
    let mut st = app.state();
    app.prices.ensure(&st.settings.currency);
    let (check, pool) = plan(app, &mut st, &inputs);
    match name {
        "tradeup.check" => ok(check),
        "tradeup.sign" => {
            if !check.ok {
                return Err(bad(check.message));
            }
            let mut rng = rand::thread_rng();
            let pick = odds::pick(&pool, |t| t.2, rng.gen::<f64>()).clone();
            // Knives: a Doppler finish rolls its phase like a case does.
            let drop = if pick.3 {
                let case = &st.db.cases[pick.1];
                let only = CaseInfo { items: vec![], rare: case.rare.iter().filter(|r| r.name == pick.0.name).cloned().collect(), ..case.clone() };
                odds::roll(&only, &mut rng).0
            } else {
                pick.0
            };
            let def = st.db.weapon_of_item(&drop.name).unwrap_or(0);
            let wear = if drop.paint != 0 { trade_float(&st, &inputs, def, drop.paint) } else { 0.0 };
            let gloves = drop.name.contains("Gloves") || drop.name.contains("Wraps");
            let item = InventoryItem {
                uid: new_uid(), kind: "skin".into(), name: drop.name.replace("StatTrak™ ", ""), image: drop.image.clone(), grade: drop.rarity.clone(),
                color: drop.color.clone(), def, paint: drop.paint, wear, seed: rng.gen_range(0..=1000), stat_trak: inputs[0].stat_trak && !gloves,
                souvenir: false, phase: drop.phase.clone(), case_name: "Trade Up Contract".into(), collectible_id: 0,
                opened: chrono::Utc::now().to_rfc3339_opts(chrono::SecondsFormat::Millis, true), item_id: Some(drop.id.clone()), team: None, stickers: None,
            };
            let used: std::collections::HashSet<&String> = inputs.iter().map(|i| &i.uid).collect();
            let items = inv.items(app)?;
            items.retain(|i| !used.contains(&i.uid));
            items.insert(0, item.clone());
            inv.save(app);
            app.changed();
            let loadout = read_loadout(app, inv, &st)?;
            ok(json!({ "item": view(app, &st, &item, &loadout), "outcomes": check.outcomes }))
        }
        _ => Err(unknown(name)),
    }
}
