//! Everything the UI can ask the launcher to do: `call(name, args)` from the page, one module per page.
//! Calls run on a worker thread each. `State` (settings, game folder, item data, page state) is one mutex:
//! never hold it across a download or a dialog. Inventory calls also hold `App::inv`, taken before `State`.

mod cases;
pub mod friends;
mod hacker;
mod logs;
mod play;
mod setup;
pub mod shell;
mod theme;
mod tradeup;
mod trainer;
mod workshop;

pub use cases::{Inv, UNLOCK_HASH};
pub use play::poll_server;
pub use shell::{confirm_close, started, watch_game};
pub use theme::apply_title_bar;

use crate::game::{self, GameFolder};
use crate::itemdb::{self, ItemDatabase};
use crate::items::{ServerStatus, STATUS_FILE};
use crate::prices::PriceList;
use crate::session::Session;
use crate::settings::LauncherSettings;
use crate::util;
use serde::de::DeserializeOwned;
use serde::Serialize;
use serde_json::{json, Value};
use std::sync::{Arc, Mutex, MutexGuard};
use tauri::{AppHandle, Emitter, Manager};

/// An error for the page: `User` is a message for the person (a red toast, not logged), `Fail` is
/// unexpected (logged as FAILED, and shown too).
#[derive(Debug)]
pub enum ApiError {
    User(String),
    Fail(String),
}

pub type R<T> = Result<T, ApiError>;

pub fn bad(message: impl Into<String>) -> ApiError {
    ApiError::User(message.into())
}

impl From<std::io::Error> for ApiError {
    fn from(e: std::io::Error) -> Self {
        ApiError::Fail(e.to_string())
    }
}

impl From<String> for ApiError {
    fn from(e: String) -> Self {
        ApiError::Fail(e)
    }
}

pub struct App {
    pub handle: AppHandle,
    pub http: ureq::Agent,
    pub prices: PriceList,
    state: Mutex<State>,
    inv: Mutex<Inv>,
    log: Mutex<Vec<String>>,
    /// Optional --page from the command line, so a test run can open one screen.
    pub start_page: Option<String>,
}

/// The settings and everything the pages share, plus each page's own state.
pub struct State {
    pub settings: LauncherSettings,
    pub game: Option<GameFolder>,
    pub db: Arc<ItemDatabase>,
    pub shell: shell::ShellState,
    pub setup: setup::SetupState,
    pub play: play::PlayState,
    pub friends: friends::FriendsState,
    pub hacker_sent: bool,
    pub collections: tradeup::Collections,
}

impl State {
    /// Metamod, CounterStrikeSharp and the plugin are installed.
    pub fn ready(&self) -> bool {
        self.game
            .as_ref()
            .is_some_and(|g| g.metamod_installed() && g.css_installed() && g.plugin_installed())
    }

    pub fn test_mode_on(&self) -> bool {
        self.game
            .as_ref()
            .is_some_and(|g| Session { game: g }.is_on())
    }

    pub fn game(&self) -> R<&GameFolder> {
        self.game
            .as_ref()
            .ok_or_else(|| bad("Pick your CS2 folder first."))
    }

    /// The plugin's live report, or None if the server isn't running (or the report is stale).
    pub fn server_status(&self) -> Option<ServerStatus> {
        let text =
            std::fs::read_to_string(self.game.as_ref()?.plugin_dir().join(STATUS_FILE)).ok()?;
        let status: ServerStatus = util::read_json(&text).ok()?;
        (-5..=8)
            .contains(&(util::unix_now() - status.time))
            .then_some(status)
    }
}

impl App {
    pub fn new(handle: AppHandle, start_page: Option<String>) -> Arc<App> {
        let mut settings = LauncherSettings::load();
        // Hacker mode is never carried over from last time: it is on only when picked in this session.
        settings.server.hacker_mode = 0;
        let db = Arc::new(ItemDatabase::load_cached(&itemdb::data_dir()));
        Arc::new_cyclic(|me: &std::sync::Weak<App>| {
            let (log_me, changed_me) = (me.clone(), me.clone());
            App {
                handle,
                http: util::http_agent(300),
                prices: PriceList::new(
                    Arc::new(move |line| {
                        if let Some(a) = log_me.upgrade() {
                            a.log(line)
                        }
                    }),
                    Arc::new(move || {
                        if let Some(a) = changed_me.upgrade() {
                            a.changed()
                        }
                    }),
                ),
                state: Mutex::new(State {
                    settings,
                    game: None,
                    db,
                    shell: Default::default(),
                    setup: Default::default(),
                    play: Default::default(),
                    friends: Default::default(),
                    hacker_sent: false,
                    collections: Default::default(),
                }),
                inv: Mutex::new(Inv::default()),
                log: Mutex::new(vec![]),
                start_page,
            }
        })
    }

    pub fn state(&self) -> MutexGuard<'_, State> {
        self.state.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// The inventory gate: inventory and loadout changes one at a time (take it before `state`).
    pub fn inv(&self) -> MutexGuard<'_, Inv> {
        self.inv.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// Sends an event to the page: { type, data }.
    pub fn push(&self, kind: &str, data: Value) {
        let _ = self
            .handle
            .emit("lst", json!({ "type": kind, "data": data }));
    }

    /// Folder, install state, test mode or item data changed: every page reloads its data.
    pub fn changed(&self) {
        self.push("changed", Value::Null);
    }

    /// A toast in the window: kind is "info", "good", "warning" or "bad".
    /// A Windows notification, only while the launcher isn't the window in front (Setup › Notifications).
    /// On its own thread: asking the window whether it has focus waits for the window's thread.
    pub fn notify(&self, title: &str, body: &str) {
        if !crate::notify::ENABLED.load(std::sync::atomic::Ordering::Relaxed) {
            return;
        }
        let Some(window) = self.window() else { return };
        let (title, body) = (title.to_string(), body.to_string());
        std::thread::spawn(move || {
            if !window.is_focused().unwrap_or(true) {
                crate::notify::show(&window, &title, &body);
            }
        });
    }

    pub fn toast(&self, text: impl Into<String>, kind: &str) {
        self.push("toast", json!({ "text": text.into(), "kind": kind }));
    }

    pub fn log(&self, line: impl Into<String>) {
        let line = util::stamp(&line.into());
        {
            let mut log = self.log.lock().unwrap();
            log.push(line.clone());
            let extra = log.len().saturating_sub(600);
            log.drain(..extra);
        }
        self.push("log", Value::String(line));
    }

    pub fn log_lines(&self) -> Vec<String> {
        self.log.lock().unwrap().clone()
    }

    /// Saves launcher.json; a failure is only logged (it is saved again with the next change).
    pub fn save(&self, st: &State) {
        if let Err(e) = st.settings.save() {
            self.log(format!("Could not save settings: {e}"));
        }
    }

    pub fn window(&self) -> Option<tauri::WebviewWindow> {
        self.handle.get_webview_window("main")
    }

    /// Runs on the window's thread (dialogs) and returns its result.
    pub fn on_ui<T: Send + 'static>(
        &self,
        work: impl FnOnce(Option<tauri::WebviewWindow>) -> T + Send + 'static,
    ) -> T {
        let (tx, rx) = std::sync::mpsc::channel();
        let window = self.window();
        if self
            .handle
            .run_on_main_thread(move || {
                let _ = tx.send(work(window));
            })
            .is_err()
        {
            panic!("the window is gone");
        }
        rx.recv().expect("the window thread stopped")
    }

    /// Copies text to the clipboard.
    pub fn copy(&self, text: &str) -> bool {
        arboard::Clipboard::new()
            .and_then(|mut c| c.set_text(text.to_string()))
            .is_ok()
    }

    /// Picks a file or folder with the system dialog (None when cancelled).
    pub fn pick(
        &self,
        dialog: impl FnOnce(rfd::FileDialog) -> Option<std::path::PathBuf> + Send + 'static,
    ) -> Option<std::path::PathBuf> {
        self.on_ui(move |w| {
            let d = rfd::FileDialog::new();
            dialog(match &w {
                Some(w) => d.set_parent(w),
                None => d,
            })
        })
    }

    /// Uses the CS2 folder (or forgets it when it isn't one).
    pub fn set_game(&self, st: &mut State, path: Option<String>) {
        st.game =
            GameFolder::is_valid(path.as_deref()).then(|| GameFolder::new(path.clone().unwrap()));
        if let Some(g) = st.game.clone() {
            st.settings.game_path = Some(g.root.display().to_string());
            self.save(st);
            // Keep the plugin's skin names in sync with the launcher's data (e.g. after picking another folder).
            if !st.db.is_empty()
                && g.plugin_installed()
                && !g.skin_names_dir().join("skins.json").exists()
            {
                let _ = st.db.write_plugin_skin_names(&g.skin_names_dir());
            }
            if crate::server::discover_modes(&g) {
                self.log("Found Valve's Rush mode in your CS2 files.");
            }
        }
        self.changed();
    }

    pub fn reload_db(&self) {
        let db = Arc::new(ItemDatabase::load_cached(&itemdb::data_dir()));
        let mut st = self.state();
        if let Some(g) = &st.game {
            if g.plugin_dir().is_dir() && !db.is_empty() {
                let _ = db.write_plugin_skin_names(&g.skin_names_dir());
            }
        }
        st.db = db;
        drop(st);
        self.changed();
    }

    /// Turns off CounterStrikeSharp's FollowCS2ServerGuidelines (it blocks skins).
    pub fn allow_skins_in_css(&self, game: Option<&GameFolder>) {
        if let Some(g) = game.filter(|g| g.css_installed()) {
            match game::css_allow_skins(g) {
                Ok(true) => self.log("Set FollowCS2ServerGuidelines to false in CounterStrikeSharp's core.json (otherwise only the knife model changes, not the skin)."),
                Ok(false) => {}
                Err(e) => self.log(format!("Could not change CounterStrikeSharp's core.json: {e}")),
            }
        }
    }
}

fn arg<T: DeserializeOwned + Default>(v: &Value) -> R<T> {
    if v.is_null() {
        return Ok(T::default());
    }
    serde_json::from_value(v.clone()).map_err(|e| bad(format!("Bad arguments: {e}")))
}

fn ok<T: Serialize>(v: T) -> R<Value> {
    Ok(serde_json::to_value(v).unwrap_or(Value::Null))
}

/// Runs one call from the page.
pub fn call(app: &Arc<App>, name: &str, a: Value) -> Result<Value, String> {
    let result = dispatch(app, name, &a);
    result.map_err(|e| match e {
        ApiError::User(m) => m,
        ApiError::Fail(m) => {
            app.log(format!("FAILED ({name}): {m}"));
            m
        }
    })
}

fn dispatch(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    // Inventory and loadout calls one at a time: they share one in-memory list and loadouts.json.
    if name.starts_with("cases.") || name.starts_with("inventory.") || name.starts_with("tradeup.")
    {
        let mut inv = app.inv();
        return match name.split('.').next() {
            Some("tradeup") => tradeup::call(app, &mut inv, name, a),
            _ => cases::call(app, &mut inv, name, a),
        };
    }
    match name.split('.').next().unwrap_or("") {
        "app" => shell::call(app, name, a),
        "setup" => match name {
            "setup.theme"
            | "setup.themeReset"
            | "setup.themeImage"
            | "setup.themeImageRemove"
            | "setup.themeExport"
            | "setup.themeImport"
            | "setup.customCss" => theme::call(app, name, a),
            _ => setup::call(app, name, a),
        },
        "play" => play::call(app, name, a),
        "live" => match name {
            "live.logs" | "live.openLog" | "live.copyLogs" => logs::call(app, name, a),
            _ => play::call(app, name, a),
        },
        "friends" => friends::call(app, name, a),
        "hacker" => hacker::call(app, name, a),
        "trainer" => trainer::call(app, name, a),
        "workshop" => workshop::call(app, name, a),
        "backup" => cases::backup(app, name),
        _ => Err(bad(format!("Unknown call: {name}"))),
    }
}

fn unknown(name: &str) -> ApiError {
    bad(format!("Unknown call: {name}"))
}

fn plural(n: i32, word: &str) -> String {
    format!("{n} {word}{}", if n == 1 { "" } else { "s" })
}

/// Opens a link, file or folder with the system's app for it.
fn open_external(target: impl AsRef<std::ffi::OsStr>) -> R<()> {
    open::that_detached(target).map_err(|e| ApiError::Fail(e.to_string()))
}
