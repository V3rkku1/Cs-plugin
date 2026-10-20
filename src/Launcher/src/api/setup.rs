//! Setup page: install, update, repair and uninstall everything the skin test needs.

use super::{arg, bad, ok, unknown, App, State, R};
use crate::game::{self, GameFolder};
use crate::installer::{self, Installer};
use crate::itemdb::{self, ItemDatabase};
use crate::session;
use crate::util;
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::collections::{BTreeMap, HashMap};
use std::sync::Arc;

#[derive(Default)]
pub struct SetupState {
    busy: bool,
    latest_metamod: Option<String>,
    latest_css: Option<String>,
    /// Steps whose last run failed, until the next run.
    step_results: HashMap<String, (String, String)>,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct Step {
    id: String,
    title: String,
    state: String,
    detail: String,
    action: String,
    enabled: bool,
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct SetupView {
    folder: Option<String>,
    busy: bool,
    steps: Vec<Step>,
    has_game: bool,
    text_size: String,
    reduce_glass: bool,
    reduce_motion: bool,
    metamod_page: &'static str,
    css_page: &'static str,
    theme: BTreeMap<String, String>,
}

#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct AppearanceArgs {
    text_size: Option<String>,
    reduce_glass: Option<bool>,
    reduce_motion: Option<bool>,
}

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "setup.state" => ok(view(&app.state())),
        "setup.find" => {
            let mut st = app.state();
            app.set_game(&mut st, GameFolder::find(game::steam_path()));
            ok(())
        }
        "setup.browse" => {
            let Some(path) = app.pick(|d| d.set_title("Pick the \"Counter-Strike Global Offensive\" folder (Steam > CS2 > Manage > Browse local files)").pick_folder()) else {
                return ok(());
            };
            let path = path.display().to_string();
            if !GameFolder::is_valid(Some(&path)) {
                return Err(bad("That folder doesn't contain game\\csgo\\gameinfo.gi. Pick the \"Counter-Strike Global Offensive\" folder."));
            }
            let mut st = app.state();
            app.set_game(&mut st, Some(path));
            ok(())
        }
        "setup.step" => {
            let id: String = arg(a)?;
            match id.as_str() {
                "cssConfig" => {
                    let g = app.state().game.clone();
                    app.allow_skins_in_css(g.as_ref());
                    app.changed();
                }
                "sac" => super::open_external("windowsdefender:")?,
                "data" => run_setup(app, false, |app, _| {
                    step(app, "data", || download_data(app));
                })?,
                _ => run_setup(app, true, |app, i| {
                    step(app, &id, || step_work(app, i, &id));
                })?,
            }
            ok(())
        }
        "setup.all" => {
            run_setup(app, true, |app, i| {
                if !step(app, "metamod", || step_work(app, i, "metamod")) {
                    app.log(format!(
                        "   -> download Metamod from {} and use \"Install from zip...\"",
                        installer::METAMOD_PAGE
                    ));
                }
                if !step(app, "css", || step_work(app, i, "css")) {
                    app.log(format!("   -> download the \"with-runtime\" Windows zip from {} and use \"Install from zip...\"", installer::CSS_PAGE));
                }
                step(app, "plugin", || step_work(app, i, "plugin"));
                step(app, "cssConfig", || {
                    app.allow_skins_in_css(Some(i.game));
                    Ok(())
                });
                step(app, "data", || download_data(app));
                app.log("All done. Go to PLAY and press START.");
            })?;
            ok(())
        }
        "setup.checkUpdates" => {
            app.log("Checking for updates...");
            let game = app
                .state()
                .game
                .clone()
                .unwrap_or_else(|| GameFolder::new(""));
            let log = |l: String| app.log(l);
            let (metamod, css) = Installer {
                game: &game,
                http: &app.http,
                log: &log,
                progress: &|_| {},
            }
            .latest_versions();
            app.log(format!(
                "Latest Metamod: {} · latest CounterStrikeSharp: {}",
                metamod.as_deref().unwrap_or("unknown"),
                css.as_deref().unwrap_or("unknown")
            ));
            let mut st = app.state();
            (st.setup.latest_metamod, st.setup.latest_css) = (metamod, css);
            drop(st);
            app.changed();
            ok(())
        }
        "setup.zip" => {
            let Some(file) = app.pick(|d| {
                d.set_title("Pick a Metamod or CounterStrikeSharp zip")
                    .add_filter("Zip files", &["zip"])
                    .pick_file()
            }) else {
                return ok(());
            };
            run_setup(app, true, move |app, i| match i.install_zip(&file) {
                Ok(()) => app.allow_skins_in_css(Some(i.game)),
                Err(e) => app.log(format!("FAILED: {e}")),
            })?;
            ok(())
        }
        "setup.repair" => {
            run_setup(app, true, |app, i| {
                app.log("Repairing...");
                if !app.state().test_mode_on() {
                    let _ = game::disable_metamod(&i.game.game_info()); // leftover line from a crash
                }
                step(app, "plugin", || step_work(app, i, "plugin"));
                // Download the add-ons the current settings use again (a damaged or half-updated copy is replaced).
                let server = app.state().settings.server.clone();
                let mut addons: Vec<(&str, Box<dyn Fn() -> installer::R<()> + '_>)> = vec![];
                if server.is_plugin_retakes() {
                    addons.push(("retakes plugin", Box::new(|| i.install_retakes())));
                    addons.push(("instadefuse", Box::new(|| i.install_instadefuse())));
                }
                if server.uses_allocator() {
                    addons.push(("retakes allocator", Box::new(|| i.install_allocator())));
                }
                if server.bot_improver {
                    addons.push(("Bot Improver", Box::new(|| i.install_bot_improver())));
                }
                for (name, install) in addons {
                    match install() {
                        Ok(()) => app.log(format!("Reinstalled {name}.")),
                        Err(e) => app.log(format!("Could not reinstall {name}: {e}")),
                    }
                }
                app.allow_skins_in_css(Some(i.game));
                let db = app.state().db.clone();
                if !db.is_empty() {
                    let _ = db.write_plugin_skin_names(&i.game.skin_names_dir());
                }
                for leftover in [
                    "server_commands.txt",
                    "server_commands.txt.tmp",
                    "server_commands.txt.run",
                    "loadouts.json.tmp",
                ] {
                    let _ = std::fs::remove_file(i.game.plugin_dir().join(leftover));
                }
                app.log("Repair finished.");
            })?;
            ok(())
        }
        "setup.uninstall" => {
            run_setup(app, true, |app, i| {
                if let Err(e) = i.uninstall_all(&util::data_dir().join("backups")) {
                    app.log(format!("FAILED: {e}"));
                }
            })?;
            ok(())
        }
        "setup.appearance" => {
            let a: AppearanceArgs = arg(a)?;
            let mut st = app.state();
            if let Some(size) = a
                .text_size
                .filter(|s| ["small", "normal", "large"].contains(&s.as_str()))
            {
                st.settings.text_size = size;
            }
            if let Some(glass) = a.reduce_glass {
                st.settings.reduce_glass = glass;
            }
            if let Some(motion) = a.reduce_motion {
                st.settings.reduce_motion = motion;
            }
            app.save(&st);
            drop(st);
            app.changed();
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

/// Windows 11 Smart App Control enforcing: it blocks unsigned DLLs like Metamod's ("Bad Image", 0xc0e90002).
pub fn smart_app_control_on() -> bool {
    #[cfg(windows)]
    {
        winreg::RegKey::predef(winreg::enums::HKEY_LOCAL_MACHINE)
            .open_subkey(r"SYSTEM\CurrentControlSet\Control\CI\Policy")
            .and_then(|k| k.get_value::<u32, _>("VerifiedAndReputablePolicyState"))
            .is_ok_and(|v| v == 1)
    }
    #[cfg(not(windows))]
    false
}

fn versions(installed: &Option<String>, latest: &Option<String>) -> String {
    let have = installed
        .as_ref()
        .map_or("Installed".into(), |i| format!("Installed: {i}"));
    latest
        .as_ref()
        .map_or(have.clone(), |l| format!("{have}   ·   Latest: {l}"))
}

/// Version strings from different sources don't line up exactly: only flag an update when the installed one
/// clearly doesn't contain the latest build number.
fn newer(installed: &Option<String>, latest: &Option<String>) -> &'static str {
    let (Some(i), Some(l)) = (installed, latest) else {
        return "ok";
    };
    let build = Regex::new(r"(\d+)$")
        .unwrap()
        .find(l)
        .map_or("", |m| m.as_str())
        .to_string();
    if !build.is_empty() && !i.contains(&build) {
        "warning"
    } else {
        "ok"
    }
}

fn view(st: &State) -> SetupView {
    let mut steps = vec![];
    let mut add =
        |id: &str, title: &str, state: &str, detail: String, action: &str, enabled: bool| {
            let (state, detail) = st
                .setup
                .step_results
                .get(id)
                .cloned()
                .unwrap_or((state.into(), detail));
            steps.push(Step {
                id: id.into(),
                title: title.into(),
                state,
                detail,
                action: action.into(),
                enabled: enabled && !st.setup.busy,
            });
        };
    match &st.game {
        None => {
            let first = || "Pick your CS2 folder first".to_string();
            add(
                "metamod",
                "Metamod:Source 2.x",
                "missing",
                first(),
                "Install",
                false,
            );
            add(
                "css",
                "CounterStrikeSharp",
                "missing",
                first(),
                "Install",
                false,
            );
            add(
                "plugin",
                "LAN Skin Tester plugin",
                "missing",
                first(),
                "Install",
                false,
            );
            add(
                "cssConfig",
                "Skins allowed in CounterStrikeSharp",
                "missing",
                first(),
                "Fix",
                false,
            );
        }
        Some(g) => {
            let (mm, latest_mm) = (
                installer::installed_metamod_version(g),
                &st.setup.latest_metamod,
            );
            let has_mm = g.metamod_installed();
            add(
                "metamod",
                "Metamod:Source 2.x",
                if has_mm {
                    newer(&mm, latest_mm)
                } else {
                    "missing"
                },
                if has_mm {
                    versions(&mm, latest_mm)
                } else {
                    "Not installed - loads plugins into the server".into()
                },
                if has_mm { "Update" } else { "Install" },
                true,
            );

            let (css, latest_css) = (installer::installed_css_version(g), &st.setup.latest_css);
            let has_css = g.css_installed();
            add(
                "css",
                "CounterStrikeSharp",
                if has_css {
                    newer(&css, latest_css)
                } else {
                    "missing"
                },
                if has_css {
                    versions(&css, latest_css)
                } else {
                    "Not installed - runs C# plugins".into()
                },
                if has_css { "Update" } else { "Install" },
                true,
            );

            let installed = installer::installed_plugin_version(g);
            let bundled = installer::bundled_plugin_version();
            let old = matches!((&installed, &bundled), (Some(i), Some(b)) if i < b);
            let v = installer::v;
            add(
                "plugin",
                "LAN Skin Tester plugin",
                if installed.is_none() {
                    "missing"
                } else if old {
                    "warning"
                } else {
                    "ok"
                },
                if installed.is_none() {
                    format!("Not installed (this launcher has v{})", v(&bundled))
                } else if old {
                    format!(
                        "v{} installed - v{} available, press Update",
                        v(&installed),
                        v(&bundled)
                    )
                } else {
                    format!("v{} - up to date", v(&installed))
                },
                if installed.is_none() {
                    "Install"
                } else {
                    "Update"
                },
                true,
            );

            let blocks = has_css && game::css_blocks_skins(g);
            add(
                "cssConfig",
                "Skins allowed in CounterStrikeSharp",
                if !has_css {
                    "missing"
                } else if blocks {
                    "warning"
                } else {
                    "ok"
                },
                if !has_css {
                    "Needs CounterStrikeSharp".into()
                } else if blocks {
                    "FollowCS2ServerGuidelines is on - only the knife model would change. Press Fix.".into()
                } else {
                    "FollowCS2ServerGuidelines is off".into()
                },
                "Fix",
                blocks,
            );
        }
    }
    if smart_app_control_on() {
        add("sac", "Windows Smart App Control", "warning",
            "On - it can block Metamod's unsigned files (\"Bad Image\", 0xc0e90002) when it doesn't know them. Turn it off in Windows Security › App & browser control › Smart App Control (Windows can't turn it back on without a reset).".into(),
            "Open", true);
    }
    let db = &st.db;
    let n = |x: usize| {
        x.to_string()
            .as_bytes()
            .rchunks(3)
            .rev()
            .map(|c| String::from_utf8_lossy(c).into_owned())
            .collect::<Vec<_>>()
            .join(",")
    };
    add(
        "data",
        "Skin, sticker and charm lists",
        if db.is_empty() { "missing" } else { "ok" },
        if db.is_empty() {
            "Not downloaded - needed for skin pictures, names, stickers and charms".into()
        } else {
            format!(
                "{} skins · {} stickers · {} charms",
                n(db.skins.len()),
                n(db.stickers.len()),
                db.charms.len()
            )
        },
        if db.is_empty() { "Download" } else { "Update" },
        true,
    );

    let s = &st.settings;
    SetupView {
        folder: st.game.as_ref().map(|g| g.root.display().to_string()),
        busy: st.setup.busy,
        steps,
        has_game: st.game.is_some(),
        text_size: s.text_size.clone(),
        reduce_glass: s.reduce_glass,
        reduce_motion: s.reduce_motion,
        metamod_page: installer::METAMOD_PAGE,
        css_page: installer::CSS_PAGE,
        theme: s.theme.clone(),
    }
}

/// Runs install work with the page's rows locked; refuses while CS2 runs (files are in use).
fn run_setup(app: &Arc<App>, needs_game: bool, work: impl FnOnce(&Arc<App>, &Installer)) -> R<()> {
    let game = {
        let mut st = app.state();
        if st.setup.busy {
            return Ok(());
        }
        if needs_game && st.game.is_none() {
            return Err(bad("Pick your CS2 folder first."));
        }
        if st.game.is_some() && session::cs2_running() {
            return Err(bad(
                "Close CS2 and the test server first (files are in use while they run).",
            ));
        }
        st.setup.busy = true;
        st.setup.step_results.clear();
        st.game.clone().unwrap_or_else(|| GameFolder::new(""))
    };
    app.changed();
    let log = |l: String| app.log(l);
    let progress = |_p: f64| {};
    work(
        app,
        &Installer {
            game: &game,
            http: &app.http,
            log: &log,
            progress: &progress,
        },
    );
    app.state().setup.busy = false;
    app.changed();
    Ok(())
}

/// Runs one install step; logs and marks failures on its row.
fn step(app: &App, id: &str, action: impl FnOnce() -> installer::R<()>) -> bool {
    app.state().setup.step_results.remove(id);
    app.push("setup.progress", json!({ "id": id, "p": -1.0 }));
    let result = action();
    app.push("setup.progress", json!({ "id": id, "p": null }));
    match result {
        Ok(()) => true,
        Err(e) => {
            app.log(format!("FAILED: {e}"));
            app.state()
                .setup
                .step_results
                .insert(id.into(), ("failed".into(), format!("Failed: {e}")));
            false
        }
    }
}

fn step_work(app: &App, i: &Installer, id: &str) -> installer::R<()> {
    let progress = |p: f64| app.push("setup.progress", json!({ "id": id, "p": p }));
    let i = Installer {
        progress: &progress,
        ..*i
    };
    match id {
        "metamod" => i.install_metamod(),
        "css" => {
            i.install_css()?;
            app.allow_skins_in_css(Some(i.game));
            Ok(())
        }
        "plugin" => i.install_plugin(),
        _ => Err("Unknown step".into()),
    }
}

fn download_data(app: &App) -> installer::R<()> {
    ItemDatabase::download(&app.http, &itemdb::data_dir(), &|l| app.log(l))?;
    app.reload_db();
    Ok(())
}
