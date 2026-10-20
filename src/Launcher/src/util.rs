//! Small shared helpers: data folders, safe file writes, JSON naming, HTTP.

use serde_json::Value;
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::OnceLock;
use std::time::Duration;

static DATA_DIR: OnceLock<PathBuf> = OnceLock::new();

/// %LocalAppData%\LanSkinTester: item data, pictures, inventory, theme, session markers. Tests use a temp folder.
pub fn data_dir() -> &'static Path {
    DATA_DIR.get_or_init(|| match cfg!(test) {
        true => std::env::temp_dir().join(format!("lst-tests-{}", std::process::id())),
        false => dirs::data_local_dir().unwrap_or_else(std::env::temp_dir).join("LanSkinTester"),
    })
}

/// %AppData%\LanSkinTester\launcher.json.
pub fn settings_file() -> PathBuf {
    dirs::data_dir().unwrap_or_else(std::env::temp_dir).join("LanSkinTester").join("launcher.json")
}

/// Writes a temp file and swaps it in, so a crash or power cut mid-write leaves the old file whole.
pub fn safe_write(path: &Path, text: &str) -> std::io::Result<()> {
    let temp = PathBuf::from(format!("{}.tmp", path.display()));
    fs::write(&temp, text)?;
    fs::rename(&temp, path)
}

/// Moves a file that can't be read aside ("inventory.broken-20261020-153000.json"), so the next save doesn't
/// overwrite what may still be rescued by hand. Returns the new name, or None when it couldn't be moved.
pub fn set_aside(path: &Path) -> Option<PathBuf> {
    let stem = path.file_stem()?.to_string_lossy();
    let ext = path.extension().map(|e| format!(".{}", e.to_string_lossy())).unwrap_or_default();
    let aside = path.with_file_name(format!("{stem}.broken-{}{ext}", chrono::Local::now().format("%Y%m%d-%H%M%S")));
    fs::rename(path, &aside).ok().map(|_| aside)
}

/// The page's JSON names: System.Text.Json's camelCase ("TeamSizeCT" -> "teamSizeCT", "IP" -> "ip").
pub fn camel_name(name: &str) -> String {
    let mut c: Vec<char> = name.chars().collect();
    for i in 0..c.len() {
        if i == 1 && !c[i].is_uppercase() {
            break;
        }
        if i > 0 && i + 1 < c.len() && !c[i + 1].is_uppercase() {
            if c[i + 1] == ' ' {
                c[i] = c[i].to_ascii_lowercase();
            }
            break;
        }
        c[i] = c[i].to_ascii_lowercase();
    }
    c.into_iter().collect()
}

/// A file-format value (PascalCase, like the plugin and older launchers wrote it) as the page sees it.
pub fn camel(v: Value) -> Value {
    match v {
        Value::Object(map) => Value::Object(map.into_iter().map(|(k, v)| (camel_name(&k), camel(v))).collect()),
        Value::Array(a) => Value::Array(a.into_iter().map(camel).collect()),
        v => v,
    }
}

/// Serializes a file-format value for the page.
pub fn web<T: serde::Serialize>(v: &T) -> Value {
    camel(serde_json::to_value(v).unwrap_or(Value::Null))
}

/// JSON nulls (older files, hand edits) become "use the default" instead of failing the whole file.
pub fn strip_nulls(v: &mut Value) {
    match v {
        Value::Object(map) => {
            map.retain(|_, v| !v.is_null());
            map.values_mut().for_each(strip_nulls);
        }
        Value::Array(a) => a.iter_mut().for_each(strip_nulls),
        _ => {}
    }
}

/// Reads a JSON file in the file format, tolerating nulls.
pub fn read_json<T: serde::de::DeserializeOwned>(text: &str) -> serde_json::Result<T> {
    let mut v: Value = serde_json::from_str(text.trim_start_matches('\u{feff}'))?;
    strip_nulls(&mut v);
    serde_json::from_value(v)
}

pub fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02X}")).collect()
}

pub fn random_hex(bytes: usize) -> String {
    hex(&(0..bytes).map(|_| rand::random::<u8>()).collect::<Vec<_>>())
}

pub fn unix_now() -> i64 {
    chrono::Utc::now().timestamp()
}

/// "[12:34:56] line" like the activity log shows it.
pub fn stamp(line: &str) -> String {
    format!("[{}] {line}", chrono::Local::now().format("%H:%M:%S"))
}

/// The launcher's HTTP client. Steam's community pages refuse requests that don't say what they are.
pub fn http_agent(timeout_secs: u64) -> ureq::Agent {
    ureq::Agent::config_builder()
        .timeout_global(Some(Duration::from_secs(timeout_secs)))
        .user_agent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LanSkinTester/2")
        .http_status_as_error(false)
        .build()
        .into()
}

pub type HttpResult<T> = Result<T, String>;

/// GET as text; non-2xx answers are errors ("HTTP 404").
pub fn get_text(http: &ureq::Agent, url: &str) -> HttpResult<String> {
    let mut r = http.get(url).call().map_err(|e| e.to_string())?;
    if !r.status().is_success() {
        return Err(format!("HTTP {}", r.status()));
    }
    r.body_mut().with_config().limit(256 * 1024 * 1024).read_to_string().map_err(|e| e.to_string())
}

pub fn get_bytes(http: &ureq::Agent, url: &str) -> HttpResult<Vec<u8>> {
    let mut r = http.get(url).call().map_err(|e| e.to_string())?;
    if !r.status().is_success() {
        return Err(format!("HTTP {}", r.status()));
    }
    r.body_mut().with_config().limit(256 * 1024 * 1024).read_to_vec().map_err(|e| e.to_string())
}

/// Streams a download to a file, reporting 0..1 (or -1 when the size is unknown).
pub fn download_to_file(http: &ureq::Agent, url: &str, path: &Path, progress: &dyn Fn(f64)) -> HttpResult<()> {
    use std::io::{Read, Write};
    let mut r = http.get(url).call().map_err(|e| e.to_string())?;
    if !r.status().is_success() {
        return Err(format!("HTTP {} for {url}", r.status()));
    }
    let total = r.body().content_length().unwrap_or(0) as f64;
    let mut reader = r.body_mut().with_config().limit(u64::MAX).reader();
    let mut file = fs::File::create(path).map_err(|e| e.to_string())?;
    let mut buffer = vec![0u8; 81920];
    let mut done = 0f64;
    loop {
        let read = reader.read(&mut buffer).map_err(|e| e.to_string())?;
        if read == 0 {
            return Ok(());
        }
        file.write_all(&buffer[..read]).map_err(|e| e.to_string())?;
        done += read as f64;
        progress(if total > 0.0 { done / total } else { -1.0 });
    }
}

/// Percent-encoding for URL parts (Uri.EscapeDataString).
pub fn escape(text: &str) -> String {
    text.bytes()
        .map(|b| match b {
            b'A'..=b'Z' | b'a'..=b'z' | b'0'..=b'9' | b'-' | b'_' | b'.' | b'~' => (b as char).to_string(),
            _ => format!("%{b:02X}"),
        })
        .collect()
}

/// Uri.UnescapeDataString: %XX back to bytes, invalid sequences kept as they are.
pub fn unescape(text: &str) -> String {
    let bytes = text.as_bytes();
    let mut out = Vec::with_capacity(bytes.len());
    let mut i = 0;
    while i < bytes.len() {
        if bytes[i] == b'%' && i + 2 < bytes.len() {
            if let Some(b) = text.get(i + 1..i + 3).and_then(|h| u8::from_str_radix(h, 16).ok()) {
                out.push(b);
                i += 3;
                continue;
            }
        }
        out.push(bytes[i]);
        i += 1;
    }
    String::from_utf8_lossy(&out).into_owned()
}

/// The last lines of a file another process may still be writing.
pub fn tail(path: &Path, lines: usize, max_bytes: u64) -> Vec<String> {
    use std::io::{Read, Seek, SeekFrom};
    let Ok(mut f) = fs::File::open(path) else { return vec![] };
    let len = f.metadata().map(|m| m.len()).unwrap_or(0);
    if len > max_bytes {
        let _ = f.seek(SeekFrom::End(-(max_bytes as i64)));
    }
    let mut bytes = Vec::new();
    if f.read_to_end(&mut bytes).is_err() {
        return vec![];
    }
    let all: Vec<String> = String::from_utf8_lossy(&bytes)
        .split('\n')
        .map(|l| l.trim_end_matches('\r').to_string())
        .filter(|l| !l.trim().is_empty())
        .collect();
    all[all.len().saturating_sub(lines)..].to_vec()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn camel_like_dotnet() {
        assert_eq!(camel_name("TeamSizeCT"), "teamSizeCT");
        assert_eq!(camel_name("SkinsCT"), "skinsCT");
        assert_eq!(camel_name("IP"), "ip");
        assert_eq!(camel_name("Mode"), "mode");
        assert_eq!(camel_name("de_dust2"), "de_dust2");
        assert_eq!(camel_name("7"), "7");
    }

    #[test]
    fn safe_write_replaces_the_file() {
        let file = std::env::temp_dir().join(format!("lst-safe-{}.json", random_hex(6)));
        safe_write(&file, "one").unwrap();
        safe_write(&file, "two").unwrap();
        assert_eq!(fs::read_to_string(&file).unwrap(), "two");
        assert!(!PathBuf::from(format!("{}.tmp", file.display())).exists());
        fs::remove_file(file).unwrap();
    }

    #[test]
    fn damaged_file_is_set_aside() {
        let dir = std::env::temp_dir().join(format!("lst-aside-{}", random_hex(6)));
        fs::create_dir_all(&dir).unwrap();
        let file = dir.join("inventory.json");
        fs::write(&file, "{broken").unwrap();
        let aside = set_aside(&file).unwrap();
        assert!(!file.exists());
        assert_eq!(fs::read_to_string(&aside).unwrap(), "{broken");
        assert!(aside.file_name().unwrap().to_string_lossy().starts_with("inventory.broken-"));
        fs::remove_dir_all(dir).unwrap();
    }

    #[test]
    fn escapes_round_trip() {
        assert_eq!(escape("a b/ä"), "a%20b%2F%C3%A4");
        assert_eq!(unescape("a%20b%2F%C3%A4"), "a b/ä");
        assert_eq!(unescape("100%"), "100%");
    }
}
