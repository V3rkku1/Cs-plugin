//! Play and Live Server pages: quick starts, start / apply / stop the local server, the server settings and the
//! live controls. Every way of starting the server (Start, F5, quick starts, Friends, Workshop) goes through
//! `start_server`.

use super::{arg, bad, ok, open_external, plural, unknown, ApiError, App, State, R};
use crate::game::{self, GameFolder};
use crate::installer::{self, Installer};
use crate::items::{self, ServerStatus, STATUS_FILE};
use crate::net;
use crate::server::{self, ServerSettings, PORT, PRESETS};
use crate::session::{self, Session};
use crate::settings::SavedSetup;
use crate::util;
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::{json, Map, Value};
use std::sync::atomic::Ordering;
use std::sync::{Arc, LazyLock};
use std::time::{Duration, Instant};

#[derive(Default, PartialEq, Clone, Copy)]
enum JoinWhenUp {
    #[default]
    None,
    /// Start the game as soon as the server has read gameinfo.gi, so both load at once.
    StartGame,
    /// After a server restart with the game left open.
    Reconnect,
    /// The game was started early; if it hasn't joined a while after the server is up, send it again.
    CheckJoined,
}

/// What the running server was started with (None when this launcher didn't start it), and start progress.
#[derive(Default)]
pub struct PlayState {
    running_mode: Option<String>,
    running_map: Option<String>,
    running_retake_key: Option<String>,
    /// Started with sv_lan 0 (can download Workshop maps).
    running_online: bool,
    running_for_friends: bool,
    /// Started with -maxplayers (Custom mode).
    running_slots: bool,
    /// Opening the router port / downloading a server plugin.
    pub busy: bool,
    /// The last "could not start", shown on the hero until the next start.
    start_error: Option<String>,
    last_tick: String,
    known_players: Option<Vec<String>>,
    // Workshop start: the server boots on a normal map, and once it's up (and logged on to Steam) it is switched
    // to the Workshop map. CS2 starts once the Workshop map is loaded.
    pending_workshop_id: Option<String>,
    bootstrap_map: Option<String>,
    start_game_after_workshop: bool,
    server_up_since: Option<Instant>,
    workshop_sent_at: Option<Instant>,
    join_when_up: JoinWhenUp,
    join_up_since: Option<Instant>,
    join_asked_at: Option<i64>,
}

#[derive(Deserialize, Serialize, Default, Clone)]
#[serde(rename_all = "camelCase", default)]
pub struct AskArgs {
    pub confirmed: bool,
    pub for_friends: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct LiveStatus {
    map: String,
    mode: String,
    humans: i32,
    bots: i32,
    players: Vec<String>,
    skins_allowed: bool,
    disabled_reason: String,
    version: String,
    stats: Vec<items::PlayerStats>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct PlayView {
    has_game: bool,
    ready: bool,
    test_mode: bool,
    busy: bool,
    start_error: Option<String>,
    settings: Value,
    status: Option<LiveStatus>,
    live_title: String,
    live_detail: String,
    live_kind: &'static str,
    invite: Value,
    presets: Vec<Value>,
    modes: Vec<Value>,
    workshop: Value,
    hacker_detail: &'static str,
    difficulties: [&'static str; 4],
    bot_teams: [&'static str; 3],
    bot_weapons: [&'static str; 5],
    bot_rifles: Vec<&'static str>,
    retake_styles: [&'static str; 2],
    give: Vec<Value>,
    port: u16,
    step: i32,
    console_error: Option<String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct PresetArgs {
    title: String,
    saved: bool,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct SaveSetupArgs {
    name: String,
    confirmed: bool,
}

fn give_items() -> Vec<(&'static str, &'static str)> {
    let mut list: Vec<(&str, &str)> = items::DISPLAY_NAMES
        .iter()
        .map(|d| (items::classname(d.0).unwrap(), d.1))
        .collect();
    list.sort_by_key(|d| d.1.to_lowercase());
    list.push(("weapon_knife", "Knife (equipped)"));
    list
}

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "play.state" => ok(view(&app.state())),
        "play.startOrApply" => {
            let (on, ready) = {
                let st = app.state();
                (st.test_mode_on(), st.ready())
            };
            if on {
                apply_to_server(app, AskArgs::default())?;
            } else if ready {
                start_server(app, AskArgs::default())?;
            } else {
                return Err(bad("Install first (Setup page)."));
            }
            ok(())
        }
        "play.start" => start_server(app, arg(a)?).map(|_| Value::Null),
        "play.apply" => apply_to_server(app, arg(a)?).map(|_| Value::Null),
        "play.openGame" => {
            let st = app.state();
            let game = st.game()?.clone();
            isolate_client(app, &game, st.server_status().as_ref());
            Session { game: &game }
                .start_game(&st.settings.server)
                .map_err(|e| {
                    app.log(format!("Could not start CS2: {e}"));
                    bad(format!("Could not start CS2: {e}"))
                })?;
            app.log("Starting CS2 (VAC off)...");
            app.toast("Starting CS2…", "info");
            ok(())
        }
        "play.stop" => stop_test(app, arg(a)?).map(|_| Value::Null),
        "play.preset" => {
            let a: PresetArgs = arg(a)?;
            let (on, ready) = {
                let mut st = app.state();
                if a.saved {
                    let setup = st
                        .settings
                        .saved_setups
                        .iter()
                        .find(|x| x.name.eq_ignore_ascii_case(&a.title))
                        .cloned()
                        .ok_or_else(|| bad("That saved setup is gone."))?;
                    st.settings.server.copy_practice_from(&setup.settings);
                } else {
                    let preset = PRESETS
                        .iter()
                        .find(|p| p.title == a.title)
                        .ok_or_else(|| bad("Unknown quick start."))?;
                    (preset.apply)(&mut st.settings.server);
                }
                app.save(&st);
                (st.test_mode_on(), st.ready())
            };
            app.log(format!("Quick start: {}", a.title));
            app.changed();
            if on {
                apply_to_server(app, AskArgs::default())?;
            } else if ready {
                start_server(app, AskArgs::default())?;
            } else {
                app.log("Install first (Setup page), then pick the quick start again.");
            }
            ok(())
        }
        "play.saveSetup" => {
            let a: SaveSetupArgs = arg(a)?;
            let name: String = a.name.trim().chars().take(24).collect();
            if name.is_empty() {
                return ok(());
            }
            if PRESETS.iter().any(|p| p.title.eq_ignore_ascii_case(&name)) {
                return Err(bad("That name is already a built-in quick start."));
            }
            let mut st = app.state();
            let existing = st
                .settings
                .saved_setups
                .iter()
                .position(|x| x.name.eq_ignore_ascii_case(&name));
            if existing.is_some() && !a.confirmed {
                ask(
                    app,
                    "Replace setup?",
                    &format!("Replace the saved setup \"{name}\" with the current settings?"),
                    "Replace",
                    "play.saveSetup",
                    json!({ "name": name, "confirmed": true }),
                    false,
                );
                return ok(());
            }
            let setup = SavedSetup {
                name: name.clone(),
                settings: st.settings.server.clone(),
            };
            match existing {
                Some(i) => st.settings.saved_setups[i] = setup,
                None => st.settings.saved_setups.push(setup),
            }
            app.save(&st);
            drop(st);
            app.log(format!("Saved setup \"{name}\"."));
            app.toast(format!("Saved setup \"{name}\"."), "good");
            app.changed();
            ok(())
        }
        "play.deleteSetup" => {
            let name: String = arg(a)?;
            let mut st = app.state();
            st.settings
                .saved_setups
                .retain(|x| !x.name.eq_ignore_ascii_case(&name));
            app.save(&st);
            drop(st);
            app.toast(format!("Deleted \"{name}\"."), "info");
            app.changed();
            ok(())
        }
        "live.saveTeamSize" => {
            let mut st = app.state();
            let s = &mut st.settings.server;
            let key = s.map_key();
            let sizes = vec![s.team_size_t, s.team_size_ct];
            s.map_team_sizes.insert(key, sizes.clone());
            app.save(&st);
            drop(st);
            app.toast(
                format!("Team sizes {}v{} saved for this map.", sizes[0], sizes[1]),
                "good",
            );
            app.changed();
            ok(())
        }
        "live.set" => set_server(app, arg(a)?).map(|_| Value::Null),
        "live.reset" => {
            let mut st = app.state();
            st.settings.server = ServerSettings::stock(&st.settings.server);
            app.save(&st);
            drop(st);
            app.toast("Back to a plain CS2 server.", "info");
            app.changed();
            ok(())
        }
        "live.subscribe" => {
            let id = app
                .state()
                .settings
                .server
                .workshop_map_id()
                .ok_or_else(|| bad("Paste the map's ID or Workshop link first."))?;
            open_external(game::subscribe_url(&id)).map_err(|e| {
                app.log(format!("Could not open Steam: {e:?}"));
                bad("Could not open Steam.")
            })?;
            app.log("Opened the map in Steam: press Subscribe there, wait for the download, then Start (or Apply).");
            ok(())
        }
        "live.command" => send_command(app, &arg::<String>(a)?).map(|_| Value::Null),
        "live.botweapon" => {
            let weapon: String = arg(a)?;
            let mut st = app.state();
            st.settings.server.bot_force_weapon =
                if server::BOT_FORCED_WEAPONS.contains(&weapon.as_str()) {
                    weapon
                } else {
                    String::new()
                };
            app.save(&st);
            app.changed();
            let (Some(game), Some(_)) = (st.game.clone(), st.server_status()) else {
                return ok(());
            };
            let s = &st.settings.server;
            s.write_cfg(&game)
                .and_then(|_| server::send_commands(&game, &s.bot_weapon_commands()))
                .map_err(|e| {
                    app.log(format!("Could not send: {e}"));
                    bad(format!("Could not send: {e}"))
                })?;
            app.log(if s.forces_bot_weapon() {
                format!("Bots get the {} from the next round.", s.bot_force_weapon)
            } else {
                "Bots buy normally again from the next round.".into()
            });
            ok(())
        }
        "live.scouts" => set_scouts(app, arg(a)?).map(|_| Value::Null),
        "live.give" => {
            let classname: String = arg(a)?;
            if !give_items().iter().any(|i| i.0 == classname) {
                return Err(bad("Unknown weapon."));
            }
            send_command(app, &format!("lst_give {classname}")).map(|_| Value::Null)
        }
        "live.damage" => {
            let mut st = app.state();
            st.settings.server.show_damage = !st.settings.server.show_damage;
            app.save(&st);
            let on = st.settings.server.show_damage;
            if let Some(g) = st.game.clone() {
                st.settings.server.write_cfg(&g)?;
                drop(st);
                send_command(app, &format!("lst_show_damage {}", on as i32))?;
            }
            app.changed();
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

/// Asking first: the page shows the question and calls again with confirmed = true. An event (not a return
/// value) so F5 / Shift+F5 from any page get the question too.
fn ask(app: &App, title: &str, message: &str, ok: &str, call: &str, args: Value, danger: bool) {
    app.push("play.ask", json!({ "title": title, "message": message, "ok": ok, "call": call, "args": args, "danger": danger }));
}

// -------------------------------------------------------------------------------------------------
// State
// -------------------------------------------------------------------------------------------------

/// Where a start is: 0 server loading, 1 map up (CS2 not started), 2 CS2 joining, 3 joined, -1 not running.
fn join_step(st: &State, status: Option<&ServerStatus>) -> i32 {
    if !st.test_mode_on() {
        -1
    } else {
        match status {
            None => 0,
            Some(s) if s.humans > 0 => 3,
            _ if session::client_running() => 2,
            _ => 1,
        }
    }
}

/// Plugin retakes runs as competitive (0/1): show Retakes while that style is the one we started.
fn mode_name(st: &State, status: &ServerStatus) -> String {
    let key = st.play.running_retake_key.as_deref();
    let plugin_session = key.is_some_and(|k| k != "valve" && k != "off")
        || (key.is_none()
            && st
                .game
                .as_ref()
                .is_some_and(installer::third_party_retakes_loaded)
            && st.settings.server.is_plugin_retakes());
    if status.game_type == 0 && status.game_mode == 1 && plugin_session {
        return "Retakes".into();
    }
    if status.game_type == 0
        && status.game_mode == 1
        && st.settings.server.mode == server::WEAPONS_EXPERT_MODE
    {
        return server::WEAPONS_EXPERT_MODE.into();
    }
    server::modes()
        .into_iter()
        .find(|m| m.game_type == status.game_type && m.mode == status.game_mode)
        .map(|m| m.name)
        .unwrap_or_else(|| format!("mode {}/{}", status.game_type, status.game_mode))
}

/// The newest console line that looks like a map or load failure, instead of "loading" forever.
fn map_error(st: &State) -> Option<String> {
    static FAIL: LazyLock<Regex> = LazyLock::new(|| {
        Regex::new(r"(?i)(failed|unable|can't|cannot|couldn't|missing|not found).*(map|workshop|load)|(map|workshop).*(failed|not found|missing)").unwrap()
    });
    let game = st.game.as_ref()?;
    Session { game }
        .console_tail(60)
        .into_iter()
        .rev()
        .find(|l| FAIL.is_match(l))
}

fn invite(s: &ServerSettings) -> Value {
    let address = net::host_address(s);
    let how = match s.friends {
        server::INTERNET => "Internet",
        server::VPN => "Radmin VPN",
        _ => "same Wi-Fi",
    };
    let text = match &address {
        Some(a) => format!("{a} · {how}"),
        None if s.friends == server::INTERNET => "Router address shows after Start".into(),
        None => format!("No {how} address found · see the Friends page"),
    };
    json!({ "address": address, "how": how, "text": text })
}

fn workshop_info(st: &State, s: &ServerSettings) -> Value {
    let info = |id: Option<String>, note: &str, kind: &str| json!({ "id": id, "note": note, "kind": kind });
    let Some(id) = s.workshop_map_id() else {
        return if s.workshop_map.trim().is_empty() {
            info(None, "Optional · overrides the map", "muted")
        } else {
            info(None, "Paste the map's ID or Workshop link", "warning")
        };
    };
    if st
        .game
        .as_ref()
        .is_some_and(|g| game::workshop_downloaded(g, &id))
    {
        info(
            Some(id),
            "Subscribed · the server loads it from the Workshop",
            "good",
        )
    } else {
        info(
            Some(id),
            "The server downloads it (needs internet)",
            "muted",
        )
    }
}

fn view(st: &State) -> PlayView {
    let s = &st.settings.server;
    let status = st.server_status();
    let test = st.test_mode_on();
    let waiting = st.play.pending_workshop_id.is_some();
    let live = status.as_ref().map(|x| LiveStatus {
        map: x.map.clone(),
        mode: mode_name(st, x),
        humans: x.humans,
        bots: x.bots,
        players: x.players.clone(),
        skins_allowed: x.skins_allowed,
        disabled_reason: x.disabled_reason.clone(),
        version: x.version.clone(),
        stats: x.stats.clone(),
    });
    let (title, detail, kind) = match (&status, &live) {
        (Some(x), Some(l)) => {
            let mut detail = if x.skins_allowed {
                format!(
                    "{} · {} · skins working · plugin {}",
                    plural(x.humans, "player"),
                    plural(x.bots, "bot"),
                    x.version
                )
            } else {
                x.disabled_reason.clone()
            };
            let mut kind = if x.skins_allowed { "good" } else { "warning" };
            if waiting {
                detail = if st.play.workshop_sent_at.is_none() {
                    "Server is up - loading the Workshop map next…".into()
                } else {
                    "Downloading and loading the Workshop map… CS2 starts by itself when it's ready.".into()
                };
                kind = "busy";
            }
            (format!("{} · {}", x.map, l.mode), detail, kind)
        }
        _ if !test => (
            "Server not running".into(),
            "Press Start or pick a quick start.".into(),
            "off",
        ),
        _ if waiting => (
            "Starting the server for the Workshop map…".into(),
            "Then it downloads the map. CS2 starts by itself when it's loaded.".into(),
            "busy",
        ),
        _ => (
            "Server starting…".into(),
            "Loading the map can take a minute. Live controls unlock when it's up.".into(),
            "busy",
        ),
    };
    let presets = PRESETS
        .iter()
        .map(|p| json!({ "title": p.title, "subtitle": p.subtitle, "saved": false }))
        .chain(
            st.settings
                .saved_setups
                .iter()
                .filter(|x| !x.name.trim().is_empty())
                .map(
                    |x| json!({ "title": x.name, "subtitle": x.settings.summary(), "saved": true }),
                ),
        )
        .collect();
    PlayView {
        has_game: st.game.is_some(),
        ready: st.ready(),
        test_mode: test,
        busy: st.play.busy,
        start_error: st.play.start_error.clone(),
        settings: util::web(s),
        step: join_step(st, status.as_ref()),
        console_error: if status.is_none() && test {
            map_error(st)
        } else {
            None
        },
        status: live,
        live_title: title,
        live_detail: detail,
        live_kind: kind,
        invite: invite(s),
        presets,
        modes: server::modes()
            .into_iter()
            .map(|m| json!({ "name": m.name, "maps": m.maps }))
            .collect(),
        workshop: workshop_info(st, s),
        hacker_detail: super::hacker::summary(s),
        difficulties: server::DIFFICULTIES,
        bot_teams: server::BOT_TEAMS,
        bot_weapons: server::BOT_WEAPON_CHOICES,
        bot_rifles: server::BOT_RIFLE_CHOICES.iter().map(|c| c.0).collect(),
        retake_styles: server::RETAKE_STYLES,
        give: give_items()
            .into_iter()
            .map(|(c, n)| json!({ "classname": c, "name": n }))
            .collect(),
        port: PORT,
    }
}

// -------------------------------------------------------------------------------------------------
// The poll (every second): status, joining, Workshop start, crash report
// -------------------------------------------------------------------------------------------------

/// Once the server is up, CS2 itself must not load Metamod and the server plugins.
fn isolate_client(app: &App, game: &GameFolder, status: Option<&ServerStatus>) {
    if let Err(e) = (Session { game }).isolate_client(status) {
        app.log(format!("Could not update gameinfo.gi: {e}"));
    }
}

pub fn poll_server(app: &Arc<App>) {
    let mut st = app.state();
    let status = st.server_status();
    if let Some(g) = st.game.clone() {
        isolate_client(app, &g, status.as_ref());
    }
    announce_players(app, &mut st, status.as_ref().map(|s| &s.players));
    handle_workshop_startup(app, &mut st, status.as_ref());
    handle_join_when_up(app, &mut st, status.as_ref());
    check_server_crash(app, &mut st);
    let id = st.settings.server.workshop_map_id();
    let tick = json!({
        "status": status.as_ref().map(|s| json!([s.map, s.game_type, s.game_mode, s.humans, s.bots, s.players, s.skins_allowed, s.disabled_reason, s.stats])),
        "on": st.test_mode_on(), "step": join_step(&st, status.as_ref()), "w": st.play.pending_workshop_id, "s": st.play.workshop_sent_at.is_some(),
        "sub": st.game.as_ref().zip(id).is_some_and(|(g, id)| game::workshop_downloaded(g, &id)),
    })
    .to_string();
    let changed = tick != st.play.last_tick;
    st.play.last_tick = tick;
    drop(st);
    net::renew_if_needed();
    if changed {
        app.push("play.tick", Value::Null);
    }
}

/// A toast when a friend joins or leaves the running server (not for the first list after a start).
fn announce_players(app: &App, st: &mut State, players: Option<&Vec<String>>) {
    let Some(players) = players else {
        st.play.known_players = None;
        return;
    };
    if let Some(known) = &st.play.known_players {
        for name in players.iter().filter(|p| !known.contains(p)) {
            app.toast(format!("{name} joined the server"), "good");
        }
        for name in known.iter().filter(|p| !players.contains(p)) {
            app.toast(format!("{name} left the server"), "info");
        }
    }
    st.play.known_players = Some(players.clone());
}

fn set_join_when_up(st: &mut State, what: JoinWhenUp) {
    st.play.join_when_up = what;
    st.play.join_up_since = None;
    st.play.join_asked_at = (what != JoinWhenUp::None).then(util::unix_now);
}

fn clear_workshop_wait(st: &mut State) {
    let p = &mut st.play;
    (
        p.pending_workshop_id,
        p.bootstrap_map,
        p.server_up_since,
        p.workshop_sent_at,
    ) = (None, None, None, None);
}

/// Starts CS2 while the server loads, or reconnects it once the server reports it is up.
fn handle_join_when_up(app: &App, st: &mut State, status: Option<&ServerStatus>) {
    let Some(game) = st
        .game
        .clone()
        .filter(|_| st.play.join_when_up != JoinWhenUp::None)
    else {
        return;
    };
    let asked = st.play.join_asked_at.unwrap_or(0);
    if !st.test_mode_on() || util::unix_now() - asked > 180 {
        if st.test_mode_on() {
            app.log(format!("The server took too long to load - press \"Open CS2\" (or connect 127.0.0.1:{PORT})."));
        }
        set_join_when_up(st, JoinWhenUp::None);
        return;
    }
    let session = Session { game: &game };
    // Only a report written by the new server counts: the old one's is still fresh for ~8 s after a restart.
    let Some(status) = status.filter(|s| !s.map.is_empty() && s.time >= asked) else {
        st.play.join_up_since = None;
        if st.play.join_when_up == JoinWhenUp::StartGame {
            start_game_early(app, st, &session);
        }
        return;
    };
    let up = *st.play.join_up_since.get_or_insert_with(Instant::now);
    if st.play.join_when_up == JoinWhenUp::CheckJoined {
        if status.humans > 0 || !session::client_running() {
            set_join_when_up(st, JoinWhenUp::None);
        } else if up.elapsed() >= Duration::from_secs(30) {
            // CS2 gave up connecting before the map was up: send it again.
            set_join_when_up(st, JoinWhenUp::None);
            if let Err(e) = session::reconnect_client(&st.settings.server.password) {
                app.log(format!("Could not make CS2 join: {e}"));
            }
        }
        return;
    }
    if up.elapsed() < Duration::from_secs(2) {
        return; // let the map finish loading
    }
    let what = st.play.join_when_up;
    set_join_when_up(st, JoinWhenUp::None);
    let result = if what == JoinWhenUp::StartGame && !session::client_running() {
        session
            .start_game(&st.settings.server)
            .map(|_| app.log("Server is up - starting CS2 (VAC off)..."))
    } else if what == JoinWhenUp::Reconnect {
        session::reconnect_client(&st.settings.server.password)
            .map(|_| app.log("Server is up - CS2 rejoins."))
    } else {
        Ok(())
    };
    if let Err(e) = result {
        app.log(format!("Could not start CS2: {e}"));
    }
}

/// Starts CS2 once the server has read gameinfo.gi, without waiting for the map to load.
fn start_game_early(app: &App, st: &mut State, session: &Session) {
    // gameinfo.gi busy or not loaded yet: try again on the next tick.
    if session::client_running() || !matches!(session.isolate_client_early(), Ok(true)) {
        return;
    }
    set_join_when_up(st, JoinWhenUp::CheckJoined);
    match session.start_game(&st.settings.server) {
        Ok(()) => {
            app.log("Server is loading - starting CS2 (VAC off), it joins when the map is up...")
        }
        Err(e) => {
            set_join_when_up(st, JoinWhenUp::None);
            app.log(format!("Could not start CS2: {e}"));
        }
    }
}

fn handle_workshop_startup(app: &App, st: &mut State, status: Option<&ServerStatus>) {
    let (Some(id), Some(game)) = (st.play.pending_workshop_id.clone(), st.game.clone()) else {
        return;
    };
    if !st.test_mode_on() {
        clear_workshop_wait(st); // stopped meanwhile
        return;
    }
    let Some(sent) = st.play.workshop_sent_at else {
        if status.is_none() {
            st.play.server_up_since = None;
            return;
        }
        // Give it a few seconds on the first map to finish logging on to Steam.
        if st
            .play
            .server_up_since
            .get_or_insert_with(Instant::now)
            .elapsed()
            < Duration::from_secs(6)
        {
            return;
        }
        match server::send(&game, &format!("host_workshop_map {id}")) {
            Ok(()) => {
                st.play.workshop_sent_at = Some(Instant::now());
                app.log(format!("Server is up - loading Workshop map {id} (it downloads first, this can take a while)..."));
            }
            Err(e) => app.log(format!("Could not send the Workshop map command: {e}")),
        }
        return;
    };
    let bootstrap = st.play.bootstrap_map.clone().unwrap_or_default();
    if let Some(s) = status.filter(|s| !s.map.is_empty() && !s.map.eq_ignore_ascii_case(&bootstrap))
    {
        app.log(format!("Workshop map loaded: {}.", s.map));
        app.toast(
            format!(
                "{} loaded{}",
                s.map,
                if st.play.start_game_after_workshop {
                    " - starting CS2…"
                } else {
                    "."
                }
            ),
            "info",
        );
        if st.play.start_game_after_workshop {
            if let Err(e) = (Session { game: &game }).start_game(&st.settings.server) {
                app.log(format!("Could not start CS2: {e}"));
            }
        }
        clear_workshop_wait(st);
    } else if sent.elapsed() > Duration::from_secs(15 * 60) {
        clear_workshop_wait(st);
        app.log("The Workshop map still hasn't loaded after 15 minutes. Check the server window (is it downloading?), or press \"Open CS2\" to join the server on its first map.");
    }
}

/// The server we started is gone without Stop: say so and show its last console lines.
fn check_server_crash(app: &App, st: &mut State) {
    let Some(game) = st.game.clone() else { return };
    if session::STOPPED_ON_PURPOSE.load(Ordering::Relaxed) {
        return;
    }
    let Some(code) = session::server_exit() else {
        return;
    };
    let code = code.filter(|c| *c != 0).map_or(String::new(), |c| {
        format!(" (exit code 0x{:08X})", c as u32)
    });
    app.log(format!(
        "The server closed unexpectedly{code}. Its last console lines:"
    ));
    let tail = Session { game: &game }.console_tail(12);
    if tail.is_empty() {
        app.log("   (no console log - it closed before writing one)");
    }
    for line in tail {
        app.log(format!("   {line}"));
    }
    st.play.start_error = Some("The server closed unexpectedly - its last console lines are in Logs (Live Server). Copy Logs to ask for help.".into());
    app.toast(
        "The server closed unexpectedly - see Logs on Live Server.",
        "bad",
    );
    app.changed();
}

// -------------------------------------------------------------------------------------------------
// Start / apply / stop
// -------------------------------------------------------------------------------------------------

/// In "Internet" friends mode it first opens the port on the router (UPnP) and makes sure there's a password.
/// Any friends mode also counts as hosting friends.
pub fn start_server(app: &Arc<App>, a: AskArgs) -> R<()> {
    {
        let st = app.state();
        st.game()?;
        if st.play.busy {
            return Ok(());
        }
    }
    // Prunes skins you don't own before the plugin reads them (an equip may be saving: same gate).
    {
        let mut inv = app.inv();
        let st = app.state();
        super::cases::read_loadout(app, &mut inv, &st)?;
    }
    let friends = {
        let mut st = app.state();
        if st.test_mode_on() {
            return Err(bad(
                "The server is already running - press Apply Changes, or Stop first.",
            ));
        }
        if !st.ready() {
            return Err(bad("Install first (Setup page)."));
        }
        if let Some(id) = st.settings.server.workshop_map_id() {
            if super::workshop::try_play_offline(app, &st, &id)? {
                return Ok(());
            }
        }
        st.play.busy = true;
        st.settings.server.friends
    };
    app.changed();
    let for_friends = a.for_friends || friends != server::LAN;
    let result = (|| -> R<()> {
        if friends == server::INTERNET {
            {
                // Open to the whole internet: always use a password.
                let mut st = app.state();
                if st.settings.server.password.trim().is_empty() {
                    st.settings.server.password = util::random_hex(3).to_lowercase();
                    app.save(&st);
                    app.log(format!(
                        "Internet mode: set password \"{}\" (it's in the Friends page invite).",
                        st.settings.server.password
                    ));
                }
            }
            if !a.confirmed {
                if let Some(error) = net::open_port(&|l| app.log(l)) {
                    ask(app, "Couldn't open the port",
                        &format!("{error}\n\nStart the server anyway? Friends can only join if you forward UDP port {PORT} to this PC in your router yourself."),
                        "Start Anyway", "play.start", json!(AskArgs { confirmed: true, ..a.clone() }), false);
                    return Ok(());
                }
            }
        }
        prepare_plugins(app, false)?;
        start_test(app, for_friends)
    })();
    app.state().play.busy = false;
    app.changed();
    result
}

fn start_test(app: &App, for_friends: bool) -> R<()> {
    let mut st = app.state();
    let game = st.game()?.clone();
    let server = st.settings.server.clone();
    st.play.start_error = None;
    if super::setup::smart_app_control_on() {
        app.log("Windows Smart App Control is on: it can block Metamod (\"Bad Image\"). If the server fails to load plugins, see Setup.");
        app.toast(
            "Windows Smart App Control is on - if CS2 shows \"Bad Image\", see Setup.",
            "warning",
        );
    }
    let result = (|| -> Result<bool, String> {
        app.allow_skins_in_css(Some(&game));
        // A newer launcher carries a newer plugin: put it in before the server loads it.
        if !installer::plugin_matches_bundled(&game) {
            let log = |l: String| app.log(l);
            Installer {
                game: &game,
                http: &app.http,
                log: &log,
                progress: &|_| {},
            }
            .install_plugin()?;
            app.log(format!(
                "Updated the plugin to v{} (the one in this launcher).",
                installer::v(&installer::bundled_plugin_version())
            ));
        }
        Session { game: &game }.start(&server)
    })();
    let start_game = match result {
        Ok(start_game) => start_game,
        Err(e) => {
            app.log(format!("Could not start: {e}"));
            st.play.start_error = Some(e);
            // Nothing is running: put gameinfo.gi back now instead of waiting for the auto-restore.
            if st.test_mode_on() && !session::cs2_running() {
                if let Err(e) = (Session { game: &game }).stop() {
                    app.log(format!("Could not restore gameinfo.gi: {e}"));
                }
            }
            return Err(bad(
                "Could not start - the details are on the Play page and in the log.",
            ));
        }
    };
    set_join_when_up(
        &mut st,
        if start_game {
            JoinWhenUp::StartGame
        } else {
            JoinWhenUp::None
        },
    );
    clear_workshop_wait(&mut st);
    let id = server.workshop_map_id();
    if server.workshop_needs_download() {
        (
            st.play.pending_workshop_id,
            st.play.bootstrap_map,
            st.play.start_game_after_workshop,
        ) = (id.clone(), Some(server.map.clone()), server.auto_start_game);
    }
    let p = &mut st.play;
    p.running_mode = Some(server.mode.clone());
    p.running_map = Some(id.clone().unwrap_or_else(|| server.map.clone()));
    p.running_online = server.private_server();
    p.running_for_friends = for_friends;
    p.running_slots = server.needs_player_slots();
    p.running_retake_key = Some(server.plugin_retake_key());
    drop(st);
    let map = id.map_or(server.map.clone(), |id| format!("Workshop map {id}"));
    if server.workshop_needs_download() {
        app.log("Workshop map: the server goes online (unlisted) and loads it from the Workshop - the first time takes longer while it downloads. Players' games fetch it automatically when they join.");
    }
    let started_as = if server.is_plugin_retakes() {
        "Plugin retakes".to_string()
    } else {
        server.mode.clone()
    };
    app.log(format!(
        "Started {started_as} on {map} with {} {} bots.",
        server.bots,
        server::DIFFICULTIES[server.difficulty.clamp(0, 3) as usize].to_lowercase()
    ));
    app.toast(format!("Starting {started_as} on {map}…"), "info");
    if server.friends != server::LAN {
        app.log(format!(
            "Friends mode: {} (sv_lan 0, unlisted, VAC off).",
            if server.friends == server::VPN {
                "Radmin / VPN"
            } else {
                "internet (UPnP)"
            }
        ));
    }
    app.log(if !server.auto_start_game {
        "Server only - press \"Open CS2\" when you want to join.".to_string()
    } else if start_game {
        format!("CS2 starts while the server loads and joins when it is up. If it doesn't join: console (~) → connect 127.0.0.1:{PORT}")
    } else {
        "CS2 starts by itself as soon as the server has downloaded and loaded the Workshop map.".into()
    });
    Ok(())
}

pub fn apply_to_server(app: &Arc<App>, a: AskArgs) -> R<()> {
    let mut st = app.state();
    let game = st.game()?.clone();
    if !st.test_mode_on() {
        return Err(bad("The server isn't running - press Start."));
    }
    let server = st.settings.server.clone();
    let map = server.map_key();
    let reload = st.play.running_mode.as_ref() != Some(&server.mode)
        || st.play.running_map.as_ref() != Some(&map);
    let needs_slots = server.needs_player_slots() && !st.play.running_slots;
    if needs_slots || retakes_need_restart(&st, &game, &server) {
        if !a.confirmed {
            let why = if needs_slots {
                "Custom mode needs player slots that are set when the server starts, so the server needs a restart."
            } else if installer::retake_plugins_mismatch(&game, &server)
                || installer::extra_plugins_mismatch(&game, &server)
            {
                "A server plugin has to load or unload, so the server needs a restart."
            } else {
                "These retakes settings are read when the plugin loads, so the server needs a restart."
            };
            ask(
                app,
                "Restart the server?",
                why,
                "Restart",
                "play.apply",
                json!(AskArgs {
                    confirmed: true,
                    for_friends: false
                }),
                false,
            );
            return Ok(());
        }
        drop(st);
        app.log("Restarting so the server plugins match these settings.");
        return restart_server(app);
    }
    st.play
        .running_retake_key
        .get_or_insert_with(|| server.plugin_retake_key());
    match server.apply_to_running_server(&game, reload, st.play.running_online) {
        Err(e) => {
            app.log(format!("Could not apply: {e}"));
            Err(bad(format!("Could not apply: {e}")))
        }
        Ok(Some(warning)) => {
            app.log(warning);
            app.toast(
                "Workshop map not downloaded - see the activity log.",
                "warning",
            );
            Ok(())
        }
        Ok(None) => {
            (
                st.play.running_mode,
                st.play.running_map,
                st.play.running_retake_key,
            ) = (
                Some(server.mode.clone()),
                Some(map.clone()),
                Some(server.plugin_retake_key()),
            );
            drop(st);
            if reload {
                app.log(format!("Switching to {} on {map}...", server.mode));
                app.toast(format!("Switching to {} on {map}…", server.mode), "good");
            } else {
                app.log("Applied bots and options to the running server.");
                app.toast("Applied to the running server.", "good");
            }
            Ok(())
        }
    }
}

/// Restarts the server with the current settings. An open CS2 stays open and rejoins the new server; only when
/// the plugin files can't be changed while it runs is CS2 closed too.
fn restart_server(app: &Arc<App>) -> R<()> {
    let game = app.state().game()?.clone();
    if session::client_running() {
        session::stop_server_only();
        let restarted = prepare_plugins(app, true).and_then(|_| {
            let st = app.state();
            Session { game: &game }
                .restart_server(&st.settings.server)
                .map_err(ApiError::Fail)
        });
        match restarted {
            Ok(()) => {
                let mut st = app.state();
                let server = st.settings.server.clone();
                let p = &mut st.play;
                (
                    p.running_mode,
                    p.running_map,
                    p.running_slots,
                    p.running_retake_key,
                ) = (
                    Some(server.mode.clone()),
                    Some(server.map_key()),
                    server.needs_player_slots(),
                    Some(server.plugin_retake_key()),
                );
                // The new server boots on the normal map: switch it to the Workshop map once it's up.
                clear_workshop_wait(&mut st);
                if server.workshop_needs_download() {
                    (
                        st.play.pending_workshop_id,
                        st.play.bootstrap_map,
                        st.play.start_game_after_workshop,
                    ) = (server.workshop_map_id(), Some(server.map.clone()), false);
                }
                set_join_when_up(&mut st, JoinWhenUp::Reconnect);
                drop(st);
                app.log("Server restarting - CS2 stays open and rejoins when it is up.");
                app.toast("Restarting the server… CS2 rejoins in a moment.", "info");
                app.changed();
                return Ok(());
            }
            Err(ApiError::Fail(e)) => app.log(format!(
                "The game has the plugin files open, so it closes for the restart: {e}"
            )),
            Err(e) => return Err(e),
        }
    }
    (Session { game: &game }).stop()?;
    let for_friends = {
        let mut st = app.state();
        (st.play.running_mode, st.play.running_map) = (None, None);
        st.play.running_for_friends
    };
    app.changed();
    for _ in 0..20 {
        if !session::cs2_running() {
            break;
        }
        std::thread::sleep(Duration::from_millis(500));
    }
    start_server(
        app,
        AskArgs {
            confirmed: false,
            for_friends,
        },
    )
}

fn stop_test(app: &App, a: AskArgs) -> R<()> {
    let mut st = app.state();
    let Some(game) = st.game.clone().filter(|_| st.test_mode_on()) else {
        return Ok(());
    };
    if session::cs2_running() && !a.confirmed {
        ask(
            app,
            "Stop the skin test?",
            "This closes CS2 and the test server and puts CS2 back to normal.",
            "Stop",
            "play.stop",
            json!(AskArgs {
                confirmed: true,
                for_friends: false
            }),
            true,
        );
        return Ok(());
    }
    match (Session { game: &game }).stop() {
        Ok(()) => {
            forget_running(&mut st);
            st.play.start_error = None;
            let _ = std::fs::remove_file(game.plugin_dir().join(STATUS_FILE));
            app.log("Stopped. CS2 is back to normal - you can play online again.");
            app.toast("Stopped. CS2 is back to normal.", "good");
        }
        Err(e) => {
            app.log(format!("Could not restore gameinfo.gi: {e}"));
            app.log("   -> Steam > CS2 > Properties > Installed Files > Verify integrity also restores it.");
            app.toast("Could not restore gameinfo.gi - see the log.", "bad");
        }
    }
    drop(st);
    app.changed();
    Ok(())
}

fn forget_running(st: &mut State) {
    (
        st.play.running_mode,
        st.play.running_map,
        st.play.running_retake_key,
    ) = (None, None, None);
    clear_workshop_wait(st);
}

/// CS2 and the server were closed without Stop: restore quietly.
pub fn auto_restore(app: &App) -> std::io::Result<()> {
    let mut st = app.state();
    let Some(game) = st.game.clone() else {
        return Ok(());
    };
    (Session { game: &game }).stop()?;
    forget_running(&mut st);
    let _ = std::fs::remove_file(game.plugin_dir().join(STATUS_FILE));
    drop(st);
    app.log("CS2 was closed - restored gameinfo.gi automatically. You can play online again.");
    app.changed();
    Ok(())
}

/// The window closes (or the launcher updates) while test mode is on.
pub fn stop_from_close(app: &App) -> std::io::Result<()> {
    let game = app.state().game.clone();
    if let Some(game) = game {
        (Session { game: &game }).stop()?;
    }
    app.changed();
    Ok(())
}

// -------------------------------------------------------------------------------------------------
// Server plugins
// -------------------------------------------------------------------------------------------------

/// Downloads the server plugins the settings use the first time, then loads or parks each one. The retakes
/// plugin reads its JSON when it loads, so that is written before the server starts.
fn prepare_plugins(app: &App, game_may_run: bool) -> R<()> {
    let (game, server) = {
        let st = app.state();
        (st.game()?.clone(), st.settings.server.clone())
    };
    type Install = fn(&Installer) -> installer::R<()>;
    let mut wanted: Vec<(&str, fn(&GameFolder) -> bool, Install, &str)> = vec![];
    if server.is_plugin_retakes() {
        wanted.push((
            "the retakes plugin",
            installer::retakes_installed,
            |i| i.install_retakes(),
            "Check your internet connection, or switch Style to Valve.",
        ));
        if server.retake_instadefuse {
            wanted.push((
                "instadefuse",
                installer::instadefuse_installed,
                |i| i.install_instadefuse(),
                "Check your internet connection, or turn Instadefuse off.",
            ));
        }
        if server.uses_allocator() {
            wanted.push((
                "the retakes allocator",
                installer::allocator_installed,
                |i| i.install_allocator(),
                "Check your internet connection, or switch Guns to Buy.",
            ));
        }
    }
    // Bot Improver's package already has BotAI, Bot Controller and Bot Hider.
    if server.bot_improver {
        wanted.push(("Bot Improver", installer::bot_improver_installed, |i| i.install_bot_improver(), "Check your internet connection, or turn Bot Improver off (Live Server › Server Plugins)."));
    }
    if server.bot_controller && !server.bot_improver {
        wanted.push((
            "Bot Controller",
            installer::bot_controller_installed,
            |i| i.install_bot_controller(),
            "Check your internet connection, or turn Bot Controller off.",
        ));
    }
    if server.bot_hider && !server.bot_improver {
        wanted.push((
            "Bot Hider",
            installer::bot_hider_installed,
            |i| i.install_bot_hider(),
            "Check your internet connection, or turn Bot Hider off (Live Server › Server Plugins).",
        ));
    }
    if server.uses_deathmatch_plugin() {
        wanted.push((
            "the Deathmatch plugin",
            installer::deathmatch_installed,
            |i| i.install_deathmatch(),
            "Check your internet connection, or turn the Deathmatch plugin off.",
        ));
    }
    let log = |l: String| app.log(l);
    for (label, installed, install, hint) in wanted {
        if installed(&game) {
            continue;
        }
        if session::cs2_running() {
            return Err(bad(format!(
                "Close CS2 first, then start again. {label} installs into the game folder."
            )));
        }
        if let Err(e) = install(&Installer {
            game: &game,
            http: &app.http,
            log: &log,
            progress: &|_| {},
        }) {
            app.log(format!("Could not download {label}: {e}"));
            return Err(bad(format!("{label} could not be downloaded: {e}. {hint}")));
        }
    }
    // A running CS2 refuses to start anyway: leave its plugin folder alone until it is closed. (A server-only
    // restart changes the files with the game left open: the game doesn't load them.)
    if if game_may_run {
        session::find_server().is_some()
    } else {
        session::cs2_running()
    } {
        return Ok(());
    }
    let result = installer::set_extra_plugins(&game, &server)
        .and_then(|_| installer::set_retakes_suite(&game, &server))
        .and_then(|_| {
            if server.is_plugin_retakes() {
                server.write_retakes_config(&game)
            } else {
                Ok(())
            }
        });
    match result {
        Ok(()) => Ok(()),
        Err(e) if game_may_run => Err(ApiError::Fail(e.to_string())),
        Err(e) => {
            app.log(format!("Could not set up the retakes plugin: {e}"));
            Err(bad(format!("Could not set up the retakes plugin: {e}")))
        }
    }
}

/// True when the running server must restart: loading or unloading a plugin, or the retakes plugin's JSON
/// settings. A launcher that just opened onto an already-running server does not restart on its own.
fn retakes_need_restart(st: &State, game: &GameFolder, server: &ServerSettings) -> bool {
    let mismatch = installer::retake_plugins_mismatch(game, server)
        || installer::extra_plugins_mismatch(game, server);
    let running = st.play.running_retake_key.as_deref();
    if running.is_none() && !mismatch {
        return false;
    }
    let changed = running.is_some_and(|k| {
        k != server.plugin_retake_key()
            && (server.is_plugin_retakes() || (k != "valve" && k != "off"))
    });
    mismatch || changed
}

// -------------------------------------------------------------------------------------------------
// Live Server
// -------------------------------------------------------------------------------------------------

/// Changes some server settings: { "bots": 8, "mode": "Casual" }. Apply sends them to a running server.
fn set_server(app: &App, changes: Map<String, Value>) -> R<()> {
    let mut st = app.state();
    let old_mode = st.settings.server.mode.clone();
    let mut doc = serde_json::to_value(&st.settings.server).unwrap();
    for (key, value) in &changes {
        let field = doc
            .as_object()
            .unwrap()
            .keys()
            .find(|k| k.eq_ignore_ascii_case(key))
            .cloned()
            .ok_or_else(|| bad(format!("Unknown setting: {key}")))?;
        let value = if value.is_null() && doc[&field].is_string() {
            Value::String(String::new())
        } else {
            value.clone()
        };
        doc[&field] = value;
        serde_json::from_value::<ServerSettings>(doc.clone())
            .map_err(|_| bad(format!("Bad value for {key}")))?;
    }
    let mut s: ServerSettings = serde_json::from_value(doc).unwrap();
    let modes = server::modes();
    if !modes.iter().any(|m| m.name == s.mode) {
        s.mode = old_mode.clone();
    }
    if changes.contains_key("mode") {
        s.remember_map_mode();
    } else if changes.contains_key("workshopMap") {
        s.recall_map_mode();
    }
    if (changes.contains_key("teamSizeT") || changes.contains_key("teamSizeCT"))
        && s.map_team_sizes.contains_key(&s.map_key())
    {
        s.map_team_sizes
            .insert(s.map_key(), vec![s.team_size_t, s.team_size_ct]);
    }
    // A new mode keeps the map only when that mode has it.
    let maps = s.get_mode().maps;
    if s.mode != old_mode
        && !changes.contains_key("map")
        && !maps.is_empty()
        && !maps.contains(&s.map)
    {
        s.map = maps[0].clone();
    }
    s.map = if s.map.trim().is_empty() {
        maps.first().cloned().unwrap_or_default()
    } else {
        s.map.trim().to_string()
    };
    s.bots = s.bots.clamp(0, 20);
    s.difficulty = s.difficulty.clamp(0, 3);
    s.bot_team = s.bot_team.clamp(0, 2);
    s.bot_weapons = s
        .bot_weapons
        .clamp(0, server::BOT_WEAPON_CHOICES.len() as i32 - 1);
    s.bot_rifles = s
        .bot_rifles
        .clamp(0, server::BOT_RIFLE_CHOICES.len() as i32 - 1);
    s.bot_aim = s.bot_aim.clamp(0, server::BOT_AIM_MODES.len() as i32 - 1);
    s.bot_nades = s
        .bot_nades
        .clamp(0, server::BOT_NADE_MODES.len() as i32 - 1);
    if !server::BOT_FORCED_WEAPONS.contains(&s.bot_force_weapon.as_str()) {
        s.bot_force_weapon.clear();
    }
    if !server::ONLY_WEAPONS.iter().any(|w| w.0 == s.only_weapon) {
        s.only_weapon.clear();
    }
    s.round_time = s.round_time.clamp(0, 60);
    s.retake_style = (s.retake_style == 1) as i32;
    s.retake_max_players = s.retake_max_players.clamp(2, 10);
    s.retake_terrorist_percent = s.retake_terrorist_percent.clamp(10, 80);
    s.retake_scramble_rounds = s.retake_scramble_rounds.clamp(1, 30);
    s.retake_weapons = (s.retake_weapons == 1) as i32;
    s.retake_full_percent = s.retake_full_percent.clamp(0, 100);
    s.retake_force_percent = s.retake_force_percent.clamp(0, 100);
    s.retake_eco_percent = s.retake_eco_percent.clamp(0, 100);
    if s.retake_full_percent + s.retake_force_percent + s.retake_eco_percent == 0 {
        s.retake_full_percent = 100;
    }
    s.retake_buy_seconds = s.retake_buy_seconds.clamp(4, 20);
    s.retake_bomb_timer = s.retake_bomb_timer.clamp(15, 60);
    s.workshop_map = s.workshop_map.trim().to_string();
    s.game_args = s.game_args.trim().to_string();
    s.password = server::clean_password(&s.password);
    st.settings.server = s;
    app.save(&st);
    drop(st);
    app.changed();
    Ok(())
}

/// Any server console command (first line only).
fn send_command(app: &App, command: &str) -> R<()> {
    let command = command.replace('\r', "");
    let command = command.split('\n').next().unwrap_or("").trim();
    if command.is_empty() {
        return Ok(());
    }
    let st = app.state();
    let game = st.game()?.clone();
    if st.server_status().is_none() {
        return Err(bad("The server isn't running yet."));
    }
    drop(st);
    server::send(&game, command).map_err(|e| {
        app.log(format!("Could not send: {e}"));
        bad(format!("Could not send: {e}"))
    })?;
    app.log(format!("> {command}"));
    app.toast("Sent to the server.", "info");
    Ok(())
}

/// Flying Scoutsman on or off. Off resets what the mode's own config doesn't set and reloads the map.
fn set_scouts(app: &App, on: bool) -> R<()> {
    let mut st = app.state();
    let game = st.game()?.clone();
    if st.server_status().is_none() {
        return Err(bad("The server isn't running yet."));
    }
    let s = st.settings.server.clone();
    let fail = |e: std::io::Error| {
        app.log(format!("Could not send: {e}"));
        bad(format!("Could not send: {e}"))
    };
    s.write_cfg(&game).map_err(fail)?;
    if on {
        server::send(&game, &format!("exec {}", server::SCOUTS_ON_CFG)).map_err(fail)?;
        app.log("Flying Scoutsman on: scouts, low gravity, no spread. The match restarts.");
        app.toast("Flying Scoutsman on.", "good");
        return Ok(());
    }
    server::send(&game, &format!("exec {}", server::SCOUTS_OFF_CFG)).map_err(fail)?;
    if retakes_need_restart(&st, &game, &s) {
        app.log("Flying Scoutsman off. Press Apply Changes: the server restarts for your other changes.");
        app.toast("Flying Scoutsman off.", "info");
        return Ok(());
    }
    if let Some(warning) = s
        .apply_to_running_server(&game, true, st.play.running_online)
        .map_err(fail)?
    {
        app.log(warning);
        return Ok(());
    }
    let map = s.map_key();
    (
        st.play.running_mode,
        st.play.running_map,
        st.play.running_retake_key,
    ) = (
        Some(s.mode.clone()),
        Some(map.clone()),
        Some(s.plugin_retake_key()),
    );
    drop(st);
    app.log(format!(
        "Flying Scoutsman off: reloading {map} with your settings."
    ));
    app.toast("Flying Scoutsman off - reloading the map…", "good");
    Ok(())
}
