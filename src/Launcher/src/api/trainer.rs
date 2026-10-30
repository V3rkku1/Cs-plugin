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
            // The page asks on every server tick change (each kill): only re-read when the file changed.
            let file = game.plugin_dir().join(TRAINING_FILE);
            let meta = fs::metadata(&file).ok();
            let key = (
                meta.as_ref().map(|m| m.len()),
                meta.and_then(|m| m.modified().ok()),
                player,
            );
            let mut cache = STATS_CACHE.lock().unwrap();
            if let Some((_, v)) = cache.as_ref().filter(|c| c.0 == key) {
                return ok(v.clone());
            }
            let value = stats(&fs::read_to_string(&file).unwrap_or_default(), &key.2);
            *cache = Some((key, value.clone()));
            ok(value)
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

type StatsKey = (Option<u64>, Option<std::time::SystemTime>, String);
static STATS_CACHE: std::sync::Mutex<Option<(StatsKey, Value)>> = std::sync::Mutex::new(None);

/// One day's totals for one player.
#[derive(Default)]
struct Day {
    kills: u32,
    hs: u32,
    deaths: u32,
    reflex: Vec<f64>,
    flick: Vec<f64>,
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
    prefire: Vec<f64>,
    spray: Vec<f64>,
}

/// All-time totals for one gun or one map.
#[derive(Default)]
struct Tally {
    kills: u32,
    hs: u32,
    deaths: u32,
    shots: u32,
    hits: u32,
    rounds: u32,
    decided: u32,
    wins: u32,
    damage: u32,
    drill_rounds: u32,
    drill_wins: u32,
    course: Option<f64>,
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
    let mut guns: BTreeMap<String, Tally> = BTreeMap::new();
    let mut maps: BTreeMap<String, Tally> = BTreeMap::new();
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
        let yes = |k: &str| l[k].as_bool().unwrap_or(false) as u32;
        // Lines from before the map and gun were logged count for neither table.
        let mut map = l["m"]
            .as_str()
            .filter(|m| !m.is_empty())
            .map(|m| maps.entry(m.to_string()).or_default());
        let mut gun = l["w"]
            .as_str()
            .filter(|w| !w.is_empty())
            .map(|w| guns.entry(w.to_string()).or_default());
        match l["k"].as_str().unwrap_or("") {
            "kill" => {
                d.kills += 1;
                d.hs += yes("hs");
                for t in [map.as_deref_mut(), gun.as_deref_mut()]
                    .into_iter()
                    .flatten()
                {
                    t.kills += 1;
                    t.hs += yes("hs");
                }
            }
            "death" => {
                d.deaths += 1;
                if let Some(m) = map.as_deref_mut() {
                    m.deaths += 1;
                }
            }
            "gun" => {
                if let Some(g) = gun.as_deref_mut() {
                    g.shots += num("shots") as u32;
                    g.hits += num("hits") as u32;
                }
            }
            "prefire" => d.prefire.push(num("ms")),
            "spray" => d.spray.push(num("deg")),
            "course" => {
                if let Some(m) = map.as_deref_mut() {
                    let s = num("ms") / 1000.0;
                    m.course = Some(m.course.map_or(s, |b| b.min(s)));
                }
            }
            "reflex" => d.reflex.push(num("ms")),
            "flick" => d.flick.push(num("ms")),
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
                if let Some(m) = map.as_deref_mut() {
                    m.rounds += 1;
                    m.damage += num("dmg") as u32;
                    if let Some(won) = l["won"].as_bool() {
                        m.decided += 1;
                        m.wins += won as u32;
                    }
                }
            }
            "jump" => d.jumps.push((num("dist"), num("sync"))),
            "drill" => {
                d.drill_rounds += 1;
                d.drill_wins += yes("won");
                if let Some(m) = map.as_deref_mut() {
                    m.drill_rounds += 1;
                    m.drill_wins += yes("won");
                }
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
                "flick": avg(&d.flick), "flickN": d.flick.len(),
                "test": avg(&d.test), "testN": d.test.len(),
                                "csShots": d.cs_shots, "stopPct": pct(d.cs_ok, d.cs_shots),
                "rounds": d.rounds, "fired": d.fired, "hits": d.hits, "damage": d.damage,
                "accPct": pct(d.hits, d.fired),
                "adr": (d.rounds > 0).then(|| (d.damage as f64 / d.rounds as f64).round()),
                "jumps": d.jumps.len(), "jumpBest": d.jumps.iter().map(|j| j.0).reduce(f64::max), "sync": avg(&sync),
                "drillRounds": d.drill_rounds, "drillPct": pct(d.drill_wins, d.drill_rounds),
                "prefire": avg(&d.prefire), "prefireN": d.prefire.len(),
                "spray": (!d.spray.is_empty()).then(|| (d.spray.iter().sum::<f64>() / d.spray.len() as f64 * 100.0).round() / 100.0), "sprayN": d.spray.len(),
            })
        })
        .collect();
    let table = |all: BTreeMap<String, Tally>| -> Vec<Value> {
        all.into_iter()
            .map(|(name, t)| json!({
                "name": name, "kills": t.kills, "deaths": t.deaths, "hsPct": pct(t.hs, t.kills),
                "kd": (t.kills + t.deaths > 0).then(|| (t.kills as f64 * 100.0 / t.deaths.max(1) as f64).round() / 100.0),
                "shots": t.shots, "accPct": pct(t.hits, t.shots), "rounds": t.rounds, "winPct": pct(t.wins, t.decided),
                "adr": (t.rounds > 0).then(|| (t.damage as f64 / t.rounds as f64).round()),
                "drillRounds": t.drill_rounds, "drillPct": pct(t.drill_wins, t.drill_rounds),
                "course": t.course.map(|c| (c * 100.0).round() / 100.0),
            }))
            .collect()
    };
    json!({ "player": player, "players": players.iter().map(|p| p.0).collect::<Vec<_>>(), "days": days,
            "guns": table(guns), "maps": table(maps) })
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
        let guns = format!("{{\"t\":{t},\"k\":\"kill\",\"p\":\"me\",\"hs\":true,\"w\":\"ak47\",\"m\":\"de_dust2\"}}\n\
             {{\"t\":{t},\"k\":\"gun\",\"p\":\"me\",\"w\":\"ak47\",\"shots\":10,\"hits\":3,\"m\":\"de_dust2\"}}\n\
             {{\"t\":{t},\"k\":\"round\",\"p\":\"me\",\"shots\":10,\"hits\":3,\"dmg\":90,\"won\":true,\"m\":\"de_dust2\"}}\n\
             {{\"t\":{t},\"k\":\"course\",\"p\":\"me\",\"ms\":12345,\"m\":\"de_dust2\"}}\n");
        let g = stats(&guns, "me");
        assert_eq!(
            g["guns"],
            json!([{ "name": "ak47", "kills": 1, "deaths": 0, "hsPct": 100.0, "kd": 1.0, "shots": 10, "accPct": 30.0,
            "rounds": 0, "winPct": null, "adr": null, "drillRounds": 0, "drillPct": null, "course": null }])
        );
        let m = &g["maps"][0];
        assert_eq!(
            (
                m["name"].as_str(),
                m["winPct"].as_f64(),
                m["adr"].as_f64(),
                m["course"].as_f64()
            ),
            (Some("de_dust2"), Some(100.0), Some(90.0), Some(12.35))
        );
        let friend = &stats(&text, "friend")["days"][0];
        assert_eq!(
            (friend["kills"].as_u64(), friend["kd"].as_f64()),
            (Some(1), Some(1.0))
        );
    }
}
