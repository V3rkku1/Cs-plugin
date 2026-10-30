//! Storage units, like CS2's: a named box in the inventory that holds up to 1000 items. Stored items are hidden
//! from the inventory and can't be equipped, traded up or gifted until they're taken out. A unit is an
//! inventory item itself (kind "unit"), so Export / Import carry the units along with what's in them.

use super::super::{arg, bad, ok, App, R};
use super::{new_uid, now, read_loadout, take_off, view, require_plugin, Inv, InventoryItem};
use serde::Deserialize;
use serde_json::{json, Value};
use std::collections::HashSet;
use std::sync::Arc;

const UNIT_SIZE: usize = 1000;
const MAX_UNITS: usize = 100;

#[derive(Deserialize, Default)]
#[serde(default)]
struct RenameArgs {
    uid: String,
    name: String,
}

#[derive(Deserialize, Default)]
#[serde(default)]
struct StoreArgs {
    uids: Vec<String>,
    /// None takes the items out.
    unit: Option<String>,
}

/// The calls this file answers, or None to let cases.rs answer.
pub fn call(app: &Arc<App>, inv: &mut Inv, name: &str, a: &Value) -> Option<R<Value>> {
    Some(match name {
        "inventory.units" => units(inv, app),
        "inventory.unitItems" => unit_items(app, inv, &arg::<String>(a).ok()?),
        "inventory.unitNew" => create(app, inv, &arg::<String>(a).ok()?),
        "inventory.unitRename" => rename(app, inv, arg(a).ok()?),
        "inventory.unitDelete" => delete(app, inv, &arg::<String>(a).ok()?),
        "inventory.store" => store(app, inv, arg(a).ok()?),
        _ => return None,
    })
}

fn clean_name(name: &str) -> R<String> {
    let name: String = name.trim().chars().take(40).collect();
    if name.is_empty() {
        return Err(bad("Give the storage unit a name."));
    }
    Ok(name)
}

fn unit_of<'a>(items: &'a mut [InventoryItem], uid: &str) -> R<&'a mut InventoryItem> {
    items
        .iter_mut()
        .find(|i| i.uid == uid && i.kind == "unit")
        .ok_or_else(|| bad("That storage unit isn't there any more."))
}

fn units(inv: &mut Inv, app: &App) -> R<Value> {
    let items = inv.items(app)?;
    let list: Vec<Value> = items
        .iter()
        .filter(|i| i.kind == "unit")
        .map(|u| {
            let count = items.iter().filter(|i| i.unit.as_deref() == Some(&u.uid)).count();
            json!({ "uid": u.uid, "name": u.name, "count": count, "size": UNIT_SIZE })
        })
        .collect();
    ok(list)
}

fn unit_items(app: &App, inv: &mut Inv, uid: &str) -> R<Value> {
    let st = app.state();
    let loadout = read_loadout(app, inv, &st)?;
    let list: Vec<Value> = inv
        .items(app)?
        .iter()
        .filter(|i| i.unit.as_deref() == Some(uid))
        .map(|i| serde_json::to_value(view(app, &st, i, &loadout)).unwrap())
        .collect();
    ok(list)
}

fn create(app: &App, inv: &mut Inv, name: &str) -> R<Value> {
    let name = clean_name(name)?;
    let items = inv.items(app)?;
    if items.iter().filter(|i| i.kind == "unit").count() >= MAX_UNITS {
        return Err(bad(format!("You have {MAX_UNITS} storage units already.")));
    }
    let uid = new_uid();
    items.insert(
        0,
        InventoryItem {
            uid: uid.clone(),
            kind: "unit".into(),
            name,
            grade: "Base Grade".into(),
            color: "#b0c3d9".into(),
            case_name: "Storage Unit".into(),
            opened: now(),
            ..Default::default()
        },
    );
    inv.save(app);
    app.changed();
    ok(uid)
}

fn rename(app: &App, inv: &mut Inv, a: RenameArgs) -> R<Value> {
    let name = clean_name(&a.name)?;
    unit_of(inv.items(app)?, &a.uid)?.name = name;
    inv.save(app);
    app.changed();
    ok(())
}

/// Deletes the unit; what was in it goes back to the inventory.
fn delete(app: &App, inv: &mut Inv, uid: &str) -> R<Value> {
    let items = inv.items(app)?;
    unit_of(items, uid)?;
    let mut back = 0;
    for i in items.iter_mut().filter(|i| i.unit.as_deref() == Some(uid)) {
        i.unit = None;
        back += 1;
    }
    items.retain(|i| i.uid != uid);
    inv.save(app);
    app.toast(format!("Storage unit deleted. {back} item{} back in your inventory.", if back == 1 { "" } else { "s" }), "info");
    app.changed();
    ok(())
}

/// Moves items into a unit (equipped ones come off first, like in CS2) or, with no unit, takes them out.
fn store(app: &App, inv: &mut Inv, a: StoreArgs) -> R<Value> {
    let st = app.state();
    let uids: HashSet<&str> = a.uids.iter().map(String::as_str).collect();
    let items = inv.items(app)?;
    if let Some(unit) = &a.unit {
        unit_of(items, unit)?;
        let moving: Vec<InventoryItem> = items
            .iter()
            .filter(|i| uids.contains(i.uid.as_str()) && i.kind != "unit" && i.unit.as_ref() != Some(unit))
            .cloned()
            .collect();
        let inside = items.iter().filter(|i| i.unit.as_ref() == Some(unit)).count();
        if inside + moving.len() > UNIT_SIZE {
            return Err(bad(format!("A storage unit holds {UNIT_SIZE} items: this one has room for {}.", UNIT_SIZE - inside)));
        }
        let loadout = read_loadout(app, inv, &st)?;
        let worn: Vec<&InventoryItem> = moving.iter().filter(|i| view(app, &st, i, &loadout).equipped).collect();
        if !worn.is_empty() {
            let mut store = require_plugin(&st)?;
            for i in worn {
                take_off(store.local(), i);
            }
            store.save()?;
        }
    }
    let mut n = 0;
    for i in inv.items(app)?.iter_mut().filter(|i| uids.contains(i.uid.as_str()) && i.kind != "unit") {
        if i.unit != a.unit {
            i.unit = a.unit.clone();
            n += 1;
        }
    }
    inv.save(app);
    let s = if n == 1 { "" } else { "s" };
    app.toast(if a.unit.is_some() { format!("{n} item{s} stored.") } else { format!("{n} item{s} taken out.") }, "good");
    app.changed();
    ok(n)
}

#[cfg(test)]
mod tests {
    use super::super::loose;
    use super::*;

    #[test]
    fn stored_items_are_not_loose() {
        let unit = InventoryItem { uid: "u".into(), kind: "unit".into(), ..Default::default() };
        let mut gun = InventoryItem { uid: "g".into(), kind: "skin".into(), def: 7, ..Default::default() };
        assert!(!loose(&unit) && loose(&gun));
        gun.unit = Some("u".into());
        assert!(!loose(&gun));
        assert!(super::super::find(&[unit, gun], "g").is_err());
    }
}
