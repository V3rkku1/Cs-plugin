//! The Trainer page: the plugin's saved grenade lineups for a map (plugins/LanSkinTester/lineups/<map>.json).
//! Drills, the speedometer and going to a lineup are plain server commands (live.command).
//! The Stats page: the plugin's training.jsonl (one line per kill, death, reaction time, shot, jump and
//! drill round), added up per day; the launcher's own reaction test adds lines too.

use super::{arg, ok, unknown, App, R};
use chrono::{Local, NaiveDate, TimeZone};
use serde_json::{json, Map, Value};
use std::collections::{BTreeMap, HashMap};
use std::fs;
use std::io::Write;
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
        "trainer.stats" => {
            let player: String = arg(a)?;
            let Some(game) = app.state().game.clone() else {
                return ok(json!({ "players": [], "days": [] }));
            };
            let text =
                fs::read_to_string(game.plugin_dir().join(TRAINING_FILE)).unwrap_or_default();
            ok(stats(&text, &player))
        }
        "trainer.statsReset" => {
            // Kept as training.jsonl.bak, so one mistaken click can be undone by hand.
            let game = app.state().game()?.clone();
            let file = game.plugin_dir().join(TRAINING_FILE);
            if file.exists() {
                fs::rename(&file, file.with_extension("jsonl.bak"))?;
            }
            app.changed();
            ok(())
        }
        "trainer.reaction" => {
            let ms: f64 = arg(a)?;
            let game = app.state().game()?.clone();
            let line =
                json!({ "t": Local::now().timestamp(), "k": "test", "p": "", "ms": ms.round() });
            fs::OpenOptions::new()
                .create(true)
                .append(true)
                .open(game.plugin_dir().join(TRAINING_FILE))?
                .write_all(format!("{line}\n").as_bytes())?;
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

const TRAINING_FILE: &str = "training.jsonl";

/// One day's totals for one player.
#[derive(Default)]
struct Day {
    kills: u32,
    hs: u32,
    deaths: u32,
    reflex: Vec<f64>,
    test: Vec<f64>,
    cs_shots: u32,
    cs_ok: u32,
    rounds: u32,
    fired: u32,
    hits: u32,
    damage: u32,
    jumps: Vec<(f64, f64)>,
    drill_rounds: u32,
    drill_wins: u32,
}

/// Players seen (most lines first) and the chosen one's days, oldest first. "" picks the most active
/// player. Lines with no player (the launcher's reaction test) count for everyone.
fn stats(text: &str, player: &str) -> Value {
    let lines: Vec<Value> = text
        .lines()
        .filter_map(|l| serde_json::from_str(l).ok())
        .collect();
    let mut counts: HashMap<&str, usize> = HashMap::new();
    for l in &lines {
        if let Some(p) = l["p"].as_str().filter(|p| !p.is_empty()) {
            *counts.entry(p).or_default() += 1;
        }
    }
    let mut players: Vec<(&str, usize)> = counts.into_iter().collect();
    players.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(b.0)));
    let player = if player.is_empty() {
        players.first().map_or("", |p| p.0)
    } else {
        player
    };

    let mut days: BTreeMap<NaiveDate, Day> = BTreeMap::new();
    for l in &lines {
        let p = l["p"].as_str().unwrap_or("");
        let Some(t) = l["t"].as_i64() else { continue };
        if !p.is_empty() && p != player {
            continue;
        }
        let Some(date) = Local.timestamp_opt(t, 0).single().map(|d| d.date_naive()) else {
            continue;
        };
        let d = days.entry(date).or_default();
        let num = |k: &str| l[k].as_f64().unwrap_or(0.0);
        match l["k"].as_str().unwrap_or("") {
            "kill" => {
                d.kills += 1;
                d.hs += l["hs"].as_bool().unwrap_or(false) as u32;
            }
            "death" => d.deaths += 1,
            "reflex" => d.reflex.push(num("ms")),
            "test" => d.test.push(num("ms")),
            "shot" => {
                d.cs_shots += 1;
                d.cs_ok += l["ok"].as_bool().unwrap_or(false) as u32;
            }
            "round" => {
                d.rounds += 1;
                d.fired += num("shots") as u32;
                d.hits += num("hits") as u32;
                d.damage += num("dmg") as u32;
            }
            "jump" => d.jumps.push((num("dist"), num("sync"))),
            "drill" => {
                d.drill_rounds += 1;
                d.drill_wins += l["won"].as_bool().unwrap_or(false) as u32;
            }
            _ => {}
        }
    }
    let avg = |v: &[f64]| (!v.is_empty()).then(|| (v.iter().sum::<f64>() / v.len() as f64).round());
    let pct = |a: u32, b: u32| (b > 0).then(|| (a as f64 * 1000.0 / b as f64).round() / 10.0);
    let days: Vec<Value> = days
        .iter()
        .map(|(date, d)| {
            let sync: Vec<f64> = d.jumps.iter().map(|j| j.1).collect();
            json!({
                "day": date.format("%Y-%m-%d").to_string(),
                "kills": d.kills, "deaths": d.deaths, "hs": d.hs,
                "hsPct": pct(d.hs, d.kills),
                "kd": (d.kills + d.deaths > 0).then(|| (d.kills as f64 * 100.0 / d.deaths.max(1) as f64).round() / 100.0),
                "reflex": avg(&d.reflex), "reflexBest": d.reflex.iter().cloned().reduce(f64::min), "reflexN": d.reflex.len(),
                "test": avg(&d.test), "testN": d.test.len(),
                                "csShots": d.cs_shots, "stopPct": pct(d.cs_ok, d.cs_shots),
                "rounds": d.rounds, "fired": d.fired, "hits": d.hits, "damage": d.damage,
                "accPct": pct(d.hits, d.fired),
                "adr": (d.rounds > 0).then(|| (d.damage as f64 / d.rounds as f64).round()),
                "jumps": d.jumps.len(), "jumpBest": d.jumps.iter().map(|j| j.0).reduce(f64::max), "sync": avg(&sync),
                "drillRounds": d.drill_rounds, "drillPct": pct(d.drill_wins, d.drill_rounds),
            })
        })
        .collect();
    json!({ "player": player, "players": players.iter().map(|p| p.0).collect::<Vec<_>>(), "days": days })
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

    #[test]
    fn adds_up_training_per_day_and_player() {
        let t = Local
            .with_ymd_and_hms(2026, 10, 1, 12, 0, 0)
            .unwrap()
            .timestamp();
        let text = format!(
            "{{\"t\":{t},\"k\":\"kill\",\"p\":\"me\",\"hs\":true}}\n{{\"t\":{t},\"k\":\"kill\",\"p\":\"me\",\"hs\":false}}\n\
             {{\"t\":{t},\"k\":\"death\",\"p\":\"me\"}}\n{{\"t\":{t},\"k\":\"reflex\",\"p\":\"me\",\"ms\":300}}\n\
             {{\"t\":{t},\"k\":\"reflex\",\"p\":\"me\",\"ms\":500}}\n{{\"t\":{t},\"k\":\"test\",\"p\":\"\",\"ms\":200}}\n\
             {{\"t\":{t},\"k\":\"round\",\"p\":\"me\",\"shots\":10,\"hits\":4,\"dmg\":120}}\n\
             {{\"t\":{t},\"k\":\"round\",\"p\":\"me\",\"shots\":0,\"hits\":0,\"dmg\":0}}\n\
             {{\"t\":{t},\"k\":\"kill\",\"p\":\"friend\",\"hs\":true}}\nnot json\n"
        );
        let s = stats(&text, "");
        assert_eq!(s["player"], "me");
        assert_eq!(s["players"], json!(["me", "friend"]));
        let day = &s["days"][0];
        assert_eq!(day["day"], "2026-10-01");
        assert_eq!(
            (
                day["kills"].as_u64(),
                day["hsPct"].as_f64(),
                day["kd"].as_f64()
            ),
            (Some(2), Some(50.0), Some(2.0))
        );
        assert_eq!(
            (
                day["reflex"].as_f64(),
                day["reflexBest"].as_f64(),
                day["test"].as_f64()
            ),
            (Some(400.0), Some(300.0), Some(200.0))
        );
        assert_eq!(
            (day["accPct"].as_f64(), day["adr"].as_f64()),
            (Some(40.0), Some(60.0))
        );
        let friend = &stats(&text, "friend")["days"][0];
        assert_eq!(
            (friend["kills"].as_u64(), friend["kd"].as_f64()),
            (Some(1), Some(1.0))
        );
    }
}
