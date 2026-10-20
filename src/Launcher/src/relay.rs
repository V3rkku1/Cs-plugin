//! Hacker mode's triggerbot on this PC: the server can't press fire for a game client, so the plugin sends
//! "fire" to this launcher over localhost and the launcher clicks the left mouse button. Only while the
//! launcher's VAC-off test session runs and the CS2 game window is in front.

use std::net::UdpSocket;
use std::time::{Duration, Instant};

/// The plugin sends to 127.0.0.1 on this port (lst_fire_relay in the server config).
pub const PORT: u16 = 27099;

pub fn start(allowed: impl Fn() -> bool + Send + 'static, log: impl Fn(String) + Send + 'static) {
    std::thread::spawn(move || {
        let udp = match UdpSocket::bind(("127.0.0.1", PORT)) {
            Ok(u) => u,
            Err(e) => return log(format!("Triggerbot: port {PORT} is in use, so it can't press fire in CS2 ({e}).")),
        };
        let mut last = Instant::now() - Duration::from_secs(1);
        let mut buf = [0u8; 64];
        loop {
            let Ok((n, from)) = udp.recv_from(&mut buf) else {
                std::thread::sleep(Duration::from_millis(100));
                continue;
            };
            // The plugin paces shots by the gun's fire rate; this only stops a flood.
            if &buf[..n] != b"LST fire" || !from.ip().is_loopback() || last.elapsed() < Duration::from_millis(35) || !allowed() || !game_in_front() {
                continue;
            }
            last = Instant::now();
            click();
        }
    });
}

/// The CS2 game window (not the server console) has focus.
#[cfg(windows)]
fn game_in_front() -> bool {
    use windows_sys::Win32::UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowThreadProcessId};
    let mut pid = 0u32;
    unsafe {
        let window = GetForegroundWindow();
        if window.is_null() {
            return false;
        }
        GetWindowThreadProcessId(window, &mut pid);
    }
    let mut sys = sysinfo::System::new();
    let p = sysinfo::Pid::from_u32(pid);
    sys.refresh_processes_specifics(sysinfo::ProcessesToUpdate::Some(&[p]), true, sysinfo::ProcessRefreshKind::nothing());
    sys.process(p).is_some_and(|p| p.name().eq_ignore_ascii_case("cs2.exe")) && crate::session::find_server().map(|s| s.pid) != Some(pid)
}

#[cfg(windows)]
fn click() {
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{SendInput, INPUT, INPUT_0, INPUT_MOUSE, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, MOUSEINPUT};
    let input = |flags| INPUT { r#type: INPUT_MOUSE, Anonymous: INPUT_0 { mi: MOUSEINPUT { dx: 0, dy: 0, mouseData: 0, dwFlags: flags, time: 0, dwExtraInfo: 0 } } };
    unsafe {
        SendInput(1, &input(MOUSEEVENTF_LEFTDOWN), std::mem::size_of::<INPUT>() as i32);
        std::thread::sleep(Duration::from_millis(18)); // long enough for the game to see the press
        SendInput(1, &input(MOUSEEVENTF_LEFTUP), std::mem::size_of::<INPUT>() as i32);
    }
}

#[cfg(not(windows))]
fn game_in_front() -> bool {
    false
}

#[cfg(not(windows))]
fn click() {}
