//! Live Server › Logs: the launcher's activity, the server console (csgo\console.log, -condebug) and
//! CounterStrikeSharp's newest log in one place, and a button that copies all of it as one report.

use super::{arg, bad, ok, open_external, unknown, App, State, R};
use crate::session::Session;
use crate::update;
use crate::util;
use regex::Regex;
use serde::Serialize;
use serde_json::Value;
use std::path::PathBuf;
use std::sync::{Arc, LazyLock};

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct LogsView {
    console: Vec<String>,
    plugin: Vec<String>,
    console_file: Option<String>,
    plugin_file: Option<String>,
}

fn plugin_log_file(st: &State) -> Option<PathBuf> {
    let dir = st.game.as_ref()?.css().join("logs");
    std::fs::read_dir(dir).ok()?.flatten()
        .filter(|e| e.path().extension().is_some_and(|x| x == "txt"))
        .max_by_key(|e| e.metadata().and_then(|m| m.modified()).ok())
        .map(|e| e.path())
}

fn console_log_file(st: &State) -> Option<PathBuf> {
    st.game.as_ref().map(|g| Session { game: g }.console_log())
}

fn tail(path: &Option<PathBuf>, lines: usize) -> Vec<String> {
    // shortcut: reads at most the last 512 KB, enough for a few thousand lines.
    path.as_ref().map(|p| util::tail(p, lines, 512 * 1024)).unwrap_or_default()
}

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    let st = app.state();
    let (console, plugin) = (console_log_file(&st), plugin_log_file(&st));
    match name {
        "live.logs" => ok(LogsView {
            console: tail(&console, 400),
            plugin: tail(&plugin, 400),
            console_file: console.as_ref().map(|p| p.display().to_string()),
            plugin_file: plugin.as_ref().map(|p| p.display().to_string()),
        }),
        "live.openLog" => {
            let which: String = arg(a)?;
            let path = if which == "plugin" { plugin } else { console }.filter(|p| p.exists());
            let path = path.ok_or_else(|| bad(if which == "plugin" { "No CounterStrikeSharp log yet." } else { "No console log yet: start the server once." }))?;
            open_external(path).map(|_| Value::Null)
        }
        "live.copyLogs" => {
            let report = report(app, &st, &console, &plugin);
            drop(st);
            if app.copy(&report) {
                app.toast("Logs copied. Paste them (Ctrl+V) where you ask for help.", "good");
            } else {
                app.toast("Could not use the clipboard.", "bad");
            }
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

/// One report: what is set up, the error lines first, then the end of each log.
fn report(app: &App, st: &State, console_file: &Option<PathBuf>, plugin_file: &Option<PathBuf>) -> String {
    static ERROR_LINE: LazyLock<Regex> = LazyLock::new(|| {
        Regex::new(r"(?i)error|eror|\[err|\[wrn|fail|exception|crash|could not|couldn't|unable|missing|not found|denied|warning").unwrap()
    });
    let activity = app.log_lines();
    let console = tail(console_file, 150);
    let plugin = tail(plugin_file, 80);
    let s = &st.settings.server;
    let mut text = vec![
        "=== LAN Skin Tester logs ===".to_string(),
        format!(
            "Launcher {} · {} · CS2 folder {} · plugin {} · server {}",
            update::short_version(),
            std::env::consts::OS,
            if st.game.is_some() { "found" } else { "not found" },
            if st.game.as_ref().is_some_and(|g| g.plugin_installed()) { "installed" } else { "not installed" },
            if st.server_status().is_some() { "running" } else { "not running" }
        ),
        format!("Mode {} · map {}", s.mode, s.map),
    ];
    let mut errors: Vec<String> = vec![];
    for l in activity.iter().chain(&console).chain(&plugin) {
        if ERROR_LINE.is_match(l) && !errors.contains(l) {
            errors.push(l.clone());
        }
    }
    let errors = errors[errors.len().saturating_sub(40)..].to_vec();
    let mut block = |title: String, lines: Vec<String>| {
        text.push(String::new());
        text.push(format!("--- {title} ---"));
        text.extend(lines);
    };
    let or = |lines: Vec<String>, empty: &str| if lines.is_empty() { vec![empty.to_string()] } else { lines };
    block(format!("Errors and warnings ({})", errors.len()), or(errors, "(none found)"));
    block("Launcher activity".into(), activity[activity.len().saturating_sub(80)..].to_vec());
    block("Server console (console.log)".into(), or(console, "(no console.log yet - start the server once)"));
    block("CounterStrikeSharp log".into(), or(plugin, "(none)"));
    text.join("\r\n") + "\r\n"
}
