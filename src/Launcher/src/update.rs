//! Self-update: compares the commit this launcher was built from (build.sh passes it as LST_COMMIT) with the
//! "latest" release tag on GitHub, downloads the new LanSkinTesterLauncher.exe and swaps it in after this
//! launcher exits (the running exe is renamed aside, the new one takes its name and starts once this one closes).

use crate::util;
use serde_json::Value;
use std::fs;
use std::path::{Path, PathBuf};
use std::time::Duration;

pub const RELEASE_URL: &str = "https://github.com/V3rkku1/Cs-plugin/releases/tag/latest";
const TAG_API: &str = "https://api.github.com/repos/V3rkku1/Cs-plugin/git/ref/tags/latest";
const COMPARE_API: &str = "https://api.github.com/repos/V3rkku1/Cs-plugin/compare";
/// The exe straight from the release's download link: no GitHub API limit, and it keeps working while a new build
/// replaces the release's files (the link answers 404 for that minute).
const DOWNLOAD_URL: &str =
    "https://github.com/V3rkku1/Cs-plugin/releases/download/latest/LanSkinTesterLauncher.exe";

/// Full commit hash of this build, or None for local builds without one.
pub fn local_commit() -> Option<&'static str> {
    option_env!("LST_COMMIT").filter(|c| c.len() >= 7)
}

pub fn short_version() -> String {
    local_commit().map_or("dev build".into(), |c| format!("build {}", &c[..7]))
}

/// A newer build on GitHub: its commit and the changes since this one (newest first).
#[derive(Clone)]
pub struct Update {
    pub commit: String,
    pub notes: Vec<String>,
}

fn github_get(
    http: &ureq::Agent,
    url: &str,
) -> Result<ureq::http::Response<ureq::Body>, ureq::Error> {
    http.get(url)
        .header("User-Agent", "LanSkinTester-Launcher/1.0")
        .header("Accept", "application/vnd.github+json")
        .call()
}

/// The newer build (or None when this is the newest), or why GitHub couldn't be asked.
pub fn check(http: &ureq::Agent) -> Result<Option<Update>, String> {
    let local =
        local_commit().ok_or("this is a dev build, so it can't tell which build is newer")?;
    let mut r = github_get(http, TAG_API).map_err(|_| "no connection to GitHub")?;
    if !r.status().is_success() {
        let limited = r
            .headers()
            .get("x-ratelimit-remaining")
            .is_some_and(|v| v == "0");
        return Err(if limited {
            "GitHub's limit for this network is used up - try again in an hour".into()
        } else {
            format!("GitHub answered {}", r.status())
        });
    }
    let doc: Value =
        serde_json::from_str(&r.body_mut().read_to_string().map_err(|e| e.to_string())?)
            .map_err(|e| e.to_string())?;
    let remote = doc["object"]["sha"]
        .as_str()
        .ok_or("GitHub's answer has no commit")?;
    if remote.eq_ignore_ascii_case(local) {
        return Ok(None);
    }
    Ok(Some(Update {
        commit: remote.into(),
        notes: notes(http, local, remote),
    }))
}

/// First lines of the commit messages between the two builds (the "what's new" list).
fn notes(http: &ureq::Agent, local: &str, remote: &str) -> Vec<String> {
    let doc: Option<Value> = github_get(http, &format!("{COMPARE_API}/{local}...{remote}"))
        .ok()
        .filter(|r| r.status().is_success())
        .and_then(|mut r| r.body_mut().read_to_string().ok())
        .and_then(|t| serde_json::from_str(&t).ok());
    let Some(commits) = doc.as_ref().and_then(|d| d["commits"].as_array()) else {
        return vec![];
    };
    commits
        .iter()
        .rev()
        .filter_map(|c| c["commit"]["message"].as_str())
        .map(|m| m.lines().next().unwrap_or("").trim().to_string())
        .filter(|l| !l.is_empty() && !l.starts_with("Merge "))
        .take(15)
        .collect()
}

pub fn current_exe() -> PathBuf {
    std::env::current_exe().unwrap_or_default()
}

/// Can this launcher replace its own exe (not under a read-only Program Files folder)?
pub fn can_self_update() -> bool {
    let probe = current_exe().with_file_name(".lst-write-test");
    fs::write(&probe, "")
        .and_then(|_| fs::remove_file(&probe))
        .is_ok()
}

fn update_dir() -> PathBuf {
    std::env::temp_dir().join("LanSkinTester-update")
}

/// Written by the swap: "ok". Read by the next launcher start.
fn result_file() -> PathBuf {
    update_dir().join("result.txt")
}

/// Downloads the new exe and returns its path. Tries 4 times over about a minute.
pub fn download(
    http: &ureq::Agent,
    progress: &dyn Fn(f64),
    retrying: &dyn Fn(String),
) -> Result<PathBuf, String> {
    let mut attempt = 1;
    loop {
        match download_once(http, progress) {
            Ok(p) => return Ok(p),
            Err(e) if attempt < 4 => {
                retrying(e);
                std::thread::sleep(Duration::from_secs(15 * attempt));
                attempt += 1;
            }
            Err(e) => return Err(e),
        }
    }
}

fn download_once(http: &ureq::Agent, progress: &dyn Fn(f64)) -> Result<PathBuf, String> {
    let dir = update_dir();
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    // A fresh name each time: an old download may still be open (virus scan) and can't be overwritten.
    for old in fs::read_dir(&dir).map_err(|e| e.to_string())?.flatten() {
        if old
            .file_name()
            .to_string_lossy()
            .starts_with("LanSkinTesterLauncher-")
        {
            let _ = fs::remove_file(old.path());
        }
    }
    let target = dir.join(format!(
        "LanSkinTesterLauncher-{}.exe",
        chrono::Utc::now().format("%Y%m%d%H%M%S")
    ));
    util::download_to_file(http, DOWNLOAD_URL, &target, &|p| {
        if p >= 0.0 {
            progress(p)
        }
    })?;
    // Sanity check: a real Windows exe of a plausible size.
    let bytes = fs::read(&target).map_err(|e| e.to_string())?;
    if bytes.len() < 1_000_000 || !bytes.starts_with(b"MZ") {
        return Err("The downloaded file isn't a valid launcher.".into());
    }
    Ok(target)
}

/// Moves this exe aside (Windows allows that while it runs), copies the new one in its place and starts it with
/// --after <pid>, so it waits for this one to exit. The caller then exits; the old copy goes on the next start.
pub fn replace_after_exit(new_exe: &Path) -> std::io::Result<()> {
    let cur = current_exe();
    let old = old_exe();
    let _ = fs::remove_file(&old);
    fs::rename(&cur, &old)?;
    if let Err(e) = fs::copy(new_exe, &cur) {
        let _ = fs::rename(&old, &cur);
        return Err(e);
    }
    let _ = fs::remove_file(new_exe);
    let _ = fs::write(result_file(), "ok");
    std::process::Command::new(&cur)
        .args(["--after", &std::process::id().to_string()])
        .spawn()
        .map(drop)
}

fn old_exe() -> PathBuf {
    current_exe().with_extension("old.exe")
}

/// The updated launcher waits here (at most a minute) until the old one has closed and saved its window.
pub fn wait_for_exit(pid: u32) {
    let pid = sysinfo::Pid::from_u32(pid);
    let mut sys = sysinfo::System::new();
    for _ in 0..300 {
        sys.refresh_processes_specifics(
            sysinfo::ProcessesToUpdate::Some(&[pid]),
            true,
            sysinfo::ProcessRefreshKind::nothing(),
        );
        if sys.process(pid).is_none() {
            break;
        }
        std::thread::sleep(Duration::from_millis(200));
    }
    let _ = fs::remove_file(old_exe());
}

/// The last swap's outcome ("ok" / "failed: ..."), once; None when there was none.
pub fn take_result() -> Option<String> {
    let text = fs::read_to_string(result_file())
        .ok()?
        .trim()
        .trim_start_matches('\u{feff}')
        .to_string();
    let _ = fs::remove_file(result_file());
    Some(text)
}
