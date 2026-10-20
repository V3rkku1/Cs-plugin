//! Workshop page: the maps Steam has downloaded for CS2, a search of the Steam Workshop, and one click to host a
//! map. Subscribing happens in Steam (the launcher opens the item's page there).

use super::play::{apply_to_server, start_server, AskArgs};
use super::{arg, bad, ok, open_external, unknown, App, State, R};
use crate::game;
use crate::server::{self, ServerSettings};
use crate::session::{self, Session};
use crate::util;
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::HashMap;
use std::sync::{Arc, LazyLock, Mutex};

/// Titles and pictures from Steam, kept for the launcher's run (they rarely change).
static DETAILS: LazyLock<Mutex<HashMap<String, Value>>> = LazyLock::new(Default::default);

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct WorkshopItem {
    id: String,
    title: String,
    preview: String,
    size: String,
    updated: String,
    downloaded: bool,
    cs2: bool,
    tags: String,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct Search {
    text: Option<String>,
    sort: Option<String>,
    page: i32,
}

fn is_workshop_id(id: &str) -> bool {
    id.len() >= 6 && id.chars().all(|c| c.is_ascii_digit())
}

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "workshop.state" => {
            let (game, hosting) = {
                let st = app.state();
                (
                    st.game.clone(),
                    st.test_mode_on()
                        .then(|| st.settings.server.workshop_map_id())
                        .flatten(),
                )
            };
            let Some(game) = game else {
                return ok(json!({ "hasGame": false, "downloaded": [], "hosting": null }));
            };
            let mut downloaded = game::all_downloaded(&game);
            downloaded.sort_by_key(|d| std::cmp::Reverse(d.updated));
            load_details(app, downloaded.iter().map(|d| d.id.clone()).collect());
            let items: Vec<WorkshopItem> = downloaded
                .iter()
                .map(|d| to_item(&d.id, true, d.bytes, Some(d.updated)))
                .collect();
            ok(json!({ "hasGame": true, "downloaded": items, "hosting": hosting }))
        }
        "workshop.search" => {
            let q: Search = arg(a)?;
            let text = q.text.unwrap_or_default().trim().to_string();
            let sort = match q.sort.as_deref() {
                Some("new") => "mostrecent",
                Some("top") => "toprated",
                _ if text.is_empty() => "trend",
                _ => "textsearch",
            };
            let url = format!(
                "https://steamcommunity.com/workshop/browse/?appid=730&section=readytouseitems&numperpage=30&browsesort={sort}&actualsort={sort}&days=-1&p={}{}",
                q.page.clamp(1, 50), if text.is_empty() { String::new() } else { format!("&searchtext={}", util::escape(&text)) }
            );
            let html = util::get_text(&app.http, &url)
                .map_err(|e| bad(format!("Couldn't reach the Steam Workshop: {e}")))?;
            static ID: LazyLock<Regex> =
                LazyLock::new(|| Regex::new(r"sharedfiles/filedetails/\?id=(\d{6,})").unwrap());
            let mut ids: Vec<String> = vec![];
            for c in ID.captures_iter(&html) {
                if !ids.contains(&c[1].to_string()) && ids.len() < 30 {
                    ids.push(c[1].to_string());
                }
            }
            load_details(app, ids.clone());
            let game = app.state().game.clone();
            let items: Vec<WorkshopItem> = ids
                .iter()
                .map(|id| {
                    to_item(
                        id,
                        game.as_ref()
                            .is_some_and(|g| game::workshop_downloaded(g, id)),
                        0,
                        None,
                    )
                })
                .filter(|i| !i.title.is_empty())
                .collect();
            ok(items)
        }
        "workshop.subscribe" => {
            let id: String = arg(a)?;
            if !is_workshop_id(&id) {
                return Err(bad("Not a Workshop item."));
            }
            open_external(game::subscribe_url(&id)).map_err(|_| bad("Could not open Steam."))?;
            app.toast(
                "Opened in Steam: press Subscribe. It shows under Downloaded once Steam has it.",
                "info",
            );
            ok(())
        }
        "workshop.host" => {
            let id: String = arg(a)?;
            if !is_workshop_id(&id) {
                return Err(bad("Not a Workshop item."));
            }
            let on = {
                let mut st = app.state();
                if try_play_offline(app, &st, &id)? {
                    return ok(());
                }
                let mut s = ServerSettings::for_workshop(&st.settings.server, &id);
                // Never played here before: a mode that fits its name (de_, aim_, arms race...), else Custom.
                if !s.map_modes.contains_key(&id) {
                    if let Some(d) = DETAILS.lock().unwrap().get(&id) {
                        if let Some(guess) = server::guess_mode(
                            &html_decode(d["title"].as_str().unwrap_or("")),
                            &tags(d),
                        ) {
                            s.mode = guess.into();
                        }
                    }
                }
                st.settings.server = s;
                app.save(&st);
                st.test_mode_on()
            };
            app.changed();
            if on {
                apply_to_server(app, AskArgs::default())?
            } else {
                start_server(app, AskArgs::default())?
            }
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

/// Movement Hub and similar maps switch maps with map_workshop, which a dedicated server doesn't have: those
/// start as an offline game instead. Returns false for a normal map.
pub fn try_play_offline(app: &App, st: &State, id: &str) -> R<bool> {
    let Some(game) = &st.game else {
        return Ok(false);
    };
    let Some(map) = game::offline_only_map(game, id) else {
        return Ok(false);
    };
    const WHY: &str = "This map only works as an offline game (its own script changes maps), so it doesn't run on the server.";
    if st.test_mode_on() {
        return Err(bad(format!("{WHY} Press Stop first, then Host it again.")));
    }
    if session::cs2_running() {
        return Err(bad(format!("{WHY} Close CS2 first, then Host it again.")));
    }
    Session { game }.play_offline(&st.settings.server, id, &map)?;
    app.log(format!("Workshop map {id} ({map}) starts as an offline game: its script changes maps itself, which only works without the server. Friends can't join it."));
    app.toast(format!("Starting {map} offline…"), "info");
    Ok(true)
}

fn html_decode(text: &str) -> String {
    text.replace("&quot;", "\"")
        .replace("&#39;", "'")
        .replace("&apos;", "'")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
        .replace("&amp;", "&")
}

fn tags(d: &Value) -> Vec<String> {
    d["tags"]
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(|t| t["tag"].as_str())
        .filter(|t| !t.is_empty())
        .map(String::from)
        .collect()
}

fn number(d: &Value, name: &str) -> i64 {
    d[name]
        .as_i64()
        .or_else(|| d[name].as_str().and_then(|s| s.parse().ok()))
        .unwrap_or(0)
}

fn size_text(bytes: u64) -> String {
    match bytes {
        0 => String::new(),
        b if b >= 1 << 30 => format!("{:.1} GB", b as f64 / (1u64 << 30) as f64),
        b => format!("{:.0} MB", b as f64 / (1u64 << 20) as f64),
    }
}

fn to_item(
    id: &str,
    downloaded: bool,
    bytes: u64,
    updated: Option<chrono::DateTime<chrono::Utc>>,
) -> WorkshopItem {
    let date = |w: Option<chrono::DateTime<chrono::Utc>>| {
        w.map_or(String::new(), |w| {
            w.with_timezone(&chrono::Local)
                .format("%-d %b %Y")
                .to_string()
        })
    };
    let Some(d) = DETAILS.lock().unwrap().get(id).cloned() else {
        return WorkshopItem {
            id: id.into(),
            title: if downloaded {
                format!("Workshop map {id}")
            } else {
                String::new()
            },
            preview: String::new(),
            size: size_text(bytes),
            updated: date(updated),
            downloaded,
            cs2: true,
            tags: String::new(),
        };
    };
    let tags = tags(&d);
    let time = number(&d, "time_updated");
    let cs2 = tags.iter().any(|t| t.to_lowercase().contains("cs2")) || time > 1695686400; // after CS2's release
    let size = if bytes > 0 {
        bytes
    } else {
        number(&d, "file_size").max(0) as u64
    };
    let when = updated.or_else(|| {
        (time > 0)
            .then(|| chrono::DateTime::from_timestamp(time, 0))
            .flatten()
    });
    WorkshopItem {
        id: id.into(),
        title: html_decode(d["title"].as_str().unwrap_or("")),
        preview: d["preview_url"].as_str().unwrap_or("").into(),
        size: size_text(size),
        updated: date(when),
        downloaded,
        cs2,
        tags: tags
            .iter()
            .filter(|t| !t.eq_ignore_ascii_case("CS2"))
            .take(3)
            .cloned()
            .collect::<Vec<_>>()
            .join(" · "),
    }
}

/// Steam's public details call (no key needed): titles, pictures, sizes and tags. Offline: ids instead of titles.
fn load_details(app: &App, ids: Vec<String>) {
    let missing: Vec<String> = {
        let d = DETAILS.lock().unwrap();
        ids.into_iter().filter(|id| !d.contains_key(id)).collect()
    };
    for batch in missing.chunks(50) {
        let mut form: Vec<(String, String)> = vec![("itemcount".into(), batch.len().to_string())];
        form.extend(
            batch
                .iter()
                .enumerate()
                .map(|(i, id)| (format!("publishedfileids[{i}]"), id.clone())),
        );
        let Ok(mut r) = app
            .http
            .post("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/")
            .send_form(form)
        else {
            continue;
        };
        let Some(doc) = r
            .body_mut()
            .read_to_string()
            .ok()
            .and_then(|t| serde_json::from_str::<Value>(&t).ok())
        else {
            continue;
        };
        let mut d = DETAILS.lock().unwrap();
        for item in doc["response"]["publishedfiledetails"]
            .as_array()
            .into_iter()
            .flatten()
        {
            if let Some(id) = item["publishedfileid"].as_str() {
                d.insert(id.into(), item.clone());
            }
        }
    }
}
