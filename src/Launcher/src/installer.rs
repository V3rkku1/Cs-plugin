//! Downloads and installs Metamod, CounterStrikeSharp, the plugin and the optional server plugins, and parks
//! plugins outside the plugins folder when they should not load.

use crate::game::{self, GameFolder};
use crate::server::ServerSettings;
use crate::util;
use regex::Regex;
use serde_json::Value;
use std::fs;
use std::io;
use std::path::{Path, PathBuf};

include!(concat!(env!("OUT_DIR"), "/plugin_files.rs"));

pub const METAMOD_PAGE: &str = "https://www.sourcemm.net/downloads.php?branch=dev";
pub const CSS_PAGE: &str = "https://github.com/roflmuffin/CounterStrikeSharp/releases/latest";
const METAMOD_LATEST: &str = "https://mms.alliedmods.net/mmsdrop/2.0/mmsource-latest-windows";
const METAMOD_DROP: &str = "https://mms.alliedmods.net/mmsdrop/2.0/";
const CSS_API: &str = "https://api.github.com/repos/roflmuffin/CounterStrikeSharp/releases/latest";
// Instadefuse: B3none, GPL-3.0. Allocator: Yoni Lerner's cs2-retakes-allocator. Bot Hider / Bot Controller:
// XBribo (AGPL-3.0). Deathmatch: NockyCZ (MIT). Bot Improver: ed0ard.
const RETAKES_API: &str = "https://api.github.com/repos/B3none/cs2-retakes/releases/latest";
const INSTADEFUSE_API: &str = "https://api.github.com/repos/B3none/cs2-instadefuse/releases/latest";
const ALLOCATOR_API: &str =
    "https://api.github.com/repos/yonilerner/cs2-retakes-allocator/releases/latest";
const BOT_HIDER_API: &str = "https://api.github.com/repos/XBribo/CS2-Bot-Hider/releases/latest";
const DEATHMATCH_API: &str = "https://api.github.com/repos/NockyCZ/CS2-Deathmatch/releases/latest";
const BOT_IMPROVER_API: &str =
    "https://api.github.com/repos/ed0ard/CS2-Bot-Improver/releases/latest";
const BOT_CONTROLLER_API: &str =
    "https://api.github.com/repos/XBribo/CS2-Bot-Controller/releases/latest";

const ALLOCATOR_TREES: [&str; 4] = [
    "plugins/RetakesAllocator",
    "plugins/SharpModMenu",
    "shared/SharpModMenu",
    "shared/CSSUniversalMenuAPI",
];
/// The CounterStrikeSharp plugins inside Bot Improver (BotAI is also its own download).
const IMPROVER_PLUGINS: [&str; 6] = [
    "BotAimImprover",
    "NadeSystem",
    "BotBuy",
    "BotState",
    "RoundDamageRecap",
    "BotRandomizer",
];

pub type R<T> = Result<T, String>;

fn has(name: &str, part: &str) -> bool {
    name.to_lowercase().contains(&part.to_lowercase())
}

fn zip_name(name: &str) -> bool {
    name.to_lowercase().ends_with(".zip")
}

/// The asset name the earliest matching pick chooses, or None.
pub fn pick_asset<'a>(names: &[&'a str], picks: &[fn(&str) -> bool]) -> Option<&'a str> {
    picks
        .iter()
        .find_map(|pick| names.iter().find(|n| pick(n)).copied())
}

/// Bot Improver's Windows packages: "_rules_unchanged" keeps the game rules (not every release has it).
pub const BOT_IMPROVER_ASSETS: [fn(&str) -> bool; 2] = [
    |n| has(n, "rules_unchanged") && zip_name(n) && !has(n, "linux"),
    |n| has(n, "BotImprover") && zip_name(n) && !has(n, "linux"),
];

/// "a/b/c" under `root`, refusing anything that climbs out of it.
fn safe_join(root: &Path, relative: &str) -> Option<PathBuf> {
    let mut path = root.to_path_buf();
    for part in relative.split('/') {
        if part.is_empty() || part == "." || part == ".." || part.contains(':') {
            return None;
        }
        path.push(part);
    }
    Some(path)
}

fn file_name(path: &Path) -> String {
    path.file_name()
        .map(|f| f.to_string_lossy().into_owned())
        .unwrap_or_default()
}

/// Every file in a zip: its name with forward slashes, and a way to write it out.
fn for_each_entry(zip_path: &Path, mut f: impl FnMut(&str, &mut dyn io::Read) -> R<()>) -> R<()> {
    let mut zip = zip::ZipArchive::new(fs::File::open(zip_path).map_err(|e| e.to_string())?)
        .map_err(|e| format!("{}: {e}", file_name(zip_path)))?;
    for i in 0..zip.len() {
        let mut entry = zip.by_index(i).map_err(|e| e.to_string())?;
        let name = entry.name().replace('\\', "/");
        f(&name, &mut entry)?;
    }
    Ok(())
}

fn extract(target: &Path, data: &mut dyn io::Read) -> R<()> {
    fs::create_dir_all(target.parent().unwrap()).map_err(|e| e.to_string())?;
    let mut out = fs::File::create(target).map_err(|e| format!("{}: {e}", target.display()))?;
    io::copy(data, &mut out).map_err(|e| e.to_string())?;
    Ok(())
}

pub struct Installer<'a> {
    pub game: &'a GameFolder,
    pub http: &'a ureq::Agent,
    pub log: &'a dyn Fn(String),
    /// Download progress of the current step, 0..1 (or -1 when the size is unknown).
    pub progress: &'a dyn Fn(f64),
}

impl Installer<'_> {
    fn log(&self, line: impl Into<String>) {
        (self.log)(line.into())
    }

    fn download(&self, url: &str) -> R<PathBuf> {
        let name = url.rsplit('/').next().unwrap_or("download.zip");
        let path = std::env::temp_dir().join(format!("LanSkinTester-{name}"));
        util::download_to_file(self.http, url, &path, self.progress)?;
        Ok(path)
    }

    /// The files of a GitHub project's latest release: (name, download url).
    fn release_assets(&self, api: &str, label: &str) -> R<Vec<(String, String)>> {
        self.log(format!("Looking up the latest {label} release..."));
        let doc: Value =
            serde_json::from_str(&util::get_text(self.http, api)?).map_err(|e| e.to_string())?;
        Ok(doc["assets"]
            .as_array()
            .into_iter()
            .flatten()
            .map(|a| {
                (
                    a["name"].as_str().unwrap_or("").to_string(),
                    a["browser_download_url"].as_str().unwrap_or("").to_string(),
                )
            })
            .filter(|a| !a.1.is_empty())
            .collect())
    }

    /// Downloads the first release asset that matches; with several picks, the earliest pick that matches wins.
    fn release_asset(&self, api: &str, label: &str, picks: &[fn(&str) -> bool]) -> R<PathBuf> {
        let assets = self.release_assets(api, label)?;
        let names: Vec<&str> = assets.iter().map(|a| a.0.as_str()).collect();
        let name = pick_asset(&names, picks)
            .ok_or(format!("No matching zip in the latest {label} release."))?;
        self.log(format!("Downloading {label}..."));
        self.download(&assets.iter().find(|a| a.0 == name).unwrap().1)
    }

    pub fn install_metamod(&self) -> R<()> {
        self.log("Looking up the latest Metamod 2.x build...");
        let file = util::get_text(self.http, METAMOD_LATEST)?
            .trim()
            .to_string();
        self.log(format!("Downloading {file}..."));
        self.install_zip(&self.download(&format!("{METAMOD_DROP}{file}"))?)?;
        self.log("Metamod installed.");
        Ok(())
    }

    pub fn install_css(&self) -> R<()> {
        let assets = self.release_assets(CSS_API, "CounterStrikeSharp")?;
        let (name, url) = assets
            .iter()
            .find(|a| {
                a.0.contains("with-runtime") && a.0.contains("windows") && a.0.ends_with(".zip")
            })
            .ok_or(
                "No Windows with-runtime build found in the latest CounterStrikeSharp release.",
            )?;
        self.log(format!("Downloading {name}..."));
        self.install_zip(&self.download(url)?)?;
        self.log("CounterStrikeSharp installed.");
        Ok(())
    }

    fn install_release(&self, api: &str, label: &str, pick: fn(&str) -> bool) -> R<()> {
        let assets = self.release_assets(api, label)?;
        let (name, url) = assets
            .iter()
            .find(|a| pick(&a.0))
            .ok_or(format!("No zip found in the latest {label} release."))?;
        self.log(format!("Downloading {name}..."));
        self.install_css_bundle(&self.download(url)?)?;
        self.log(format!("{label} installed."));
        Ok(())
    }

    pub fn install_retakes(&self) -> R<()> {
        self.install_release(RETAKES_API, "cs2-retakes", |n| {
            n.to_lowercase().starts_with("retakesplugin")
                && n.ends_with(".zip")
                && !has(n, "no-map-configs")
        })
    }
    pub fn install_instadefuse(&self) -> R<()> {
        self.install_release(INSTADEFUSE_API, "cs2-instadefuse", zip_name)
    }
    pub fn install_allocator(&self) -> R<()> {
        self.install_release(ALLOCATOR_API, "retakes allocator", |n| {
            has(n, "Allocator") && zip_name(n)
        })
    }
    pub fn install_deathmatch(&self) -> R<()> {
        self.install_release(DEATHMATCH_API, "Deathmatch plugin", zip_name)
    }

    pub fn install_bot_hider(&self) -> R<()> {
        // The zip has addons/ at its root.
        self.install_zip(&self.release_asset(
            BOT_HIDER_API,
            "Bot Hider",
            &[|n| has(n, "windows")],
        )?)?;
        self.log("Bot Hider installed.");
        Ok(())
    }

    pub fn install_bot_controller(&self) -> R<()> {
        self.install_zip(&self.release_asset(
            BOT_CONTROLLER_API,
            "Bot Controller",
            &[|n| has(n, "MM-windows")],
        )?)?;
        self.install_zip(&self.release_asset(
            BOT_CONTROLLER_API,
            "Bot Controller (CounterStrikeSharp part)",
            &[|n| has(n, "CSS-API")],
        )?)?;
        self.log("Bot Controller installed.");
        Ok(())
    }

    /// Bot Improver's package changes gameinfo.gi, Valve's mode configs and bot profiles too. Only its plugins are
    /// taken: the launcher manages gameinfo.gi itself.
    pub fn install_bot_improver(&self) -> R<()> {
        let zip = self.release_asset(BOT_IMPROVER_API, "Bot Improver", &BOT_IMPROVER_ASSETS)?;
        let mut count = 0;
        for_each_entry(&zip, |full, data| {
            let at = if full.starts_with("addons/") {
                0
            } else {
                match full.find("/addons/") {
                    Some(i) => i + 1,
                    None => return Ok(()),
                }
            };
            let relative = &full[at..];
            if full.ends_with('/') || !improver_file(relative) {
                return Ok(());
            }
            let Some(target) = safe_join(&self.game.csgo(), relative) else {
                return Ok(());
            };
            if relative.starts_with("addons/counterstrikesharp/configs/") && target.exists() {
                return Ok(());
            }
            extract(&target, data)?;
            count += 1;
            Ok(())
        })?;
        if count == 0 {
            return Err("The Bot Improver package had no plugin files.".into());
        }
        self.log(format!("Extracted {count} Bot Improver files."));
        self.log("Bot Improver installed.");
        Ok(())
    }

    /// Extracts a CounterStrikeSharp plugin zip: an addons/counterstrikesharp tree, a bundle rooted at
    /// plugins/shared/configs, or a single plugin folder.
    pub fn install_css_bundle(&self, zip: &Path) -> R<()> {
        let root = self.game.css();
        let mut count = 0;
        for_each_entry(zip, |full, data| {
            let full = full.trim_start_matches('/');
            if full.is_empty() || full.ends_with('/') {
                return Ok(());
            }
            let Some(relative) = css_relative(full) else {
                return Ok(());
            };
            let Some(target) = safe_join(&root, &relative) else {
                return Ok(());
            };
            if relative.to_lowercase().starts_with("configs/") && target.exists() {
                return Ok(());
            }
            extract(&target, data)?;
            count += 1;
            Ok(())
        })?;
        if count == 0 {
            return Err(format!(
                "{} has no CounterStrikeSharp plugin files.",
                file_name(zip)
            ));
        }
        self.log(format!("Extracted {count} files from {}.", file_name(zip)));
        Ok(())
    }

    /// Extracts a Metamod or CounterStrikeSharp zip's addons/ tree into game\csgo (at its root or inside a
    /// folder). Existing CounterStrikeSharp and SourceMod config files are kept.
    pub fn install_zip(&self, zip: &Path) -> R<()> {
        let csgo = self.game.csgo();
        let mut count = 0;
        for_each_entry(zip, |full, data| {
            let at = if full.starts_with("addons/") {
                0
            } else {
                match full.find("/addons/") {
                    Some(i) => i + 1,
                    None => return Ok(()),
                }
            };
            let relative = &full[at..];
            // Never write outside the addons folder.
            let Some(target) = safe_join(&csgo, relative.trim_end_matches('/')) else {
                return Ok(());
            };
            if relative == "addons/" || !relative.starts_with("addons/") {
                return Ok(());
            }
            if relative.ends_with('/') {
                return fs::create_dir_all(&target).map_err(|e| e.to_string());
            }
            if relative.starts_with("addons/counterstrikesharp/configs/") && target.exists() {
                return Ok(());
            }
            // SourceMod's own settings (core.cfg, admins): keep. Plugin configs in subfolders update.
            let sm = "addons/sourcemod/configs/";
            if relative.starts_with(sm) && !relative[sm.len()..].contains('/') && target.exists() {
                return Ok(());
            }
            extract(&target, data)?;
            count += 1;
            Ok(())
        })?;
        if count == 0 {
            return Err(format!(
                "{} has no addons folder - is it the right zip?",
                file_name(zip)
            ));
        }
        self.log(format!("Extracted {count} files from {}.", file_name(zip)));
        Ok(())
    }

    /// The plugin itself ships inside the launcher exe.
    pub fn install_plugin(&self) -> R<()> {
        if PLUGIN_FILES.is_empty() {
            return Err(
                "This launcher was built without the plugin inside. Build it with build.sh.".into(),
            );
        }
        for (name, bytes) in PLUGIN_FILES {
            let target = safe_join(&self.game.plugin_dir(), name).unwrap();
            fs::create_dir_all(target.parent().unwrap()).map_err(|e| e.to_string())?;
            fs::write(&target, bytes).map_err(|e| format!("{}: {e}", target.display()))?;
        }
        self.log("LAN Skin Tester plugin installed.");
        Ok(())
    }

    /// The latest Metamod and CounterStrikeSharp versions (None when they can't be asked).
    pub fn latest_versions(&self) -> (Option<String>, Option<String>) {
        // "mmsource-2.0.0-git1380-windows.zip" -> "2.0.0-git1380"
        let metamod = util::get_text(self.http, METAMOD_LATEST).ok().map(|f| {
            let f = f.trim().to_string();
            Regex::new(r"mmsource-(.+)-windows")
                .unwrap()
                .captures(&f)
                .map_or(f.clone(), |c| c[1].to_string())
        });
        let css = util::get_text(self.http, CSS_API)
            .ok()
            .and_then(|t| serde_json::from_str::<Value>(&t).ok())
            .and_then(|d| {
                d["tag_name"]
                    .as_str()
                    .map(|t| t.trim_start_matches('v').to_string())
            });
        (metamod, css)
    }

    /// Removes everything this launcher installed and restores gameinfo.gi; the loadout is backed up first.
    pub fn uninstall_all(&self, backup_dir: &Path) -> R<()> {
        let loadout = self.game.loadout_file();
        if loadout.exists() {
            fs::create_dir_all(backup_dir).map_err(|e| e.to_string())?;
            let backup = backup_dir.join(format!(
                "loadouts-{}.json",
                chrono::Local::now().format("%Y%m%d-%H%M%S")
            ));
            fs::copy(&loadout, &backup).map_err(|e| e.to_string())?;
            self.log(format!("Saved your loadout to {}", backup.display()));
        }
        game::disable_metamod(&self.game.game_info()).map_err(|e| e.to_string())?;
        for dir in [self.game.css(), self.game.addons().join("metamod")] {
            if dir.is_dir() {
                fs::remove_dir_all(&dir).map_err(|e| format!("{}: {e}", dir.display()))?;
                self.log(format!("Removed {}", dir.display()));
            }
        }
        for file in [
            self.game.addons().join("metamod.vdf"),
            self.game.addons().join("metamod_x64.vdf"),
            self.game.cfg_dir().join("lanskintester_server.cfg"),
        ] {
            if file.is_file() {
                fs::remove_file(&file).map_err(|e| e.to_string())?;
                self.log(format!("Removed {}", file.display()));
            }
        }
        self.log("Uninstalled. CS2 is completely back to normal.");
        Ok(())
    }
}

fn improver_file(relative: &str) -> bool {
    let r = relative.to_lowercase();
    if r.starts_with("addons/counterstrikesharp/plugins/disabled/") {
        return false;
    }
    let prefixes = [
        "addons/counterstrikesharp/plugins/",
        "addons/counterstrikesharp/shared/",
        "addons/counterstrikesharp/data/",
        "addons/counterstrikesharp/configs/plugins/",
        "addons/bothider/",
        "addons/botcontroller/",
        "addons/botvision/",
    ];
    prefixes.iter().any(|p| r.starts_with(p))
        || (r.starts_with("addons/metamod/bot") && r.ends_with(".vdf") && !r.contains(".linux."))
}

fn css_relative(full: &str) -> Option<String> {
    let lower = full.to_lowercase();
    let nested = "addons/counterstrikesharp/";
    if let Some(at) = lower.find(nested) {
        return Some(full[at + nested.len()..].to_string());
    }
    if ["plugins/", "shared/", "configs/", "gamedata/"]
        .iter()
        .any(|p| lower.starts_with(p))
    {
        return Some(full.to_string());
    }
    let top = &lower[..lower.find('/').filter(|&s| s > 0)?];
    (top.ends_with("plugin") || top == "retakesallocator" || top == "botai")
        .then(|| format!("plugins/{full}"))
}

// -------------------------------------------------------------------------------------------------
// Plugins parked outside the plugins folder when they should not load
// -------------------------------------------------------------------------------------------------

fn tree_path(game: &GameFolder, relative: &str, parked: bool) -> PathBuf {
    let mut p = if parked {
        game.css().join("lst-parked")
    } else {
        game.css()
    };
    p.extend(relative.split('/'));
    p
}

fn tree_loaded(game: &GameFolder, relative: &str) -> bool {
    tree_path(game, relative, false).is_dir()
}

fn tree_installed(game: &GameFolder, relative: &str, file: &str) -> bool {
    tree_path(game, relative, false).join(file).is_file()
        || tree_path(game, relative, true).join(file).is_file()
}

fn tree_exists(game: &GameFolder, relative: &str) -> bool {
    tree_path(game, relative, false).is_dir() || tree_path(game, relative, true).is_dir()
}

fn set_tree_active(game: &GameFolder, relative: &str, on: bool) -> io::Result<()> {
    let (active, parked) = (
        tree_path(game, relative, false),
        tree_path(game, relative, true),
    );
    // A fresh download lands in the plugins folder: it wins over an older parked copy.
    if on && active.is_dir() {
        if parked.is_dir() {
            fs::remove_dir_all(&parked)?;
        }
        return Ok(());
    }
    let (from, to) = if on {
        (parked, active)
    } else {
        (active, parked)
    };
    if !from.is_dir() {
        return Ok(());
    }
    if to.is_dir() {
        fs::remove_dir_all(&to)?;
    }
    fs::create_dir_all(to.parent().unwrap())?;
    fs::rename(from, to)
}

fn vdf(game: &GameFolder, name: &str) -> PathBuf {
    game.addons().join("metamod").join(format!("{name}.vdf"))
}

fn vdf_off(game: &GameFolder, name: &str) -> PathBuf {
    game.addons()
        .join("metamod")
        .join(format!("{name}.vdf.off"))
}

fn native_installed(game: &GameFolder, name: &str) -> bool {
    vdf(game, name).exists() || vdf_off(game, name).exists()
}

/// Metamod only loads *.vdf files, so a parked native plugin is renamed to .vdf.off.
fn set_native_active(game: &GameFolder, name: &str, on: bool) -> io::Result<()> {
    let (v, off) = (vdf(game, name), vdf_off(game, name));
    if on && off.exists() && !v.exists() {
        fs::rename(off, v)
    } else if !on && v.exists() {
        fs::rename(v, off)
    } else {
        Ok(())
    }
}

pub fn third_party_retakes_loaded(game: &GameFolder) -> bool {
    tree_loaded(game, "plugins/RetakesPlugin")
}
pub fn retakes_installed(game: &GameFolder) -> bool {
    tree_installed(game, "plugins/RetakesPlugin", "RetakesPlugin.dll")
}
pub fn instadefuse_installed(game: &GameFolder) -> bool {
    tree_installed(game, "plugins/InstadefusePlugin", "InstadefusePlugin.dll")
}
pub fn allocator_installed(game: &GameFolder) -> bool {
    tree_installed(game, "plugins/RetakesAllocator", "RetakesAllocator.dll")
}
pub fn bot_hider_installed(game: &GameFolder) -> bool {
    game.addons()
        .join("BotHider")
        .join("bin")
        .join("win64")
        .join("BotHider.dll")
        .is_file()
}
pub fn deathmatch_installed(game: &GameFolder) -> bool {
    tree_installed(game, "plugins/Deathmatch", "Deathmatch.dll")
}
pub fn bot_improver_installed(game: &GameFolder) -> bool {
    tree_exists(game, "plugins/BotState")
}
pub fn bot_controller_installed(game: &GameFolder) -> bool {
    native_installed(game, "BotController")
}

/// True when the retakes plugins on disk are not the set this server wants loaded.
pub fn retake_plugins_mismatch(game: &GameFolder, s: &ServerSettings) -> bool {
    let plugin = s.is_plugin_retakes();
    tree_loaded(game, "plugins/RetakesPlugin") != plugin
        || tree_loaded(game, "plugins/InstadefusePlugin") != (plugin && s.retake_instadefuse)
        || tree_loaded(game, "plugins/RetakesAllocator") != s.uses_allocator()
}

/// Loads or parks retakes, instadefuse and the allocator to match the settings.
pub fn set_retakes_suite(game: &GameFolder, s: &ServerSettings) -> io::Result<()> {
    let plugin = s.is_plugin_retakes();
    set_tree_active(game, "plugins/RetakesPlugin", plugin)?;
    set_tree_active(
        game,
        "plugins/InstadefusePlugin",
        plugin && s.retake_instadefuse,
    )?;
    ALLOCATOR_TREES
        .iter()
        .try_for_each(|t| set_tree_active(game, t, s.uses_allocator()))
}

// Bot Improver brings BotAI, Bot Controller and Bot Hider along.
fn want_hider(s: &ServerSettings) -> bool {
    s.bot_hider || s.bot_improver
}
fn want_controller(s: &ServerSettings) -> bool {
    s.bot_controller || s.bot_improver
}
/// BotRandomizer gives bots random skins: only when Bot Skins is on.
fn want_improver_plugin(s: &ServerSettings, plugin: &str) -> bool {
    s.bot_improver && (plugin != "BotRandomizer" || s.bot_skins)
}

/// True when an installed optional plugin is loaded / parked differently from these settings.
pub fn extra_plugins_mismatch(game: &GameFolder, s: &ServerSettings) -> bool {
    let native =
        |name: &str, want: bool| native_installed(game, name) && vdf(game, name).exists() != want;
    let tree = |relative: &str, want: bool| {
        tree_exists(game, relative) && tree_loaded(game, relative) != want
    };
    native("BotHider", want_hider(s))
        || native("BotController", want_controller(s))
        || tree("plugins/BotHiderImpl", want_hider(s))
        || tree("plugins/BotControllerImpl", want_controller(s))
        || native("BotVision", s.bot_improver)
        || tree("plugins/BotAI", s.bot_improver)
        || tree("plugins/Deathmatch", s.uses_deathmatch_plugin())
        || IMPROVER_PLUGINS
            .iter()
            .any(|p| tree(&format!("plugins/{p}"), want_improver_plugin(s, p)))
}

/// Loads or parks every optional plugin to match the settings.
pub fn set_extra_plugins(game: &GameFolder, s: &ServerSettings) -> io::Result<()> {
    set_native_active(game, "BotHider", want_hider(s))?;
    set_tree_active(game, "plugins/BotHiderImpl", want_hider(s))?;
    if want_hider(s) {
        set_bot_hider_identity(
            game,
            if s.is_plugin_retakes() {
                "bot"
            } else {
                "player"
            },
        );
    }
    set_native_active(game, "BotController", want_controller(s))?;
    // Bot Controller's CounterStrikeSharp half calls into the Metamod half: they load and park together.
    set_tree_active(game, "plugins/BotControllerImpl", want_controller(s))?;
    set_native_active(game, "BotVision", s.bot_improver)?;
    set_tree_active(game, "plugins/BotAI", s.bot_improver)?;
    for p in IMPROVER_PLUGINS {
        set_tree_active(game, &format!("plugins/{p}"), want_improver_plugin(s, p))?;
    }
    set_tree_active(game, "plugins/Deathmatch", s.uses_deathmatch_plugin())?;
    set_tree_active(game, "shared/DeathmatchAPI", s.uses_deathmatch_plugin())
}

/// NockyCZ's Deathmatch rotates "Only Headshot", "Only Deagle", "Only Pistols"… by default: turn that off so it
/// stays on its Default mode (every gun). A missing config is written with just this; the plugin fills the rest.
pub fn plain_deathmatch(game: &GameFolder) {
    let path = game
        .css()
        .join("configs/plugins/Deathmatch/Deathmatch.json");
    let mut root = match fs::read_to_string(&path) {
        Err(_) => serde_json::json!({}),
        // One that can't be read (comments, hand edits) stays as it is.
        Ok(t) => match serde_json::from_str::<Value>(&t) {
            Ok(v) if v.is_object() => v,
            _ => return,
        },
    };
    if root["Gameplay Settings"]["Custom Modes"] == false {
        return;
    }
    if !root["Gameplay Settings"].is_object() {
        root["Gameplay Settings"] = serde_json::json!({});
    }
    root["Gameplay Settings"]["Custom Modes"] = false.into();
    let _ = fs::create_dir_all(path.parent().unwrap());
    let _ = fs::write(path, serde_json::to_string_pretty(&root).unwrap());
}

/// Bot Hider's "player" identity clears the engine's bot flag; B3none's retakes would then take the bots for
/// people, so retakes keeps Valve's bot flag ("bot"). Read when the server starts.
fn set_bot_hider_identity(game: &GameFolder, mode: &str) {
    let path = game.addons().join("BotHider").join("config.json");
    let Some(mut root) = fs::read_to_string(&path)
        .ok()
        .and_then(|t| serde_json::from_str::<Value>(&t).ok())
        .filter(Value::is_object)
    else {
        return;
    };
    if root["identity_mode"].as_str() == Some(mode) {
        return;
    }
    root["identity_mode"] = mode.into();
    let _ = fs::write(path, serde_json::to_string_pretty(&root).unwrap());
}

// -------------------------------------------------------------------------------------------------
// Versions
// -------------------------------------------------------------------------------------------------

/// A string from a DLL's version resource ("FileVersion", "ProductVersion", "Assembly Version").
// shortcut: scans for the UTF-16 key instead of walking the resource tree; fine for the DLLs read here.
pub fn pe_version_string(bytes: &[u8], key: &str) -> Option<String> {
    let mut pattern: Vec<u8> = key
        .encode_utf16()
        .chain([0])
        .flat_map(u16::to_le_bytes)
        .collect();
    if pattern.len() % 2 != 0 {
        pattern.push(0);
    }
    let at = bytes
        .windows(pattern.len())
        .position(|w| w == pattern.as_slice())?
        + pattern.len();
    let words: Vec<u16> = bytes[at..]
        .chunks_exact(2)
        .take(128)
        .map(|c| u16::from_le_bytes([c[0], c[1]]))
        .collect();
    let value: String = char::decode_utf16(
        words
            .into_iter()
            .skip_while(|&w| w == 0)
            .take_while(|&w| w != 0),
    )
    .filter_map(Result::ok)
    .collect();
    (!value.is_empty()).then_some(value)
}

fn short(version: Option<String>) -> Option<String> {
    version.map(|v| v.split(['+', ' ']).next().unwrap_or("").to_string())
}

/// "2.19.0.0" -> [2, 19, 0, 0].
pub fn parse_version(text: &str) -> Option<Vec<u32>> {
    let parts: Option<Vec<u32>> = text.trim().split('.').map(|p| p.parse().ok()).collect();
    parts.filter(|p| p.len() >= 2)
}

fn assembly_version(bytes: &[u8]) -> Option<Vec<u32>> {
    pe_version_string(bytes, "Assembly Version")
        .or_else(|| pe_version_string(bytes, "FileVersion"))
        .and_then(|v| parse_version(&v))
}

/// Version of the plugin inside this launcher.
pub fn bundled_plugin_version() -> Option<Vec<u32>> {
    PLUGIN_FILES
        .iter()
        .find(|f| f.0 == "LanSkinTester.dll")
        .and_then(|f| assembly_version(f.1))
}

pub fn installed_plugin_version(game: &GameFolder) -> Option<Vec<u32>> {
    assembly_version(&fs::read(game.plugin_dir().join("LanSkinTester.dll")).ok()?)
}

/// Major.Minor.Build, like the old launcher showed it.
pub fn v(version: &Option<Vec<u32>>) -> String {
    version.as_ref().map_or("?".into(), |v| {
        v.iter()
            .chain([0, 0, 0].iter())
            .take(3)
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(".")
    })
}

/// True when the installed plugin is exactly the one inside this launcher (any newer build updates it).
pub fn plugin_matches_bundled(game: &GameFolder) -> bool {
    match PLUGIN_FILES.iter().find(|f| f.0 == "LanSkinTester.dll") {
        None => true, // launcher built without the plugin: nothing to compare
        Some((_, bytes)) => {
            fs::read(game.plugin_dir().join("LanSkinTester.dll")).is_ok_and(|b| b == *bytes)
        }
    }
}

/// "1.0.376" read from CounterStrikeSharp's API dll.
pub fn installed_css_version(game: &GameFolder) -> Option<String> {
    short(pe_version_string(
        &fs::read(game.css().join("api").join("CounterStrikeSharp.API.dll")).ok()?,
        "ProductVersion",
    ))
}

pub fn installed_metamod_version(game: &GameFolder) -> Option<String> {
    let dll = fs::read_dir(game.addons().join("metamod").join("bin").join("win64"))
        .ok()?
        .flatten()
        .map(|e| e.path())
        .find(|p| {
            p.extension().is_some_and(|e| e.eq_ignore_ascii_case("dll"))
                && file_name(p).to_lowercase().contains("metamod")
        })?;
    short(pe_version_string(&fs::read(dll).ok()?, "FileVersion"))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_game() -> GameFolder {
        GameFolder::new(std::env::temp_dir().join(format!("lst-game-{}", util::random_hex(6))))
    }

    #[test]
    fn bot_improver_assets() {
        assert_eq!(
            pick_asset(
                &[
                    "CS2BotImprover.zip",
                    "CS2BotImprover_for_Linux.zip",
                    "Panel.v1.4.6.exe"
                ],
                &BOT_IMPROVER_ASSETS
            ),
            Some("CS2BotImprover.zip")
        );
        assert_eq!(
            pick_asset(
                &[
                    "CS2BotImprover.zip",
                    "CS2BotImprover_for_Linux.zip",
                    "CS2BotImprover_rules_unchanged.zip"
                ],
                &BOT_IMPROVER_ASSETS
            ),
            Some("CS2BotImprover_rules_unchanged.zip")
        );
        assert_eq!(
            pick_asset(
                &["CS2BotImprover_for_Linux.zip", "Commands.txt"],
                &BOT_IMPROVER_ASSETS
            ),
            None
        );
    }

    fn install_bot_improver(game: &GameFolder) {
        fs::create_dir_all(game.addons().join("metamod")).unwrap();
        for v in ["BotHider", "BotController", "BotVision"] {
            fs::write(vdf(game, v), "\"Metamod Plugin\" {}").unwrap();
        }
        for p in [
            "BotHiderImpl",
            "BotControllerImpl",
            "BotAI",
            "BotAimImprover",
            "NadeSystem",
            "BotBuy",
            "BotState",
            "RoundDamageRecap",
            "BotRandomizer",
        ] {
            fs::create_dir_all(game.css().join("plugins").join(p)).unwrap();
            fs::write(
                game.css().join("plugins").join(p).join(format!("{p}.dll")),
                "",
            )
            .unwrap();
        }
        fs::create_dir_all(game.addons().join("BotHider")).unwrap();
        fs::write(
            game.addons().join("BotHider").join("config.json"),
            r#"{ "identity_mode": "player", "fake_ping": { "enabled": true } }"#,
        )
        .unwrap();
    }

    fn off() -> ServerSettings {
        ServerSettings {
            bot_improver: false,
            ..Default::default()
        }
    }

    #[test]
    fn bot_improver_off_parks_both_halves() {
        let game = temp_game();
        install_bot_improver(&game);
        assert!(extra_plugins_mismatch(&game, &off()));
        set_extra_plugins(&game, &off()).unwrap();
        assert!(!extra_plugins_mismatch(&game, &off()));
        for v in ["BotHider", "BotController", "BotVision"] {
            assert!(!vdf(&game, v).exists(), "{v}.vdf still loads");
        }
        for p in ["BotHiderImpl", "BotControllerImpl", "BotAI", "BotState"] {
            assert!(
                !game.css().join("plugins").join(p).exists(),
                "{p} still loads"
            );
        }
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn bot_improver_on_loads_everything_but_skins_until_asked() {
        let game = temp_game();
        install_bot_improver(&game);
        set_extra_plugins(&game, &off()).unwrap();
        let mut on = ServerSettings {
            bot_improver: true,
            ..Default::default()
        };
        assert!(extra_plugins_mismatch(&game, &on));
        set_extra_plugins(&game, &on).unwrap();
        assert!(!extra_plugins_mismatch(&game, &on));
        for p in [
            "BotHiderImpl",
            "BotControllerImpl",
            "BotAI",
            "BotAimImprover",
            "NadeSystem",
            "BotBuy",
            "BotState",
            "RoundDamageRecap",
        ] {
            assert!(
                game.css().join("plugins").join(p).exists(),
                "{p} isn't loaded"
            );
        }
        assert!(!game.css().join("plugins").join("BotRandomizer").exists());
        on.bot_skins = true;
        assert!(extra_plugins_mismatch(&game, &on));
        set_extra_plugins(&game, &on).unwrap();
        assert!(game.css().join("plugins").join("BotRandomizer").exists());
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn fresh_download_wins_over_an_older_parked_copy() {
        let game = temp_game();
        install_bot_improver(&game);
        set_extra_plugins(&game, &off()).unwrap();
        let state = game.css().join("plugins").join("BotState");
        fs::create_dir_all(&state).unwrap();
        fs::write(state.join("BotState.dll"), "new").unwrap();
        set_extra_plugins(
            &game,
            &ServerSettings {
                bot_improver: true,
                ..Default::default()
            },
        )
        .unwrap();
        assert_eq!(
            fs::read_to_string(state.join("BotState.dll")).unwrap(),
            "new"
        );
        assert!(!game
            .css()
            .join("lst-parked")
            .join("plugins")
            .join("BotState")
            .exists());
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn retakes_keep_the_bot_flag() {
        let game = temp_game();
        install_bot_improver(&game);
        let config = game.addons().join("BotHider").join("config.json");
        let retakes = ServerSettings {
            bot_improver: true,
            mode: crate::server::RETAKES_MODE.into(),
            retake_style: 1,
            ..Default::default()
        };
        set_extra_plugins(&game, &retakes).unwrap();
        let text = fs::read_to_string(&config).unwrap();
        assert!(text.contains("\"identity_mode\": \"bot\"") && text.contains("fake_ping"));
        set_extra_plugins(
            &game,
            &ServerSettings {
                bot_improver: true,
                mode: "Deathmatch".into(),
                ..Default::default()
            },
        )
        .unwrap();
        assert!(fs::read_to_string(&config)
            .unwrap()
            .contains("\"identity_mode\": \"player\""));
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn version_resource() {
        let mut dll = b"MZ....".to_vec();
        dll.extend("FileVersion\0".encode_utf16().flat_map(u16::to_le_bytes));
        dll.extend([0, 0]);
        dll.extend("2.0.0.1380\0".encode_utf16().flat_map(u16::to_le_bytes));
        assert_eq!(
            pe_version_string(&dll, "FileVersion").as_deref(),
            Some("2.0.0.1380")
        );
        assert_eq!(v(&parse_version("2.19.0.0")), "2.19.0");
    }

    #[test]
    fn zip_paths_stay_inside() {
        assert!(safe_join(Path::new("/x"), "addons/../../evil").is_none());
        assert_eq!(
            css_relative("RetakesPlugin/RetakesPlugin.dll").as_deref(),
            Some("plugins/RetakesPlugin/RetakesPlugin.dll")
        );
        assert_eq!(
            css_relative("x/addons/counterstrikesharp/plugins/a.dll").as_deref(),
            Some("plugins/a.dll")
        );
        assert_eq!(css_relative("readme.txt"), None);
    }

    #[test]
    fn deathmatch_custom_modes_get_turned_off() {
        let game = temp_game();
        let path = game
            .css()
            .join("configs/plugins/Deathmatch/Deathmatch.json");
        let read = || serde_json::from_str::<Value>(&fs::read_to_string(&path).unwrap()).unwrap();
        plain_deathmatch(&game);
        assert_eq!(read()["Gameplay Settings"]["Custom Modes"], false);
        fs::write(&path, r#"{"Gameplay Settings": {"Custom Modes": true, "Game Length": 45}, "Save Players Weapons": true}"#).unwrap();
        plain_deathmatch(&game);
        let v = read();
        assert_eq!(
            (
                &v["Gameplay Settings"]["Custom Modes"],
                &v["Gameplay Settings"]["Game Length"],
                &v["Save Players Weapons"]
            ),
            (&Value::Bool(false), &Value::from(45), &Value::Bool(true))
        );
        fs::write(&path, "// hand edited\n{").unwrap();
        plain_deathmatch(&game);
        assert_eq!(fs::read_to_string(&path).unwrap(), "// hand edited\n{");
        fs::remove_dir_all(&game.root).unwrap();
    }
}
