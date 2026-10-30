//! Windows notifications (the Action Center kind) while the launcher is in the background: the server is up, a
//! friend joined, an update is out. A tray icon shows the message and goes away again 20 seconds later.

/// Setup › Notifications (copied from the settings when they load or change).
pub static ENABLED: std::sync::atomic::AtomicBool = std::sync::atomic::AtomicBool::new(true);

/// Shows one notification. No-op off Windows.
#[cfg(windows)]
pub fn show(window: &tauri::WebviewWindow, title: &str, body: &str) {
    use std::sync::atomic::{AtomicU64, Ordering};
    use windows_sys::Win32::UI::Shell::{
        Shell_NotifyIconW, NIF_ICON, NIF_INFO, NIF_TIP, NIIF_INFO, NIM_ADD, NIM_DELETE, NIM_MODIFY,
        NOTIFYICONDATAW,
    };
    use windows_sys::Win32::UI::WindowsAndMessaging::{
        LoadIconW, SendMessageW, ICON_BIG, IDI_APPLICATION, WM_GETICON,
    };
    // Each message moves this on, so an older message's timer doesn't remove a newer one.
    static SHOWN: AtomicU64 = AtomicU64::new(0);

    let Ok(hwnd) = window.hwnd() else { return };
    let hwnd = hwnd.0 as isize;
    let data = move |flags| {
        let mut d: NOTIFYICONDATAW = unsafe { std::mem::zeroed() };
        d.cbSize = std::mem::size_of::<NOTIFYICONDATAW>() as u32;
        d.hWnd = hwnd as _;
        d.uID = 7;
        d.uFlags = flags;
        d
    };
    let mut d = data(NIF_ICON | NIF_TIP | NIF_INFO);
    let icon = unsafe { SendMessageW(hwnd as _, WM_GETICON, ICON_BIG as usize, 0) };
    d.hIcon = if icon != 0 {
        icon as _
    } else {
        unsafe { LoadIconW(std::ptr::null_mut(), IDI_APPLICATION) }
    };
    copy(&mut d.szTip, "LAN Skin Tester");
    copy(&mut d.szInfoTitle, title);
    copy(&mut d.szInfo, body);
    d.dwInfoFlags = NIIF_INFO;
    unsafe {
        if Shell_NotifyIconW(NIM_MODIFY, &d) == 0 {
            Shell_NotifyIconW(NIM_ADD, &d);
        }
    }
    let this = SHOWN.fetch_add(1, Ordering::Relaxed) + 1;
    std::thread::spawn(move || {
        std::thread::sleep(std::time::Duration::from_secs(20));
        if SHOWN.load(Ordering::Relaxed) == this {
            let d = data(0);
            unsafe { Shell_NotifyIconW(NIM_DELETE, &d) };
        }
    });
}

#[cfg(not(windows))]
pub fn show(_window: &tauri::WebviewWindow, _title: &str, _body: &str) {}

/// Copies text into a fixed UTF-16 field, cut to fit and zero-ended.
#[cfg(windows)]
fn copy<const N: usize>(field: &mut [u16; N], text: &str) {
    for (slot, unit) in field.iter_mut().take(N - 1).zip(text.encode_utf16()) {
        *slot = unit;
    }
}
