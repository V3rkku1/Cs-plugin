//! Item pictures, downloaded once and kept on disk (%LocalAppData%\LanSkinTester\images), plus the theme's
//! background picture and custom.css. The page loads them through the lst:// protocol.

use crate::util;
use sha1::{Digest, Sha1};
use std::fs;
use std::path::PathBuf;
use std::sync::{Condvar, Mutex};

/// At most this many downloads at once.
static SLOTS: (Mutex<usize>, Condvar) = (Mutex::new(6), Condvar::new());

/// The picture's bytes (disk cache first), or None when it can't be downloaded.
pub fn image(http: &ureq::Agent, url: &str, size: u32) -> Option<Vec<u8>> {
    if !url.starts_with("https://") {
        return None;
    }
    // Steam's economy image server resizes on request: .../economy/image/<id>/256fx256f
    let url = if url.contains("/economy/image/") {
        format!("{}/{size}fx{size}f", url.trim_end_matches('/'))
    } else {
        url.to_string()
    };
    let dir = util::data_dir().join("images");
    let file = dir.join(format!(
        "{}.png",
        &util::hex(&Sha1::digest(url.as_bytes()))[..20]
    ));
    if let Ok(bytes) = fs::read(&file) {
        return Some(bytes);
    }
    let (lock, cvar) = &SLOTS;
    let mut free = cvar.wait_while(lock.lock().unwrap(), |n| *n == 0).unwrap();
    *free -= 1;
    drop(free);
    let bytes = util::get_bytes(http, &url).ok();
    *lock.lock().unwrap() += 1;
    cvar.notify_one();
    let bytes = bytes?;
    let _ = fs::create_dir_all(&dir);
    let _ = fs::write(file, &bytes);
    Some(bytes)
}

pub fn content_type(bytes: &[u8]) -> &'static str {
    match bytes {
        [0xFF, 0xD8, ..] => "image/jpeg",
        [b'R', b'I', ..] => "image/webp",
        _ => "image/png",
    }
}

pub const IMAGE_EXTENSIONS: [&str; 4] = [".png", ".jpg", ".jpeg", ".webp"];

pub fn background_file(ext: &str) -> PathBuf {
    util::data_dir().join(format!("theme-background{ext}"))
}

/// The background picture on disk, if one was chosen.
pub fn background_path() -> Option<PathBuf> {
    IMAGE_EXTENSIONS
        .iter()
        .map(|e| background_file(e))
        .find(|p| p.exists())
}

pub fn custom_css_file() -> PathBuf {
    util::data_dir().join("custom.css")
}
