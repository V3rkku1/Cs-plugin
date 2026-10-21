//! Setup › Appearance: theme colours, panel opacity, corners, font, a background picture, theme codes to share,
//! and custom.css in the launcher's data folder (loaded after the built-in styles). Also colours the Windows
//! title bar like the launcher.

use super::{arg, bad, ok, open_external, unknown, App, R};
use crate::media::{self, IMAGE_EXTENSIONS};
use crate::util;
use base64::Engine;
use regex::Regex;
use serde_json::Value;
use std::collections::BTreeMap;
use std::fs;
use std::sync::{Arc, LazyLock};

/// Allowed theme keys and how a value is checked (None = not allowed).
fn clean_value(key: &str, value: &str) -> Option<String> {
    static HEX: LazyLock<Regex> = LazyLock::new(|| Regex::new("^#[0-9a-fA-F]{6}$").unwrap());
    static PRESET: LazyLock<Regex> = LazyLock::new(|| Regex::new("^[a-z0-9-]{1,24}$").unwrap());
    let number = |min: i32, max: i32| {
        value
            .parse::<i32>()
            .ok()
            .filter(|n| (min..=max).contains(n))
            .map(|n| n.to_string())
    };
    match key {
        "accent" | "background" | "panel" | "text" => {
            HEX.is_match(value).then(|| value.to_lowercase())
        }
        "opacity" => number(40, 100),
        "radius" => number(0, 160),
        "dim" => number(0, 90),
        "font" => ["inter", "segoe", "system", "mono", "dyslexic"]
            .contains(&value)
            .then(|| value.into()),
        "preset" => PRESET.is_match(value).then(|| value.into()),
        "titlebar" => ["theme", "accent", "system"]
            .contains(&value)
            .then(|| value.into()),
        _ => None,
    }
}

fn apply_changes(
    theme: &mut BTreeMap<String, String>,
    changes: BTreeMap<String, Option<String>>,
    replace: bool,
) {
    if replace {
        theme.retain(|k, _| k == "image");
    }
    for (key, value) in changes {
        match value.filter(|v| !v.is_empty()) {
            None => {
                theme.remove(&key);
            }
            Some(v) => {
                if let Some(clean) = clean_value(&key, v.trim()) {
                    theme.insert(key, clean);
                }
            }
        }
    }
}

const CUSTOM_CSS_TEMPLATE: &str = "/*
 * LAN Skin Tester launcher: your own styles. Loaded after the built-in ones, so anything here wins.
 * Save, then click the launcher window: it reloads this file. Delete everything to go back.
 *
 * The main colours (Setup › Appearance sets these too):
 *   --bg            window background
 *   --panel         cards and the sidebar
 *   --blue          accent: main buttons, links, selected items
 *   --text, --text-2, --text-3   main, secondary and faint text
 *   --r-xl, --r-lg, --r-md, --r-sm   corner sizes
 *   --font          the font
 *
 * Examples (remove the comment marks to try them):
 */

/* :root { --blue: #ff9f0a; --bg: #101010; } */
/* .page-header h1 { letter-spacing: 0; font-weight: 700; } */
/* .sidebar { border-radius: 0; } */
/* .backdrop { background: linear-gradient(160deg, #0d1020, #1a0d1a); } */
";

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "setup.theme" => {
            let changes: BTreeMap<String, Option<String>> = arg(a)?;
            let mut st = app.state();
            apply_changes(&mut st.settings.theme, changes, false);
            save_theme(app, &st.settings.theme);
            app.save(&st);
        }
        "setup.themeReset" => {
            let mut st = app.state();
            st.settings.theme.clear();
            remove_backgrounds();
            save_theme(app, &st.settings.theme);
            app.save(&st);
            app.toast("Appearance back to the default look.", "good");
        }
        "setup.themeImage" => {
            let Some(file) = app.pick(|d| {
                d.set_title("Background picture")
                    .add_filter("Pictures", &["png", "jpg", "jpeg", "webp"])
                    .pick_file()
            }) else {
                return ok(());
            };
            let ext = file
                .extension()
                .map(|e| format!(".{}", e.to_string_lossy().to_lowercase()))
                .unwrap_or_default();
            if !IMAGE_EXTENSIONS.contains(&ext.as_str()) {
                return Err(bad("Pick a PNG, JPG or WebP picture."));
            }
            if fs::metadata(&file)?.len() > 25_000_000 {
                return Err(bad("That picture is too big (over 25 MB)."));
            }
            fs::create_dir_all(util::data_dir())?;
            remove_backgrounds();
            fs::copy(&file, media::background_file(&ext))?;
            let mut st = app.state();
            // The value only changes the picture's address, so the page loads the new one.
            st.settings.theme.insert(
                "image".into(),
                chrono::Utc::now().timestamp_millis().to_string(),
            );
            app.save(&st);
        }
        "setup.themeImageRemove" => {
            remove_backgrounds();
            let mut st = app.state();
            st.settings.theme.remove("image");
            app.save(&st);
        }
        "setup.themeExport" => {
            // The background picture isn't in it.
            let values: BTreeMap<String, String> = app
                .state()
                .settings
                .theme
                .iter()
                .filter(|(k, _)| *k != "image")
                .map(|(k, v)| (k.clone(), v.clone()))
                .collect();
            let code = format!(
                "LST1-{}",
                base64::engine::general_purpose::STANDARD
                    .encode(serde_json::to_string(&values).unwrap())
            );
            if app.copy(&code) {
                app.toast(
                    "Theme code copied. Send it to a friend: they paste it on Setup › Appearance.",
                    "good",
                );
            } else {
                app.toast("Could not use the clipboard.", "bad");
            }
            return ok(code);
        }
        "setup.themeImport" => {
            let code: String = arg(a)?;
            let code = code.trim();
            let json = match code.strip_prefix("LST1-") {
                Some(b64) => base64::engine::general_purpose::STANDARD
                    .decode(b64)
                    .ok()
                    .and_then(|b| String::from_utf8(b).ok()),
                None => Some(code.to_string()),
            };
            let mut values: BTreeMap<String, Option<String>> = json
                .and_then(|j| serde_json::from_str(&j).ok())
                .unwrap_or_default();
            if values.is_empty() {
                return Err(bad("That isn't a theme code (they start with LST1-)."));
            }
            values.remove("image");
            let mut st = app.state();
            apply_changes(&mut st.settings.theme, values, true);
            save_theme(app, &st.settings.theme);
            app.save(&st);
            app.toast("Theme applied.", "good");
        }
        "setup.customCss" => {
            let file = media::custom_css_file();
            fs::create_dir_all(util::data_dir())?;
            if !file.exists() {
                fs::write(&file, CUSTOM_CSS_TEMPLATE)?;
            }
            // No app for .css files: Notepad always exists.
            if open_external(&file).is_err() {
                let _ = std::process::Command::new("notepad.exe").arg(&file).spawn();
            }
            app.toast(
                "custom.css opened. Save it, then click the launcher to see the change.",
                "info",
            );
            return ok(());
        }
        _ => return Err(unknown(name)),
    }
    app.changed();
    ok(())
}

fn remove_backgrounds() {
    for ext in IMAGE_EXTENSIONS {
        let _ = fs::remove_file(media::background_file(ext));
    }
}

/// The title bar is drawn by Windows: give it the new colours too.
fn save_theme(app: &App, theme: &BTreeMap<String, String>) {
    if let Some(w) = app.window() {
        apply_title_bar(&w, theme);
    }
}

fn color(theme: &BTreeMap<String, String>, key: &str, fallback: (u8, u8, u8)) -> (u8, u8, u8) {
    let parse = |h: &str| {
        Some((
            u8::from_str_radix(h.get(1..3)?, 16).ok()?,
            u8::from_str_radix(h.get(3..5)?, 16).ok()?,
            u8::from_str_radix(h.get(5..7)?, 16).ok()?,
        ))
    };
    theme
        .get(key)
        .filter(|h| h.len() == 7 && h.starts_with('#'))
        .and_then(|h| parse(h))
        .unwrap_or(fallback)
}

/// Colours the window like the launcher (Setup › Appearance › Title Bar): the theme's background with a
/// panel-coloured border, the accent colour, or Windows' own. Colours need Windows 11; Windows 10 only gets
/// the dark bar.
pub fn apply_title_bar(window: &tauri::WebviewWindow, theme: &BTreeMap<String, String>) {
    let background = color(theme, "background", (15, 17, 23));
    let _ = window.set_background_color(Some(tauri::window::Color(
        background.0,
        background.1,
        background.2,
        255,
    )));
    #[cfg(windows)]
    {
        use windows_sys::Win32::Graphics::Dwm::DwmSetWindowAttribute;
        let style = theme.get("titlebar").map_or("theme", |s| s.as_str());
        let caption = match style {
            "accent" => Some(color(theme, "accent", (10, 132, 255))),
            "system" => None,
            _ => Some(background),
        };
        let border = match style {
            "system" => None,
            "accent" => caption,
            _ => Some(color(theme, "panel", (26, 29, 38))),
        };
        let colorref = |c: Option<(u8, u8, u8)>| {
            c.map_or(0xFFFF_FFFFu32, |(r, g, b)| {
                r as u32 | (g as u32) << 8 | (b as u32) << 16
            })
        };
        // HSL lightness, like WinForms' Color.GetBrightness.
        let lightness =
            |(r, g, b): (u8, u8, u8)| (r.max(g).max(b) as f32 + r.min(g).min(b) as f32) / 510.0;
        // White or near-black title text, whichever reads better on the bar.
        let text = caption.map(|(r, g, b)| {
            if (0.2126 * r as f32 + 0.7152 * g as f32 + 0.0722 * b as f32) / 255.0 > 0.6 {
                (20, 20, 24)
            } else {
                (240, 240, 245)
            }
        });
        let Ok(hwnd) = window.hwnd() else { return };
        let set = |attribute: u32, value: u32| unsafe {
            DwmSetWindowAttribute(hwnd.0 as _, attribute, &value as *const u32 as *const _, 4);
        };
        set(20, caption.map_or(true, |c| lightness(c) < 0.6) as u32); // dark mode
        set(33, 2); // round corners
        set(35, colorref(caption));
        set(36, colorref(text));
        set(34, colorref(border));
    }
}
