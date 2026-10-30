//! "Skin test mode": Metamod in gameinfo.gi, a local dedicated server and CS2 with -insecure.

use crate::game::{self, GameFolder};
use crate::items::{ServerStatus, LOADED_FILE, STATUS_FILE};
use crate::server::{ServerSettings, PORT};
use crate::util;
use std::fs;
use std::path::PathBuf;
use std::process::{Child, Command};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use sysinfo::{Pid, ProcessRefreshKind, ProcessesToUpdate, System};

/// The dedicated server this launcher started (None after a launcher restart).
static SERVER: Mutex<Option<Child>> = Mutex::new(None);
/// True after Stop, so a closed server isn't reported as a crash.
pub static STOPPED_ON_PURPOSE: AtomicBool = AtomicBool::new(true);
/// The process list is asked many times a second: the answer is kept for a second.
static CS2: Mutex<Option<(Instant, Vec<u32>)>> = Mutex::new(None);

/// A running cs2.exe: its id and start time (unix seconds).
#[derive(Clone, Copy, PartialEq, Debug)]
pub struct Proc {
    pub pid: u32,
    pub start: u64,
}

fn is_cs2(name: &std::ffi::OsStr) -> bool {
    let n = name.to_string_lossy().to_lowercase();
    n == "cs2.exe" || n == "cs2"
}

fn system(pids: ProcessesToUpdate) -> System {
    let mut sys = System::new();
    sys.refresh_processes_specifics(pids, true, ProcessRefreshKind::nothing());
    sys
}

/// CPU (% of the whole PC) and memory (MB) of the server and the CS2 game, and the PC's free memory. CPU needs
/// two looks a moment apart, so the first call after a while reads 0.
pub fn performance() -> serde_json::Value {
    static SYS: std::sync::Mutex<Option<System>> = std::sync::Mutex::new(None);
    let mut guard = SYS.lock().unwrap_or_else(|e| e.into_inner());
    let sys = guard.get_or_insert_with(System::new);
    sys.refresh_processes_specifics(
        ProcessesToUpdate::All,
        true,
        ProcessRefreshKind::nothing().with_cpu().with_memory(),
    );
    sys.refresh_memory();
    let cores = std::thread::available_parallelism().map_or(1, |n| n.get()) as f32;
    let server = find_server().map(|p| p.pid);
    let (mut srv, mut game) = ((0f32, 0u64), (0f32, 0u64));
    for (pid, p) in sys.processes().iter().filter(|(_, p)| is_cs2(p.name())) {
        let slot = if Some(pid.as_u32()) == server {
            &mut srv
        } else {
            &mut game
        };
        slot.0 += p.cpu_usage() / cores;
        slot.1 += p.memory() / 1_000_000;
    }
    let total = sys.total_memory().max(1);
    serde_json::json!({
        "server": { "cpu": srv.0.round(), "memMb": srv.1 }, "game": { "cpu": game.0.round(), "memMb": game.1 },
        "ramUsedPct": (100 * (total - sys.available_memory()) / total), "ramFreeGb": (sys.available_memory() as f64 / 1e9 * 10.0).round() / 10.0,
        "totalGb": (total as f64 / 1e9).round(),
    })
}

fn cs2_ids() -> Vec<u32> {
    let mut cache = CS2.lock().unwrap();
    if let Some((at, ids)) = cache.as_ref() {
        if at.elapsed() < Duration::from_secs(1) {
            return ids.clone();
        }
    }
    let ids: Vec<u32> = system(ProcessesToUpdate::All)
        .processes()
        .iter()
        .filter(|(_, p)| is_cs2(p.name()))
        .map(|(pid, _)| pid.as_u32())
        .collect();
    *cache = Some((Instant::now(), ids.clone()));
    ids
}

fn forget_cs2() {
    *CS2.lock().unwrap() = None;
}

pub fn cs2_running() -> bool {
    !cs2_ids().is_empty()
}

/// The process id and start time, so a restarted launcher still knows which cs2.exe is the server.
fn pid_file() -> PathBuf {
    util::data_dir().join("server.pid")
}

/// Written when a server starts, removed by Stop: a server that closed (or crashed) still gets Stop's clean-up.
pub fn session_file() -> PathBuf {
    util::data_dir().join("test-session.on")
}

pub fn mark_session() {
    let _ = fs::create_dir_all(util::data_dir());
    let _ = fs::write(session_file(), "");
}

fn proc_of(pid: u32) -> Option<Proc> {
    let sys = system(ProcessesToUpdate::Some(&[Pid::from_u32(pid)]));
    let p = sys.process(Pid::from_u32(pid))?;
    is_cs2(p.name()).then(|| Proc {
        pid,
        start: p.start_time(),
    })
}

/// The running dedicated server (this launcher's, or the one a previous launcher started).
pub fn find_server() -> Option<Proc> {
    if let Some(child) = SERVER.lock().unwrap().as_mut() {
        if matches!(child.try_wait(), Ok(None)) {
            if let Some(p) = proc_of(child.id()) {
                return Some(p);
            }
        }
    }
    let text = fs::read_to_string(pid_file()).ok()?;
    let mut parts = text.split_whitespace();
    let (pid, start) = (
        parts.next()?.parse().ok()?,
        parts.next()?.parse::<u64>().ok()?,
    );
    proc_of(pid).filter(|p| p.start == start)
}

/// A CS2 game window is open (any cs2.exe that isn't the server).
pub fn client_running() -> bool {
    let server = find_server().map(|p| p.pid);
    cs2_ids().into_iter().any(|id| Some(id) != server)
}

/// The server we started exited (Some(exit code)) without Stop; None while it runs or when it isn't ours.
pub fn server_exit() -> Option<Option<i32>> {
    let mut guard = SERVER.lock().unwrap();
    let status = guard.as_mut()?.try_wait().ok()??;
    *guard = None;
    Some(status.code())
}

fn kill(pid: u32) {
    let sys = system(ProcessesToUpdate::Some(&[Pid::from_u32(pid)]));
    if let Some(p) = sys.process(Pid::from_u32(pid)) {
        p.kill();
        let deadline = Instant::now() + Duration::from_secs(10);
        while Instant::now() < deadline && proc_of(pid).is_some() {
            std::thread::sleep(Duration::from_millis(100));
        }
    }
    forget_cs2();
}

/// Closes the server process only.
pub fn stop_server_only() {
    STOPPED_ON_PURPOSE.store(true, Ordering::Relaxed);
    if let Some(p) = find_server() {
        kill(p.pid);
    }
    SERVER.lock().unwrap().take();
}

/// The plugin was loaded (unix seconds) by the server process that started at this time.
pub fn loaded_by_this_server(loaded: i64, server_start: i64) -> bool {
    loaded >= server_start
}

/// The plugin wrote this status after the server process started (a restart leaves the old file for a moment).
pub fn status_from_this_server(status_time: i64, server_start: i64) -> bool {
    status_time > server_start + 2
}

/// Makes the open CS2 join the local server again (Steam hands the connect to the running game).
pub fn reconnect_client(password: &str) -> Result<(), String> {
    let pass = if password.trim().is_empty() {
        String::new()
    } else {
        format!("/{}", util::escape(password.trim()))
    };
    open::that_detached(format!("steam://connect/127.0.0.1:{PORT}{pass}"))
        .map_err(|e| e.to_string())
}

pub struct Session<'a> {
    pub game: &'a GameFolder,
}

impl Session<'_> {
    /// Metamod is in gameinfo.gi, a session marker is there, or our server runs.
    pub fn is_on(&self) -> bool {
        game::metamod_enabled(&self.game.game_info())
            || session_file().exists()
            || find_server().is_some()
    }

    /// The game reads the same gameinfo.gi as the server; with the Metamod line CS2 itself loads every server
    /// plugin, which crashed it on joining. The server reads gameinfo.gi only when it starts, so once it is up
    /// the line comes out. Only a status written after this server process started counts.
    pub fn isolate_client(&self, status: Option<&ServerStatus>) -> std::io::Result<()> {
        let Some(server) = find_server() else {
            return Ok(());
        };
        if !game::metamod_enabled(&self.game.game_info()) {
            return Ok(());
        }
        // A server up for a minute has read gameinfo.gi even if its plugin never reported.
        let young = util::unix_now() - (server.start as i64) < 60;
        if young && !status.is_some_and(|s| status_from_this_server(s.time, server.start as i64)) {
            return Ok(());
        }
        game::disable_metamod(&self.game.game_info())
    }

    /// Takes the Metamod line out as soon as the plugin reports Metamod loaded it, so CS2 can start while the
    /// server is still loading the map. Returns whether gameinfo.gi is clean for the game.
    pub fn isolate_client_early(&self) -> std::io::Result<bool> {
        let Some(server) = find_server() else {
            return Ok(false);
        };
        let Ok(text) = fs::read_to_string(self.game.plugin_dir().join(LOADED_FILE)) else {
            return Ok(false);
        };
        match text.trim().parse::<i64>() {
            Ok(loaded) if loaded_by_this_server(loaded, server.start as i64) => {
                game::disable_metamod(&self.game.game_info())?;
                Ok(true)
            }
            _ => Ok(false),
        }
    }

    /// Starts the server. Returns whether the caller should start CS2 once the server is up.
    pub fn start(&self, settings: &ServerSettings) -> Result<bool, String> {
        if cs2_running() {
            return Err("Close CS2 first (it must restart with -insecure).".into());
        }
        self.launch_server(settings)?;
        Ok(settings.auto_start_game && !settings.workshop_needs_download())
    }

    /// Restarts only the server: an open CS2 stays open.
    pub fn restart_server(&self, settings: &ServerSettings) -> Result<(), String> {
        stop_server_only();
        self.launch_server(settings)
    }

    fn launch_server(&self, settings: &ServerSettings) -> Result<(), String> {
        game::remove_workshop_copies(self.game);
        game::enable_metamod(&self.game.game_info())?;
        settings.write_cfg(self.game).map_err(|e| e.to_string())?;
        mark_session();
        let _ = fs::remove_file(self.console_log());
        // The last server's: CS2 must wait until this server's plugin writes new ones.
        let _ = fs::remove_file(self.game.plugin_dir().join(LOADED_FILE));
        let _ = fs::remove_file(self.game.plugin_dir().join(STATUS_FILE));
        // Commands queued for the last server: one that crashed it would crash this one too.
        for f in ["server_commands.txt", "server_commands.txt.run"] {
            let _ = fs::remove_file(self.game.plugin_dir().join(f));
        }
        let child = self.run_cs2(&settings.start_arguments())?;
        // Above normal: the map loads and the server ticks on time while CS2 and the launcher share the CPU.
        #[cfg(windows)]
        unsafe {
            use std::os::windows::io::AsRawHandle;
            windows_sys::Win32::System::Threading::SetPriorityClass(
                child.as_raw_handle(),
                windows_sys::Win32::System::Threading::ABOVE_NORMAL_PRIORITY_CLASS,
            );
        }
        if let Some(p) = proc_of(child.id()) {
            let _ = fs::write(pid_file(), format!("{} {}", p.pid, p.start));
        }
        *SERVER.lock().unwrap() = Some(child);
        STOPPED_ON_PURPOSE.store(false, Ordering::Relaxed);
        Ok(())
    }

    /// Starts CS2 (VAC off) and joins the local server.
    pub fn start_game(&self, settings: &ServerSettings) -> Result<(), String> {
        self.run_cs2(&settings.game_arguments(self.game)).map(drop)
    }

    /// Starts CS2 (VAC off for this run) joining another server.
    pub fn join(
        &self,
        settings: &ServerSettings,
        address: &str,
        password: &str,
    ) -> Result<(), String> {
        self.run_cs2(&settings.join_arguments(self.game, address, password))
            .map(drop)
    }

    /// Starts CS2 as an offline game on a Workshop map (no server, gameinfo.gi untouched).
    pub fn play_offline(
        &self,
        settings: &ServerSettings,
        id: &str,
        map: &str,
    ) -> Result<(), String> {
        let extra = settings.game_args.replace("+connect", "");
        self.run_cs2(format!("{} +map_workshop {id} {map}", extra.trim()).trim())
            .map(drop)
    }

    fn run_cs2(&self, arguments: &str) -> Result<Child, String> {
        forget_cs2();
        let exe = self.game.exe();
        let mut cmd = Command::new(&exe);
        #[cfg(windows)]
        std::os::windows::process::CommandExt::raw_arg(&mut cmd, arguments);
        #[cfg(not(windows))]
        cmd.args(arguments.split_whitespace());
        cmd.current_dir(exe.parent().unwrap())
            .spawn()
            .map_err(|e| format!("{}: {e}", exe.display()))
    }

    /// The server's console output (it runs with -condebug).
    pub fn console_log(&self) -> PathBuf {
        self.game.csgo().join("console.log")
    }

    /// The last lines the server wrote, to show why it stopped.
    pub fn console_tail(&self, lines: usize) -> Vec<String> {
        util::tail(&self.console_log(), lines, 256 * 1024)
    }

    /// Closes the test server and game, then puts gameinfo.gi back to normal.
    pub fn stop(&self) -> std::io::Result<()> {
        STOPPED_ON_PURPOSE.store(true, Ordering::Relaxed);
        for pid in cs2_ids() {
            kill(pid);
        }
        SERVER.lock().unwrap().take();
        game::disable_metamod(&self.game.game_info())?;
        let _ = fs::remove_file(session_file());
        crate::net::remove_mapping(); // close the router port again (internet mode)
        game::remove_workshop_copies(self.game);
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn only_a_status_from_the_new_server_counts() {
        let started = 1_792_238_400;
        assert!(!status_from_this_server(started - 5, started));
        assert!(!status_from_this_server(started, started));
        assert!(status_from_this_server(started + 20, started));
        assert!(!loaded_by_this_server(started - 30, started));
        assert!(loaded_by_this_server(started, started));
        assert!(loaded_by_this_server(started + 4, started));
    }

    #[test]
    fn test_mode_stays_on_until_stop() {
        let root = std::env::temp_dir().join(format!("lst-game-{}", util::random_hex(6)));
        let game = GameFolder::new(&root);
        fs::create_dir_all(game.csgo()).unwrap();
        fs::write(game.game_info(), "\t\t\tGame\tcsgo\n").unwrap();
        let session = Session { game: &game };
        game::enable_metamod(&game.game_info()).unwrap();
        // No server process: nothing has read gameinfo.gi, so the client isn't isolated.
        fs::create_dir_all(game.plugin_dir()).unwrap();
        fs::write(
            game.plugin_dir().join(LOADED_FILE),
            util::unix_now().to_string(),
        )
        .unwrap();
        assert!(!session.isolate_client_early().unwrap());
        session
            .isolate_client(Some(&ServerStatus {
                time: util::unix_now(),
                ..Default::default()
            }))
            .unwrap();
        assert!(game::metamod_enabled(&game.game_info()));
        game::disable_metamod(&game.game_info()).unwrap();

        mark_session();
        assert!(session_file().exists());
        assert!(
            session.is_on(),
            "a clean gameinfo.gi with the marker is still test mode"
        );
        game::enable_metamod(&game.game_info()).unwrap();
        // Stop kills every cs2 process; the test machine has none.
        assert!(!cs2_running());
        session.stop().unwrap();
        assert!(
            !session_file().exists()
                && !game::metamod_enabled(&game.game_info())
                && !session.is_on()
        );
        fs::remove_dir_all(root).unwrap();
    }
}
