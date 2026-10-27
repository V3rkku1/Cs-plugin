//! The Trainer page: the plugin's saved grenade lineups for a map (plugins/LanSkinTester/lineups/<map>.json).
//! Drills, the speedometer and going to a lineup are plain server commands (live.command).

use super::{arg, ok, unknown, App, R};
use serde_json::{json, Map, Value};
use std::fs;
use std::sync::Arc;

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "trainer.lineups" => {
            let map: String = arg(a)?;
            let Some(game) = app.state().game.clone() else {
                return ok(json!([]));
            };
            let file = game
                .plugin_dir()
                .join("lineups")
                .join(format!("{}.json", file_name(&map)));
            ok(lineups(&fs::read_to_string(file).unwrap_or_default()))
        }
        _ => Err(unknown(name)),
    }
}

/// The plugin's file name for a map: anything but letters and digits becomes `_`.
fn file_name(map: &str) -> String {
    map.chars()
        .map(|c| if c.is_alphanumeric() { c } else { '_' })
        .collect()
}

/// `{"name": {"From": {...}, "Weapon": "weapon_smokegrenade"}}` as `[{name, weapon}]`, sorted by name.
fn lineups(text: &str) -> Value {
    let all: Map<String, Value> = serde_json::from_str(text).unwrap_or_default();
    let mut list: Vec<Value> = all
        .iter()
        .map(|(name, l)| json!({ "name": name, "weapon": l["Weapon"].as_str().unwrap_or("").trim_start_matches("weapon_") }))
        .collect();
    list.sort_by(|a, b| a["name"].as_str().cmp(&b["name"].as_str()));
    Value::Array(list)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_plugin_lineup_file() {
        assert_eq!(file_name("workshop/123/de_x"), "workshop_123_de_x");
        let text = r#"{"window":{"From":{"X":1},"Weapon":"weapon_smokegrenade"},"ct":{"From":{},"Weapon":"weapon_flashbang"}}"#;
        assert_eq!(
            lineups(text),
            json!([{ "name": "ct", "weapon": "flashbang" }, { "name": "window", "weapon": "smokegrenade" }])
        );
        assert_eq!(lineups(""), json!([]));
    }
}
