//! The window around the pages: status, navigation, links, launcher updates, auto-restore, closing.

use super::{arg, bad, ok, open_external, unknown, App, State, R};
use crate::game::{self, GameFolder};
use crate::session;
use crate::update::{self, Update};
use crate::util;
use serde::Serialize;
use serde_json::Value;
use std::fs;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::time::Duration;

#[derive(Default)]
pub struct ShellState {
    update: Option<Update>,
    update_progress: Option<String>,
    checking_update: bool,
    last_update_check: Option<String>,
    updating: bool,
    cs2_gone_ticks: u32,
    line_no_server_ticks: u32,
}

/// Set when the launcher closes itself (update, reset): no "test mode is on" question, no window saving.
pub static CLOSING: AtomicBool = AtomicBool::new(false);

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct ShellView {
    version: String,
    page: String,
    ready: bool,
    test_mode: bool,
    status_kind: &'static str,
    status_title: &'static str,
    status_detail: &'static str,
    update_available: bool,
    update_progress: Option<String>,
    update_commit: Option<String>,
    update_notes: Vec<String>,
    auto_update: bool,
    checking_update: bool,
    last_update_check: Option<String>,
}

const PAGE_TITLES: [(&str, &str); 9] = [
    ("play", "Play"),
    ("live", "Live Server"),
    ("skins", "All Skins"),
    ("inventory", "Inventory"),
    ("cases", "Cases"),
    ("hacker", "Hacker Mode"),
    ("workshop", "Workshop"),
    ("friends", "Friends"),
    ("setup", "Setup"),
];

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "app.state" => ok(state(app)),
        "app.log" => ok(app.log_lines()),
        "app.fullscreen" => {
            if let Some(w) = app.window() {
                let _ = w.set_fullscreen(!w.is_fullscreen().unwrap_or(false));
            }
            ok(())
        }
        "app.page" => {
            let page: String = arg(a)?;
            if let Some(w) = app.window() {
                let title = PAGE_TITLES
                    .iter()
                    .find(|t| t.0 == page)
                    .map_or("LAN Skin Tester".into(), |t| {
                        format!("{} · LAN Skin Tester", t.1)
                    });
                let _ = w.set_title(&title);
            }
            let mut st = app.state();
            st.settings.last_page = Some(page);
            app.save(&st);
            ok(())
        }
        "app.openUrl" => {
            let url: String = arg(a)?;
            if !url.starts_with("https://") && !url.starts_with("steam://") {
                return Err(bad("Not a web link"));
            }
            open_external(url).map(|_| Value::Null)
        }
        "app.openFolder" => {
            let which: String = arg(a)?;
            let path = {
                let st = app.state();
                match (which.as_str(), &st.game) {
                    ("game", Some(g)) => Some(g.root.clone()),
                    ("plugin", Some(g)) => Some(g.plugin_dir()),
                    ("cfg", Some(g)) => Some(g.cfg_dir()),
                    ("data", _) => Some(util::data_dir().to_path_buf()),
                    _ => None,
                }
            }
            .ok_or_else(|| bad("Pick your CS2 folder first."))?;
            fs::create_dir_all(&path)?;
            open_external(path).map(|_| Value::Null)
        }
        "app.checkUpdate" => ok(check_update_now(app)?),
        "app.autoUpdate" => {
            let on: bool = arg(a)?;
            let mut st = app.state();
            st.settings.auto_update = on;
            app.save(&st);
            drop(st);
            app.changed();
            ok(())
        }
        "app.update" => {
            install_update(app, false);
            ok(())
        }
        "app.resetAll" => reset_launcher(app).map(|_| Value::Null),
        _ => Err(unknown(name)),
    }
}

fn state(app: &App) -> ShellView {
    let st = app.state();
    let (test, ready) = (st.test_mode_on(), st.ready());
    let (status_kind, status_title, status_detail) = if test {
        ("warning", "Test mode on", "VAC off · press Stop when done")
    } else if ready {
        ("good", "Ready", "CS2 is normal")
    } else {
        ("bad", "Setup needed", "Open Setup to install")
    };
    // First run (or something missing): start on Setup; otherwise where you left off.
    let page = app.start_page.clone().unwrap_or_else(|| {
        if !ready || st.db.is_empty() {
            "setup".into()
        } else {
            st.settings
                .last_page
                .clone()
                .unwrap_or_else(|| "play".into())
        }
    });
    ShellView {
        version: update::short_version(),
        page,
        ready,
        test_mode: test,
        status_kind,
        status_title,
        status_detail,
        update_available: st.shell.update.is_some(),
        update_progress: st.shell.update_progress.clone(),
        update_commit: st
            .shell
            .update
            .as_ref()
            .map(|u| u.commit[..7.min(u.commit.len())].to_string()),
        update_notes: st
            .shell
            .update
            .as_ref()
            .map(|u| u.notes.clone())
            .unwrap_or_default(),
        auto_update: st.settings.auto_update,
        checking_update: st.shell.checking_update,
        last_update_check: st.shell.last_update_check.clone(),
    }
}

/// Called once the window is up: find CS2 and look for a launcher update.
pub fn started(app: &Arc<App>) {
    {
        let mut st = app.state();
        let path = st
            .settings
            .game_path
            .clone()
            .filter(|p| GameFolder::is_valid(Some(p)))
            .or_else(|| GameFolder::find(game::steam_path()));
        app.set_game(&mut st, path);
        if st.game.is_none() {
            app.log("CS2 not found automatically - press Browse on the Setup page.");
        }
    }
    report_last_update(app);
    let app = app.clone();
    std::thread::spawn(move || {
        check_for_update(&app, true);
        // Look again every 15 minutes while the launcher stays open, so a new build shows up without a restart.
        loop {
            std::thread::sleep(Duration::from_secs(15 * 60));
            if !app.state().shell.updating {
                check_for_update(&app, false);
            }
        }
    });
}

/// Looks for a newer build. At start-up (nothing running yet) it installs it right away when automatic
/// updates are on; while the launcher is open it only shows it, so nothing restarts under you.
fn check_for_update(app: &Arc<App>, startup: bool) -> Option<String> {
    {
        let mut st = app.state();
        if st.shell.checking_update {
            return None;
        }
        st.shell.checking_update = true;
    }
    app.changed();
    let result = update::check(&app.http);
    let mut st = app.state();
    st.shell.checking_update = false;
    st.shell.last_update_check = Some(chrono::Local::now().format("%H:%M").to_string());
    let found = match result {
        Ok(None) => {
            // The newest (again): forget an update that was shown earlier.
            st.shell.update = None;
            drop(st);
            app.changed();
            return None;
        }
        Err(e) => {
            drop(st);
            app.changed();
            return Some(e);
        }
        Ok(Some(found)) => found,
    };
    let is_new = st.shell.update.as_ref().map(|u| &u.commit) != Some(&found.commit);
    let short = found.commit[..7.min(found.commit.len())].to_string();
    st.shell.update = Some(found);
    let recently = st
        .settings
        .last_auto_update
        .as_deref()
        .and_then(|t| chrono::DateTime::parse_from_rfc3339(t).ok())
        .is_some_and(|t| {
            chrono::Utc::now().signed_duration_since(t) < chrono::Duration::minutes(30)
        });
    if startup
        && st.settings.auto_update
        && !recently
        && !st.test_mode_on()
        && update::can_self_update()
    {
        st.settings.last_auto_update = Some(chrono::Utc::now().to_rfc3339());
        app.save(&st);
        drop(st);
        app.log(format!(
            "Newer launcher build {short} on GitHub - updating automatically."
        ));
        app.changed();
        install_update(app, true);
        return None;
    }
    drop(st);
    if is_new {
        app.log(format!("Newer launcher build {short} on GitHub - click \"Update available\" in the sidebar to install it."));
        app.toast("Launcher update available.", "info");
    }
    app.changed();
    None
}

/// Says how the last self-update went (the swap script leaves a note for this start).
fn report_last_update(app: &App) {
    match update::take_result().as_deref() {
        None => {}
        Some("ok") => {
            app.log(format!("Launcher updated to {}.", update::short_version()));
            app.toast(format!("Updated to {}.", update::short_version()), "good");
        }
        Some(result) => {
            app.log(format!(
                "The update could not replace the launcher file ({result}). Download it from {}",
                update::RELEASE_URL
            ));
            app.toast("Update failed - see the log on the Setup page.", "bad");
        }
    }
}

/// The "Check for updates" button: looks now and says if this is already the newest.
fn check_update_now(app: &Arc<App>) -> R<bool> {
    if app.state().shell.updating {
        return Ok(true);
    }
    if let Some(error) = check_for_update(app, false) {
        app.log(format!("Could not check for a launcher update: {error}."));
        return Err(bad(format!("Couldn't check for updates: {error}.")));
    }
    let commit = app
        .state()
        .shell
        .update
        .as_ref()
        .map(|u| u.commit[..7.min(u.commit.len())].to_string());
    match &commit {
        Some(c) => app.toast(format!("Build {c} is out - click Update."), "info"),
        None => app.toast(
            format!(
                "You have the newest launcher ({}).",
                update::short_version()
            ),
            "good",
        ),
    }
    Ok(commit.is_some())
}

/// Downloads the new launcher, swaps it in after closing, and restarts.
fn install_update(app: &Arc<App>, automatic: bool) {
    {
        let mut st = app.state();
        if st.shell.updating {
            return;
        }
        st.shell.updating = true; // before the test is stopped: a second click mustn't start a second update meanwhile
    }
    install_update_core(app, automatic);
    app.state().shell.updating = false;
}

fn set_progress(app: &App, text: Option<String>) {
    app.state().shell.update_progress = text;
    app.changed();
}

fn install_update_core(app: &Arc<App>, automatic: bool) {
    if app.state().test_mode_on() {
        if automatic {
            return;
        }
        // The new launcher can't take over a running test: close CS2 and the server, restore CS2.
        app.log("Updating: stopping the test first (closes CS2 and the server).");
        if let Err(e) = super::play::stop_from_close(app) {
            app.log(format!("Could not restore gameinfo.gi: {e}"));
        }
    }
    if !update::can_self_update() {
        app.log(format!(
            "The launcher's folder ({}) isn't writable, so it can't update itself. Move LanSkinTesterLauncher.exe to a folder of your own (e.g. Documents) or install it with LanSkinTesterSetup.exe.",
            update::current_exe().parent().map(|p| p.display().to_string()).unwrap_or_default()
        ));
        if !automatic {
            app.toast(
                "Can't update: the launcher's folder isn't writable - see the log on Setup.",
                "bad",
            );
        }
        return;
    }
    set_progress(app, Some("Downloading 0%".into()));
    let shown = std::sync::atomic::AtomicI32::new(0);
    let result = update::download(
        &app.http,
        &|p| {
            // Only whole percents: every chunk would refresh all pages hundreds of times.
            let percent = (p * 100.0) as i32;
            if shown.swap(percent, Ordering::Relaxed) != percent {
                set_progress(app, Some(format!("Downloading {percent}%")));
            }
        },
        &|why| {
            app.log(format!(
                "Update download failed ({why}) - trying again shortly."
            ));
            set_progress(app, Some("Waiting for the download…".into()));
        },
    );
    let swap = result.and_then(|file| {
        set_progress(app, Some("Restarting".into()));
        app.log("Update downloaded - restarting the launcher.");
        backup_user_data(app);
        update::replace_after_exit(&file).map_err(|e| e.to_string())
    });
    match swap {
        Ok(()) => close_for_update(app, true),
        Err(e) => {
            set_progress(app, None);
            // No browser: the sidebar card stays, so trying again is one click.
            app.log(format!(
                "Update failed: {e} - click \"Update available\" to try again."
            ));
            app.toast("Update failed - click the card in the sidebar to try again (details in Setup › Log).", "bad");
        }
    }
}

/// Closes for a self-update or reset: no "test mode is on" question. Whatever still runs (a dialog, a stuck
/// thread) must not keep the old exe in use, so the process ends a few seconds later regardless.
pub fn close_for_update(app: &App, save_window: bool) {
    if save_window {
        if let Some(w) = app.window() {
            let mut st = app.state();
            remember_window(&w, &mut st);
            app.save(&st);
        }
    }
    CLOSING.store(true, Ordering::SeqCst);
    std::thread::spawn(|| {
        std::thread::sleep(Duration::from_secs(4));
        std::process::exit(0);
    });
    app.handle.exit(0);
}

/// Window position and size (restored on the next start); a maximized window keeps its normal bounds.
pub fn remember_window(w: &tauri::WebviewWindow, st: &mut State) {
    let maximized = w.is_maximized().unwrap_or(false);
    st.settings.maximized = maximized;
    if !maximized && !w.is_minimized().unwrap_or(false) && !w.is_fullscreen().unwrap_or(false) {
        if let (Ok(p), Ok(s)) = (w.outer_position(), w.outer_size()) {
            st.settings.window = Some(vec![p.x, p.y, s.width as i32, s.height as i32]);
        }
    }
}

/// Every 5 seconds: when CS2 and the test server are both closed but test mode is still on (game closed with
/// the X, a crash, or a previous launcher session), put CS2 back to normal.
pub fn watch_game(app: &Arc<App>) {
    let mut st = app.state();
    // No server for 10 seconds but the Metamod line is still in gameinfo.gi: take it out, so CS2 started from
    // Steam doesn't try to load Metamod.
    let stale_line = st
        .game
        .as_ref()
        .is_some_and(|g| session::find_server().is_none() && game::metamod_enabled(&g.game_info()));
    if stale_line {
        st.shell.line_no_server_ticks += 1;
        if st.shell.line_no_server_ticks >= 2 {
            st.shell.line_no_server_ticks = 0;
            if let Err(e) = game::disable_metamod(&st.game.as_ref().unwrap().game_info()) {
                app.log(format!("Could not update gameinfo.gi: {e}"));
            }
        }
    } else {
        st.shell.line_no_server_ticks = 0;
    }
    if !st.test_mode_on() || session::cs2_running() {
        st.shell.cs2_gone_ticks = 0;
        return;
    }
    // Two checks in a row (about 10 seconds), so a restarting game isn't caught in between.
    st.shell.cs2_gone_ticks += 1;
    if st.shell.cs2_gone_ticks < 2 {
        return;
    }
    st.shell.cs2_gone_ticks = 0;
    drop(st);
    match super::play::auto_restore(app) {
        Ok(()) => app.toast(
            "CS2 closed - restored to normal. You can play online again.",
            "info",
        ),
        Err(e) => app.log(format!("Could not restore gameinfo.gi automatically: {e}")),
    }
}

/// Closing with test mode on asks whether to restore CS2 first. False keeps the window open. Runs on the
/// window's thread.
pub fn confirm_close(app: &Arc<App>) -> bool {
    if CLOSING.load(Ordering::SeqCst) || !app.state().test_mode_on() {
        return true;
    }
    let mut dialog = rfd::MessageDialog::new()
        .set_title("Restore CS2?")
        .set_level(rfd::MessageLevel::Warning)
        .set_description("Skin test mode is still ON.\n\nYes = close CS2 + server and restore CS2 now (recommended)\nNo = leave the server and game running (CS2 from Steam works normally meanwhile)")
        .set_buttons(rfd::MessageButtons::YesNoCancel);
    if let Some(w) = app.window() {
        dialog = dialog.set_parent(&w);
    }
    match dialog.show() {
        rfd::MessageDialogResult::Yes => {
            if let Err(e) = super::play::stop_from_close(app) {
                rfd::MessageDialog::new()
                    .set_description(format!("Could not restore gameinfo.gi: {e}"))
                    .show();
            }
            true
        }
        rfd::MessageDialogResult::No => {
            // The running server read gameinfo.gi long ago; without the launcher nothing would take the line out.
            if let Some(g) = app.state().game.clone() {
                let _ = game::disable_metamod(&g.game_info());
            }
            true
        }
        _ => false,
    }
}

/// Setup › Reset Launcher (the page asks first): deletes everything the launcher keeps and the loadout in the CS2
/// folder, then starts again fresh. The mods in the CS2 folder stay: Uninstall Everything removes those.
fn reset_launcher(app: &Arc<App>) -> R<()> {
    let mut inv = app.inv();
    let st = app.state();
    if st.test_mode_on() {
        return Err(bad("Press Stop first: the test server is running."));
    }
    if let Some(g) = &st.game {
        let _ = fs::remove_file(g.loadout_file());
    }
    drop(st);
    for dir in [
        util::settings_file().parent().unwrap().to_path_buf(),
        util::data_dir().to_path_buf(),
    ] {
        delete_quietly(&dir); // files in use (the window's browser cache) stay
    }
    inv.forget();
    drop(inv);
    std::process::Command::new(update::current_exe()).spawn()?;
    close_for_update(app, false); // launcher.json is gone and stays gone
    Ok(())
}

fn delete_quietly(dir: &std::path::Path) {
    for e in fs::read_dir(dir).into_iter().flatten().flatten() {
        let p = e.path();
        if p.is_dir() {
            delete_quietly(&p);
        } else {
            let _ = fs::remove_file(p);
        }
    }
}

/// Copies inventory.json and loadouts.json to a dated folder before an update; keeps the last 5.
fn backup_user_data(app: &App) {
    let root = util::data_dir().join("backups");
    let dir = root.join(chrono::Local::now().format("%Y%m%d-%H%M%S").to_string());
    let loadout = app.state().game.as_ref().map(|g| g.loadout_file());
    let result = (|| -> std::io::Result<()> {
        fs::create_dir_all(&dir)?;
        for file in [Some(super::cases::inventory_file()), loadout]
            .into_iter()
            .flatten()
            .filter(|f| f.exists())
        {
            fs::copy(&file, dir.join(file.file_name().unwrap()))?;
        }
        let mut old: Vec<_> = fs::read_dir(&root)?
            .flatten()
            .map(|e| e.path())
            .filter(|p| p.is_dir())
            .collect();
        old.sort();
        old.reverse();
        for d in old.into_iter().skip(5) {
            fs::remove_dir_all(d)?;
        }
        Ok(())
    })();
    if let Err(e) = result {
        app.log(format!("Could not back up your data: {e}"));
    }
}
