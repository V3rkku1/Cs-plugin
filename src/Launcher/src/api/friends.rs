//! Friends page: host (connection, address, firewall, skins, password, invite), join a friend, and the friends
//! list (saved friends with an online check, friends hosting on this network).

use super::{arg, bad, ok, plural, unknown, App, State, R};
use crate::game::GameFolder;
use crate::net::{self, ServerInfo};
use crate::server::{self, PORT};
use crate::session::{self, Session};
use crate::settings::{self, FriendContact, JoinTarget};
use crate::{console, util};
use serde::Deserialize;
use serde_json::{json, Value};
use std::collections::HashMap;
use std::sync::Arc;
use std::time::{Duration, Instant};

#[derive(Default)]
pub struct FriendsState {
    firewall_ok: Option<bool>,
    firewall_checked_at: Option<Instant>,
    /// Saved friends' servers, asked at most every 5 seconds.
    infos: HashMap<String, (Instant, Option<ServerInfo>)>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct SetArgs {
    network: Option<i32>,
    shared_skins: Option<bool>,
    password: Option<String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct CopyArgs {
    what: String,
    address: Option<String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct JoinArgs {
    text: Option<String>,
    password: Option<String>,
    recent: Option<usize>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct AddArgs {
    name: Option<String>,
    text: Option<String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct JoinLanArgs {
    address: String,
    password: Option<String>,
}

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "friends.state" => state(app),
        "friends.set" => {
            let a: SetArgs = arg(a)?;
            let mut st = app.state();
            if let Some(n) = a
                .network
                .filter(|n| (0..=2).contains(n) && *n != st.settings.server.friends)
            {
                st.settings.server.friends = n;
                app.save(&st);
                net::forget_search(); // look again when Internet is picked
                if st.test_mode_on() {
                    app.log("Connection type changes on the next Start (press Stop, then Start Hosting).");
                }
                app.changed();
                if n != server::INTERNET && net::ROUTER.lock().unwrap().port_open() {
                    drop(st);
                    net::close_port(&|l| app.log(l));
                    st = app.state();
                }
            }
            if let Some(shared) = a
                .shared_skins
                .filter(|s| *s != st.settings.server.shared_skins)
            {
                st.settings.server.shared_skins = shared;
                app.save(&st);
                send_if_running(app, &st, &format!("lst_shared_loadout {}", shared as i32));
                app.changed();
            }
            if let Some(p) = a.password {
                let password = server::clean_password(&p);
                if password != st.settings.server.password {
                    st.settings.server.password = password.clone();
                    app.save(&st);
                    send_if_running(app, &st, &format!("sv_password \"{password}\""));
                    app.changed();
                }
            }
            ok(())
        }
        "friends.findRouter" => {
            net::find_router(&|l| app.log(l));
            ok(())
        }
        "friends.firewall" => {
            let game = app.state().game()?.clone();
            if net::allow_through_firewall(&game.exe()) {
                app.log("Windows Firewall: CS2 allowed (inbound).");
                app.toast("CS2 is allowed through Windows Firewall.", "good");
            } else {
                app.log("Firewall rule not added (admin prompt declined?). Windows also asks the first time the server starts - tick Private and Public.");
                app.toast(
                    "Firewall rule not added - Windows asks again when the server starts.",
                    "warning",
                );
            }
            app.state().friends.firewall_ok = None;
            app.changed();
            ok(())
        }
        "friends.host" => super::play::start_server(
            app,
            super::play::AskArgs {
                confirmed: false,
                for_friends: true,
            },
        )
        .map(|_| Value::Null),
        "friends.copy" => {
            let a: CopyArgs = arg(a)?;
            let s = app.state().settings.server.clone();
            let address = a
                .address
                .map(|x| x.trim().to_string())
                .filter(|x| !x.is_empty())
                .or_else(|| net::host_address(&s))
                .ok_or_else(|| {
                    bad(if s.friends == server::INTERNET {
                        "No internet address yet - the router has to answer first (Friends page)."
                    } else {
                        "No address found for this connection type - see the Friends page."
                    })
                })?;
            let (text, message) = match a.what.as_str() {
                "code" => (
                    net::invite_code(&address, &s.password),
                    "Code copied - your friend pastes it in Join a Friend.",
                ),
                "message" => (
                    net::invite_text(&s, &address),
                    "Invite copied - paste it to your friend.",
                ),
                _ => (
                    if address.contains(':') {
                        address.clone()
                    } else {
                        format!("{address}:{PORT}")
                    },
                    "Address copied - send it to your friend.",
                ),
            };
            if !app.copy(&text) {
                app.log("Could not copy: the clipboard is busy.");
                return Err(bad("Could not copy: the clipboard is busy."));
            }
            app.log(message);
            app.toast(message, "good");
            ok(())
        }
        "friends.join" => {
            let a: JoinArgs = arg(a)?;
            let target = {
                let st = app.state();
                st.game()?;
                match a.recent {
                    Some(i) => st.settings.recent_servers.get(i).cloned(),
                    None => net::parse_invite(
                        a.text.as_deref().unwrap_or(""),
                        a.password.as_deref().unwrap_or(""),
                    ),
                }
            };
            let target = target.ok_or_else(|| {
                bad("Paste the invite code your friend sent (LST-…), or type their address.")
            })?;
            join_server(app, target).map(|_| Value::Null)
        }
        "friends.forget" => {
            let index: usize = arg(a)?;
            let mut st = app.state();
            if index < st.settings.recent_servers.len() {
                st.settings.recent_servers.remove(index);
                app.save(&st);
                drop(st);
                app.changed();
            }
            ok(())
        }
        "friends.list" => list(app),
        "friends.add" => {
            let a: AddArgs = arg(a)?;
            let target = net::parse_invite(a.text.as_deref().unwrap_or(""), "")
                .ok_or_else(|| bad("Paste the friend's invite code (LST-…) or address."))?;
            let name = a.name.unwrap_or_default().trim().to_string();
            let name = if name.is_empty() {
                target.address.clone()
            } else {
                name
            };
            let mut st = app.state();
            let list = &mut st.settings.friends_list;
            list.retain(|f| f.address != target.address);
            list.push(FriendContact {
                name: name.chars().take(32).collect(),
                address: target.address,
                password: target.password,
            });
            app.save(&st);
            drop(st);
            app.toast(format!("{name} added to your friends."), "good");
            app.changed();
            ok(())
        }
        "friends.remove" => {
            let index: usize = arg(a)?;
            let mut st = app.state();
            if index < st.settings.friends_list.len() {
                st.settings.friends_list.remove(index);
                app.save(&st);
                drop(st);
                app.changed();
            }
            ok(())
        }
        "friends.joinSaved" => {
            let index: usize = arg(a)?;
            let f = {
                let st = app.state();
                st.game()
                    .map_err(|_| bad("Pick your CS2 folder first (Setup page)."))?;
                st.settings
                    .friends_list
                    .get(index)
                    .cloned()
                    .ok_or_else(|| bad("That friend isn't in the list any more."))?
            };
            join_server(
                app,
                JoinTarget {
                    address: f.address,
                    password: f.password,
                },
            )
            .map(|_| Value::Null)
        }
        "friends.joinLan" => {
            let a: JoinLanArgs = arg(a)?;
            app.state()
                .game()
                .map_err(|_| bad("Pick your CS2 folder first (Setup page)."))?;
            if !net::lan_hosts().iter().any(|h| h.address == a.address) {
                return Err(bad("That server isn't announcing any more."));
            }
            join_server(
                app,
                JoinTarget {
                    address: a.address,
                    password: a.password.unwrap_or_default().trim().into(),
                },
            )
            .map(|_| Value::Null)
        }
        "friends.hostName" => {
            let name: String = arg(a)?;
            let name = name.trim();
            let mut st = app.state();
            st.settings.host_name = if name.is_empty() {
                settings::user_name()
            } else {
                name.chars().take(32).collect()
            };
            app.save(&st);
            drop(st);
            app.changed();
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

pub fn send_if_running(app: &App, st: &State, command: &str) {
    let Some(g) = st.game.as_ref().filter(|_| st.test_mode_on()) else {
        return;
    };
    if let Err(e) = server::send(g, command) {
        app.log(format!("Could not send: {e}"));
    }
}

fn state(app: &App) -> R<Value> {
    let s = app.state().settings.server.clone();
    let (internet, vpn) = (s.friends == server::INTERNET, s.friends == server::VPN);
    let (router, external, searching, checked, open) = {
        let r = net::ROUTER.lock().unwrap();
        (
            r.router.clone(),
            r.external,
            r.searching,
            r.checked,
            r.port_open(),
        )
    };
    let addresses: Vec<net::LocalAddress> = if internet {
        if !checked && !searching {
            let log_app = app.handle.clone();
            std::thread::spawn(move || {
                net::find_router(&|l| crate::with_app(&log_app, |a| a.log(l)))
            });
        }
        external
            .map(|e| net::LocalAddress {
                address: e,
                kind: "Internet",
                adapter: router.as_ref().map_or("router".into(), |r| r.name.clone()),
            })
            .into_iter()
            .collect()
    } else {
        let all = net::addresses();
        let matching: Vec<_> = all.iter().filter(|a| a.is_vpn() == vpn).cloned().collect();
        if matching.is_empty() {
            all
        } else {
            matching
        }
    };
    // What the first address (the one shown) means for this connection type.
    let selected = addresses.first();
    let (note, kind) = if internet {
        match (&router, external) {
            _ if searching => ("Looking for your router…".to_string(), "muted"),
            (None, _) => ("No UPnP router - turn UPnP on in your router, or use Radmin".into(), "warning"),
            (Some(r), None) => (format!("{} didn't say its internet address", r.name), "warning"),
            (Some(_), Some(e)) if net::behind_another_nat(e) => ("Your provider shares one address between homes (CGNAT): friends can't reach you - use Radmin".into(), "warning"),
            (Some(r), Some(_)) if open => (format!("Port {PORT} is open on {}", r.name), "good"),
            (Some(r), Some(_)) => (format!("{} · the port opens when you start hosting", r.name), "muted"),
        }
    } else {
        match selected {
            None => (
                if vpn {
                    "No Radmin address yet - open Radmin VPN and join a network"
                } else {
                    "No network found - connect to Wi-Fi"
                }
                .to_string(),
                "warning",
            ),
            Some(a) if a.is_vpn() != vpn => (
                if vpn {
                    "No Radmin address yet - open Radmin VPN and join a network with your friend"
                        .into()
                } else {
                    format!("Only a {} address found - switch to Radmin above", a.kind)
                },
                "warning",
            ),
            Some(a) => (format!("{} · {}", a.kind, a.adapter), "muted"),
        }
    };
    // netsh is slow: check the firewall rule at most every 30 seconds.
    let firewall_due = {
        let st = app.state();
        st.friends.firewall_ok.is_none()
            || st
                .friends
                .firewall_checked_at
                .is_none_or(|t| t.elapsed() > Duration::from_secs(30))
    };
    if firewall_due {
        let okay = net::firewall_rule_exists();
        let mut st = app.state();
        (st.friends.firewall_ok, st.friends.firewall_checked_at) =
            (Some(okay), Some(Instant::now()));
    }
    let st = app.state();
    let status = st.server_status();
    let test = st.test_mode_on();
    let (title, detail, host_kind) = match &status {
        None => (
            if test {
                "Server starting…"
            } else if st.ready() {
                "Not hosting"
            } else {
                "Install first (Setup page) to host"
            }
            .to_string(),
            "Uses the mode, map and bots from Live Server.".to_string(),
            if test { "busy" } else { "off" },
        ),
        Some(x) => (
            format!("Hosting {} · {}", x.map, plural(x.humans, "player")),
            if !x.skins_allowed {
                x.disabled_reason.clone()
            } else if !x.players.is_empty() {
                x.players.join(", ")
            } else {
                "Waiting for players…".into()
            },
            if !x.skins_allowed {
                "warning"
            } else if x.humans > 1 {
                "good"
            } else {
                "running"
            },
        ),
    };
    ok(json!({
        "network": s.friends, "sharedSkins": s.shared_skins, "password": s.password,
        "addresses": addresses.iter().map(|a| json!({ "address": a.address.to_string(), "kind": a.kind, "adapter": a.adapter })).collect::<Vec<_>>(),
        "addressNote": note, "addressKind": kind, "searching": searching, "canRetry": internet && selected.is_none() && !searching,
        "firewallOk": st.friends.firewall_ok == Some(true), "hasGame": st.game.is_some(), "ready": st.ready(), "testMode": test, "busy": st.play.busy,
        "hostTitle": title, "hostDetail": detail, "hostKind": host_kind, "players": status.map(|x| x.players).unwrap_or_default(),
        "recent": util::web(&st.settings.recent_servers), "port": PORT,
    }))
}

fn join_server(app: &Arc<App>, target: JoinTarget) -> R<()> {
    let st = app.state();
    let game = st
        .game()
        .map_err(|_| bad("Pick your CS2 folder first (Setup page)."))?
        .clone();
    let result = if session::cs2_running() {
        // No restart: Steam hands the connect link to the open game.
        open::that_detached(net::steam_connect_url(&target)).map_err(|e| e.to_string()).map(|_| {
            app.log(format!("Joining {} in your open CS2... If it says the server is insecure, close CS2 and press Join again (then it restarts with VAC off for this game).", target.address));
        })
    } else {
        Session { game: &game }.join(&st.settings.server, &target.address, &target.password).map(|_| {
            app.log(format!("Joining {} (VAC off for this game). If it doesn't connect: console (~) → connect {}", target.address, target.address));
        })
    };
    drop(st);
    if let Err(e) = result {
        app.log(format!("Could not join: {e}"));
        return Err(bad(format!("Could not join: {e}")));
    }
    app.toast(format!("Joining {}…", target.address), "info");
    {
        let mut st = app.state();
        let list = &mut st.settings.recent_servers;
        list.retain(|r| r.address != target.address);
        list.insert(0, target);
        list.truncate(4);
        app.save(&st);
    }
    app.changed();
    send_my_skins(app, game);
    Ok(())
}

/// Your own skins on the friend's server (their plugin wears them for you); see `console`.
fn send_my_skins(app: &Arc<App>, game: GameFolder) {
    let cfg = game.cfg_dir().join(format!("{}.cfg", console::SKINS_CFG));
    // The cfg's text (None: nothing to send). Same gate as the inventory calls: it reads (and may prune) the loadout.
    let refresh = {
        let (app, game, cfg) = (app.clone(), game.clone(), cfg.clone());
        move || -> Option<String> {
            let mut inv = app.inv();
            let st = app.state();
            let l = super::cases::read_loadout(&app, &mut inv, &st).ok()?;
            let empty = l.skins.len() + l.skins_t.len() + l.skins_ct.len() == 0
                && [
                    l.knife,
                    l.knife_t,
                    l.knife_ct,
                    l.gloves,
                    l.gloves_t,
                    l.gloves_ct,
                    l.pin,
                    l.music_kit,
                ]
                .iter()
                .all(|x| *x == 0)
                && l.agent_t.is_none()
                && l.agent_ct.is_none();
            if empty || !console::write_skins_cfg(&game, &l).unwrap_or(false) {
                let _ = std::fs::remove_file(&cfg);
            }
            std::fs::read_to_string(&cfg).ok()
        }
    };
    refresh();
    let started = Instant::now();
    let log_app = app.clone();
    // Until CS2 closes (it may take a while to start), and not while you host your own server.
    console::start_sending_skins(
        move || {
            (session::cs2_running() || started.elapsed() < Duration::from_secs(90))
                && session::find_server().is_none()
        },
        refresh,
        move |line| log_app.log(line),
    );
    app.log(format!("Your skins go along to the friend's server (their launcher must be up to date). If they don't show: console (~) → exec {}", console::SKINS_CFG));
}

fn list(app: &App) -> R<Value> {
    let saved = app.state().settings.friends_list.clone();
    // Each friend who isn't hosting costs a moment of waiting: ask them all at once, at most every 5 seconds each.
    let infos: Vec<Option<ServerInfo>> = std::thread::scope(|scope| {
        let handles: Vec<_> = saved
            .iter()
            .map(|f| scope.spawn(|| query_friend(app, &f.address)))
            .collect();
        handles
            .into_iter()
            .map(|h| h.join().unwrap_or(None))
            .collect()
    });
    let mut rows: Vec<(bool, String, Value)> = saved.iter().enumerate().map(|(i, f)| {
        let (online, detail, password) = match &infos[i] {
            Some(x) => (true, format!("Hosting {} · {}/{} players{} · {} ms", x.map, x.players, x.max_players, if x.bots > 0 { format!(" + {} bots", x.bots) } else { String::new() }, x.ping), x.password),
            None => (false, "Not hosting right now".into(), false),
        };
        (online, f.name.clone(), json!({ "index": i, "name": f.name, "address": f.address, "online": online, "detail": detail, "password": password }))
    }).collect();
    rows.sort_by(|a, b| b.0.cmp(&a.0).then_with(|| a.1.cmp(&b.1)));
    let host_name = app.state().settings.host_name.clone();
    ok(
        json!({ "friends": rows.into_iter().map(|r| r.2).collect::<Vec<_>>(), "lan": net::lan_hosts(), "hostName": host_name }),
    )
}

fn query_friend(app: &App, address: &str) -> Option<ServerInfo> {
    if let Some((at, info)) = app.state().friends.infos.get(address) {
        if at.elapsed() < Duration::from_secs(5) {
            return info.clone();
        }
    }
    let info = net::query(address);
    app.state()
        .friends
        .infos
        .insert(address.into(), (Instant::now(), info.clone()));
    info
}

/// What this PC announces to friends' launchers on the same network while it hosts.
pub fn beacon(app: &App) -> Option<net::BeaconInfo> {
    let st = app.state();
    if !st.test_mode_on() {
        return None;
    }
    let status = st.server_status()?;
    Some((
        st.settings.host_name.clone(),
        status.map,
        status.humans,
        if status.max_players > 0 {
            status.max_players
        } else {
            10
        },
        !st.settings.server.password.is_empty(),
    ))
}
