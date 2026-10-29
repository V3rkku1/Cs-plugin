//! launcher.json in %AppData%\LanSkinTester: the CS2 folder, server settings and everything the pages remember.

use crate::server::ServerSettings;
use crate::util;
use serde::{Deserialize, Serialize};
use std::collections::BTreeMap;
use std::fs;

#[derive(Serialize, Deserialize, Clone, Debug)]
#[serde(rename_all = "PascalCase", default)]
pub struct LauncherSettings {
    pub game_path: Option<String>,
    pub server: ServerSettings,
    /// Window position/size (x, y, width, height) and the page that was open, restored on the next start.
    pub window: Option<Vec<i32>>,
    pub maximized: bool,
    pub last_page: Option<String>,
    /// Text size: "small", "normal" or "large".
    pub text_size: String,
    /// Accessibility: solid panels instead of see-through glass, and no animations.
    pub reduce_glass: bool,
    pub reduce_motion: bool,
    /// Install launcher updates by itself when it starts (nothing running yet).
    pub auto_update: bool,
    /// When the last automatic update ran (RFC 3339), so a half-published release can't cause a restart loop.
    pub last_auto_update: Option<String>,
    /// Setup › Appearance: accent, background and panel colours, opacity, corners, font, picture.
    pub theme: BTreeMap<String, String>,
    /// Quick starts saved from the Play page.
    pub saved_setups: Vec<SavedSetup>,
    /// Servers joined lately (newest first), for one-click rejoin.
    pub recent_servers: Vec<JoinTarget>,
    /// Saved friends: a name and their server address and password.
    pub friends_list: Vec<FriendContact>,
    /// The name friends see when this PC hosts.
    pub host_name: String,
    /// The owner's admin code was entered on this PC: every item can be used ("unbox only" without it).
    pub admin_unlocked: bool,
    /// The code hash it was unlocked with: a new admin code locks PCs unlocked with an old one.
    pub admin_hash: String,
    pub cases_opened: i32,
    /// Openings per container id, for "what it would have cost" on the Cases page.
    pub case_opens: BTreeMap<String, i32>,
    /// Market prices are shown in this currency (USD, EUR or GBP).
    pub currency: String,
}

impl Default for LauncherSettings {
    fn default() -> Self {
        Self {
            game_path: None,
            server: ServerSettings::default(),
            window: None,
            maximized: false,
            last_page: None,
            text_size: "normal".into(),
            reduce_glass: false,
            reduce_motion: false,
            auto_update: true,
            last_auto_update: None,
            theme: BTreeMap::new(),
            saved_setups: vec![],
            recent_servers: vec![],
            friends_list: vec![],
            host_name: user_name(),
            admin_unlocked: false,
            admin_hash: String::new(),
            cases_opened: 0,
            case_opens: BTreeMap::new(),
            currency: "USD".into(),
        }
    }
}

pub fn user_name() -> String {
    std::env::var("USERNAME")
        .or_else(|_| std::env::var("USER"))
        .unwrap_or_else(|_| "Player".into())
}

/// A named snapshot of the Play page, shown as an extra quick start.
#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct SavedSetup {
    pub name: String,
    pub settings: ServerSettings,
}

#[derive(Serialize, Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "PascalCase", default)]
pub struct FriendContact {
    pub name: String,
    pub address: String,
    pub password: String,
}

/// A server to join: address with port, plus the password (may be empty).
#[derive(Serialize, Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "PascalCase", default)]
pub struct JoinTarget {
    pub address: String,
    pub password: String,
}

impl LauncherSettings {
    pub fn load() -> Self {
        let file = util::settings_file();
        let mut settings = match fs::read_to_string(&file) {
            Ok(text) => match util::read_json::<LauncherSettings>(&text) {
                Ok(s) => s,
                Err(_) => {
                    // A damaged file is kept aside: the next save would otherwise replace it with defaults for good.
                    util::set_aside(&file);
                    Self::default()
                }
            },
            Err(_) => Self::default(),
        };
        settings.admin_unlocked &= settings.admin_hash == crate::api::UNLOCK_HASH;
        crate::server::SKIN_COMMANDS.store(
            settings.admin_unlocked,
            std::sync::atomic::Ordering::Relaxed,
        );
        settings
    }

    pub fn save(&self) -> std::io::Result<()> {
        let file = util::settings_file();
        fs::create_dir_all(file.parent().unwrap())?;
        util::safe_write(&file, &serde_json::to_string(self).unwrap())
    }
}
