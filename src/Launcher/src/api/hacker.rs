//! Hacker Mode page (practice against bots on your own VAC-off server): who hacks, quick profiles and every
//! power. Each change is saved at once and sent to the running server right away.

use super::{arg, bad, ok, unknown, App, R};
use crate::server::{self, ServerSettings};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use std::sync::Arc;

/// The page's settings. In hacker.set every field is optional: only the ones given change.
#[derive(Serialize, Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct HackerSettings {
    mode: Option<i32>,
    aimbot: Option<bool>,
    aim_style: Option<i32>,
    aim_bone: Option<i32>,
    aim_target: Option<i32>,
    aim_key: Option<i32>,
    bind_key: Option<String>,
    aggression: Option<i32>,
    aim_walls: Option<bool>,
    trigger: Option<bool>,
    chams: Option<bool>,
    box_esp: Option<bool>,
    bone_esp: Option<bool>,
    tags: Option<bool>,
    radar: Option<bool>,
    enemy_info: Option<bool>,
    no_flash: Option<bool>,
    no_recoil: Option<bool>,
    no_spread: Option<bool>,
    ammo: Option<bool>,
    damage: Option<bool>,
    speed: Option<bool>,
    bhop: Option<bool>,
}

#[derive(Default)]
struct Powers {
    wallhack: bool,
    aimbot: bool,
    aim_walls: bool,
    radar: bool,
    esp: bool,
    no_flash: bool,
    no_recoil: bool,
    no_spread: bool,
    ammo: bool,
    damage: bool,
    speed: bool,
    bhop: bool,
    boxes: bool,
    bones: bool,
    tags: bool,
    trigger: bool,
    style: i32,
}

/// Every power not named goes off, and hacker mode turns on (Everyone) if it was off.
fn set_powers(s: &mut ServerSettings, p: Powers) {
    (s.hack_wallhack, s.hack_aimbot, s.hack_aim_walls, s.hack_radar, s.hack_esp_info, s.hack_no_flash) = (p.wallhack, p.aimbot, p.aim_walls, p.radar, p.esp, p.no_flash);
    (s.hack_no_recoil, s.hack_no_spread, s.hack_ammo, s.hack_one_tap, s.hack_speed, s.hack_bhop) = (p.no_recoil, p.no_spread, p.ammo, p.damage, p.speed, p.bhop);
    (s.hack_box_esp, s.hack_bone_esp, s.hack_tags, s.hack_trigger) = (p.boxes, p.bones, p.tags, p.trigger);
    (s.hack_aim_style, s.hack_aim_bone, s.hack_aim_key) = (p.style, 0, 0);
    if s.hacker_mode == 0 {
        s.hacker_mode = 1;
    }
}

const PROFILES: [(&str, &str, &str, fn(&mut ServerSettings)); 5] = [
    ("legit", "Legit", "Smooth aim, looks like a good player", |s| {
        set_powers(s, Powers { wallhack: true, aimbot: true, radar: true, trigger: true, ..Default::default() });
        s.hack_aggression = 3;
    }),
    ("closet", "Closet", "Silent aim + walls, nothing loud", |s| {
        set_powers(s, Powers { wallhack: true, aimbot: true, radar: true, no_flash: true, no_recoil: true, tags: true, trigger: true, style: 1, ..Default::default() });
        s.hack_aggression = 5;
    }),
    ("rage", "Rage", "Snap aim, ESP and trigger, through walls", |s| {
        set_powers(s, Powers {
            wallhack: true, aimbot: true, aim_walls: true, radar: true, esp: true, no_flash: true, no_recoil: true, no_spread: true, ammo: true,
            damage: true, speed: true, bhop: true, boxes: true, bones: true, tags: true, trigger: true, style: 2,
        });
        s.hack_aggression = 10;
        s.hack_aim_key = 1; // always on
    }),
    ("movement", "Movement", "Speed and bunnyhop only", |s| set_powers(s, Powers { speed: true, bhop: true, no_flash: true, ..Default::default() })),
    ("off", "All Off", "Turns hacker mode and every power off", |s| {
        set_powers(s, Powers::default());
        s.hacker_mode = 0;
    }),
];

pub fn summary(s: &ServerSettings) -> &'static str {
    match s.hacker_mode {
        1 => "On · everyone hacks",
        2 => "On · one random hacker per round",
        _ => "Off · aimbot, wallhack, radar and more",
    }
}

fn clamp(v: i32, list: &[&str]) -> i32 {
    v.clamp(0, list.len() as i32 - 1)
}

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "hacker.state" => {
            let mut st = app.state();
            let running = st.test_mode_on();
            if !running {
                st.hacker_sent = false;
            }
            let s = &st.settings.server;
            let now = s.hacker_commands(false);
            let profiles: Vec<Value> = PROFILES.iter().map(|(id, title, subtitle, apply)| {
                let mut test = s.clone();
                apply(&mut test);
                json!({ "id": id, "title": title, "subtitle": subtitle, "active": test.hacker_commands(false) == now })
            }).collect();
            let settings = HackerSettings {
                mode: Some(s.hacker_mode.clamp(0, 2)), aimbot: Some(s.hack_aimbot), aim_style: Some(clamp(s.hack_aim_style, &server::AIM_STYLES)),
                aim_bone: Some(clamp(s.hack_aim_bone, &server::AIM_BONES)), aim_target: Some(clamp(s.hack_aim_target, &server::AIM_TARGETS)),
                aim_key: Some(clamp(s.hack_aim_key, &server::AIM_KEYS)), bind_key: Some(s.aim_bind_key.clone()), aggression: Some(s.hack_aggression.clamp(1, 10)),
                aim_walls: Some(s.hack_aim_walls), trigger: Some(s.hack_trigger), chams: Some(s.hack_wallhack), box_esp: Some(s.hack_box_esp),
                bone_esp: Some(s.hack_bone_esp), tags: Some(s.hack_tags), radar: Some(s.hack_radar), enemy_info: Some(s.hack_esp_info),
                no_flash: Some(s.hack_no_flash), no_recoil: Some(s.hack_no_recoil), no_spread: Some(s.hack_no_spread), ammo: Some(s.hack_ammo),
                damage: Some(s.hack_one_tap), speed: Some(s.hack_speed), bhop: Some(s.hack_bhop),
            };
            ok(json!({
                "settings": settings, "profiles": profiles, "running": running, "sent": st.hacker_sent,
                "bindKeyValid": s.aim_bind_key.trim().is_empty() || server::clean_key(&s.aim_bind_key).is_some(), "summary": summary(s),
                "modes": server::HACKER_MODES, "aimStyles": server::AIM_STYLES, "aimBones": server::AIM_BONES, "aimTargets": server::AIM_TARGETS, "aimKeys": server::AIM_KEYS,
            }))
        }
        "hacker.set" => {
            let a: HackerSettings = arg(a)?;
            let mut st = app.state();
            let s = &mut st.settings.server;
            let set_bool = |target: &mut bool, v: Option<bool>| if let Some(v) = v { *target = v };
            if let Some(m) = a.mode { s.hacker_mode = m.clamp(0, 2) }
            set_bool(&mut s.hack_aimbot, a.aimbot);
            if let Some(v) = a.aim_style { s.hack_aim_style = clamp(v, &server::AIM_STYLES) }
            if let Some(v) = a.aim_bone { s.hack_aim_bone = clamp(v, &server::AIM_BONES) }
            if let Some(v) = a.aim_target { s.hack_aim_target = clamp(v, &server::AIM_TARGETS) }
            if let Some(v) = a.aim_key { s.hack_aim_key = clamp(v, &server::AIM_KEYS) }
            if let Some(v) = a.bind_key { s.aim_bind_key = v.trim().chars().take(16).collect() }
            if let Some(v) = a.aggression { s.hack_aggression = v.clamp(1, 10) }
            set_bool(&mut s.hack_aim_walls, a.aim_walls);
            set_bool(&mut s.hack_trigger, a.trigger);
            set_bool(&mut s.hack_wallhack, a.chams);
            set_bool(&mut s.hack_box_esp, a.box_esp);
            set_bool(&mut s.hack_bone_esp, a.bone_esp);
            set_bool(&mut s.hack_tags, a.tags);
            set_bool(&mut s.hack_radar, a.radar);
            set_bool(&mut s.hack_esp_info, a.enemy_info);
            set_bool(&mut s.hack_no_flash, a.no_flash);
            set_bool(&mut s.hack_no_recoil, a.no_recoil);
            set_bool(&mut s.hack_no_spread, a.no_spread);
            set_bool(&mut s.hack_ammo, a.ammo);
            set_bool(&mut s.hack_one_tap, a.damage);
            set_bool(&mut s.hack_speed, a.speed);
            set_bool(&mut s.hack_bhop, a.bhop);
            drop(st);
            save_and_apply(app);
            ok(())
        }
        "hacker.profile" => {
            let id: String = arg(a)?;
            let (_, title, _, apply) = PROFILES.iter().find(|p| p.0 == id).ok_or_else(|| bad("Unknown profile"))?;
            apply(&mut app.state().settings.server);
            save_and_apply(app);
            app.toast(format!("Hacker profile: {title}"), "info");
            ok(())
        }
        _ => Err(unknown(name)),
    }
}

fn save_and_apply(app: &App) {
    let mut st = app.state();
    app.save(&st);
    st.hacker_sent = false;
    if let Some(g) = st.game.clone().filter(|_| st.test_mode_on()) {
        let s = &st.settings.server;
        match s.apply_hacker_to_running_server(&g) {
            Ok(()) => {
                s.write_client_cfg(&g);
                st.hacker_sent = true;
            }
            Err(e) => app.log(format!("Could not send the hacker settings: {e}")),
        }
    }
    drop(st);
    app.changed();
}
