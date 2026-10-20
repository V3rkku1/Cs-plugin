//! Your skins on a friend's server: cfg\lst_myskins.cfg holds your loadout as css_myskins lines, and the launcher
//! types "exec lst_myskins" into CS2's console (-netconport) while you're on their server.

use crate::game::GameFolder;
use crate::items::{self, PlayerLoadout};
use crate::util;
use std::fs;
use std::io::Write;
use std::net::{SocketAddr, TcpStream};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::OnceLock;
use std::time::{Duration, Instant};

pub const PORT: u16 = 29015;
pub const SKINS_CFG: &str = "lst_myskins";

/// -netconpassword: kept in a file so a restarted launcher still matches the CS2 it started earlier.
pub fn password() -> &'static str {
    static PASSWORD: OnceLock<String> = OnceLock::new();
    PASSWORD.get_or_init(|| {
        let file = util::data_dir().join("netcon.key");
        if let Ok(saved) = fs::read_to_string(&file) {
            if saved.trim().len() >= 16 {
                return saved.trim().to_string();
            }
        }
        let fresh = util::random_hex(12);
        let _ = fs::create_dir_all(util::data_dir());
        let _ = fs::write(file, &fresh);
        fresh
    })
}

/// Writes your loadout into the cfg CS2 runs. False when there is nothing to send.
pub fn write_skins_cfg(game: &GameFolder, loadout: &PlayerLoadout) -> std::io::Result<bool> {
    let chunks = items::encode_loadout(loadout);
    if chunks.len() > items::MAX_CHUNKS {
        return Ok(false);
    }
    let lines: Vec<String> = chunks.iter().enumerate().map(|(i, c)| format!("css_myskins {} {} \"{c}\"", i + 1, chunks.len())).collect();
    fs::create_dir_all(game.cfg_dir())?;
    fs::write(game.cfg_dir().join(format!("{SKINS_CFG}.cfg")), lines.join("\n") + "\n")?;
    Ok(true)
}

/// Types the command into CS2's console. False when CS2 isn't listening (not started by the launcher).
pub fn send(command: &str) -> bool {
    let addr: SocketAddr = ([127, 0, 0, 1], PORT).into();
    let Ok(mut tcp) = TcpStream::connect_timeout(&addr, Duration::from_secs(2)) else { return false };
    let _ = tcp.set_write_timeout(Some(Duration::from_secs(2)));
    tcp.write_all(format!("PASS {}\n{command}\n", password()).as_bytes()).is_ok()
}

/// Bumped to stop the current sender (a new join, or your own server).
static GENERATION: AtomicU64 = AtomicU64::new(0);

/// After joining a friend: every 10 seconds while CS2 runs, refresh the cfg. It runs in CS2 for the first
/// 2 minutes (while you connect and load) and after that only when the cfg changed.
pub fn start_sending_skins(
    still_joined: impl Fn() -> bool + Send + 'static,
    refresh_cfg: impl Fn() -> Option<String> + Send + 'static,
    log: impl Fn(String) + Send + 'static,
) {
    let mine = GENERATION.fetch_add(1, Ordering::SeqCst) + 1;
    std::thread::spawn(move || {
        let current = || GENERATION.load(Ordering::SeqCst) == mine;
        std::thread::sleep(Duration::from_secs(15)); // CS2 starting and connecting
        let until = Instant::now() + Duration::from_secs(120);
        let mut sent: Option<String> = None;
        let mut warned = false;
        while current() && still_joined() {
            if let Some(cfg) = refresh_cfg() {
                if Instant::now() < until || Some(&cfg) != sent.as_ref() {
                    if send(&format!("exec {SKINS_CFG}")) {
                        sent = Some(cfg);
                    } else if !warned {
                        warned = true;
                        log(format!("Couldn't reach CS2's console to send your skins. In CS2: console (~) → exec {SKINS_CFG}"));
                    }
                }
            }
            std::thread::sleep(Duration::from_secs(10));
        }
    });
}
