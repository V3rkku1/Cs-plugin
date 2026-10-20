//! LAN Skin Tester launcher: one Tauri window showing web/ (HTML, CSS, JS). The page calls the launcher with
//! `invoke("call", { name, args })`, gets events as "lst", and loads pictures, the theme background and
//! custom.css through the lst:// protocol. Options: --page <id> opens one page, --devtools allows dev tools.

#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod api;
mod console;
mod game;
mod installer;
mod itemdb;
mod items;
mod media;
mod net;
mod odds;
mod prices;
mod relay;
mod server;
mod session;
mod settings;
mod update;
mod util;

use api::App;
use std::sync::Arc;
use std::time::Duration;
use tauri::http::{Response, StatusCode};
use tauri::{AppHandle, Manager, WebviewUrl, WebviewWindowBuilder, WindowEvent};

/// The layout is made for about this much room; bigger windows zoom the whole page instead of stretching it.
const DESIGN_WIDTH: f64 = 1480.0;
const DESIGN_HEIGHT: f64 = 920.0;
const MAX_ZOOM: f64 = 1.6;

/// The launcher's state from anywhere that has the app handle.
pub fn with_app(handle: &AppHandle, f: impl FnOnce(&Arc<App>)) {
    if let Some(app) = handle.try_state::<Arc<App>>() {
        f(&app);
    }
}

#[tauri::command]
async fn call(app: tauri::State<'_, Arc<App>>, name: String, args: Option<serde_json::Value>) -> Result<serde_json::Value, String> {
    let app = app.inner().clone();
    tauri::async_runtime::spawn_blocking(move || api::call(&app, &name, args.unwrap_or_default())).await.map_err(|e| e.to_string())?
}

/// lst://localhost/img?u=<url>&s=<size>, /theme/background and /theme/custom.css.
fn protocol(app: &App, uri: &tauri::http::Uri) -> Response<Vec<u8>> {
    let reply = |status: StatusCode, kind: &str, cache: &str, body: Vec<u8>| {
        Response::builder().status(status).header("Content-Type", kind).header("Cache-Control", cache).body(body).unwrap()
    };
    let query: Vec<(String, String)> = uri.query().unwrap_or("").split('&')
        .filter_map(|p| p.split_once('=')).map(|(k, v)| (k.to_string(), util::unescape(&v.replace('+', " ")))).collect();
    let param = |name: &str| query.iter().find(|q| q.0 == name).map(|q| q.1.clone());
    match uri.path() {
        "/img" => {
            let size = param("s").and_then(|s| s.parse().ok()).unwrap_or(256);
            match param("u").and_then(|u| media::image(&app.http, &u, size)) {
                Some(bytes) => reply(StatusCode::OK, media::content_type(&bytes), "max-age=86400", bytes),
                None => reply(StatusCode::NOT_FOUND, "text/plain", "no-cache", vec![]),
            }
        }
        "/theme/background" => match media::background_path().and_then(|p| std::fs::read(p).ok()) {
            Some(bytes) => reply(StatusCode::OK, media::content_type(&bytes), "no-cache", bytes),
            None => reply(StatusCode::NOT_FOUND, "text/plain", "no-cache", vec![]),
        },
        "/theme/custom.css" => reply(StatusCode::OK, "text/css; charset=utf-8", "no-cache", std::fs::read(media::custom_css_file()).unwrap_or_default()),
        _ => reply(StatusCode::NOT_FOUND, "text/plain", "no-cache", vec![]),
    }
}

/// Maximized or full screen on a big monitor the page would otherwise stay small: zoom it so it fills the
/// window like it does at the normal size (steps of 5%, so small resizes don't keep re-zooming).
fn fit_zoom(window: &tauri::WebviewWindow, last: &std::sync::Mutex<f64>) {
    let (Ok(size), Ok(scale)) = (window.inner_size(), window.scale_factor()) else { return };
    if window.is_minimized().unwrap_or(false) || size.width == 0 {
        return;
    }
    let zoom = (size.width as f64 / scale / DESIGN_WIDTH).min(size.height as f64 / scale / DESIGN_HEIGHT).clamp(1.0, MAX_ZOOM);
    let zoom = (zoom * 20.0).round() / 20.0;
    let mut last = last.lock().unwrap();
    if (*last - zoom).abs() > 0.001 {
        *last = zoom;
        let _ = window.set_zoom(zoom);
    }
}

fn open_window(handle: &AppHandle, app: &Arc<App>) -> tauri::Result<()> {
    let devtools = cfg!(debug_assertions) || std::env::args().any(|a| a == "--devtools");
    let theme = app.state().settings.theme.clone();
    let builder = WebviewWindowBuilder::new(handle, "main", WebviewUrl::App("index.html".into()))
        .title("LAN Skin Tester")
        .inner_size(1400.0, 900.0)
        .min_inner_size(1080.0, 700.0)
        .center()
        .visible(false)
        .devtools(devtools)
        .data_directory(util::data_dir().join("webview"))
        // Links to the web open in the real browser; the window only ever shows the launcher.
        .on_navigation(|url| {
            let own = matches!(url.scheme(), "tauri" | "lst") || url.host_str().is_some_and(|h| h == "tauri.localhost" || h == "localhost" || h == "lst.localhost");
            if !own {
                let _ = open::that_detached(url.as_str());
            }
            own
        })
        .on_new_window(|url, _| {
            let _ = open::that_detached(url.as_str());
            tauri::webview::NewWindowResponse::Deny
        });
    let window = builder.build()?;
    // Sizes are logical (they scale with the screen's DPI); keep the window on small screens.
    if let Ok(Some(m)) = window.current_monitor() {
        let area = m.size().to_logical::<f64>(m.scale_factor());
        let fit = |w: f64, h: f64| tauri::LogicalSize::new(w.min(area.width * 0.94), h.min(area.height * 0.94));
        let _ = window.set_min_size(Some(fit(1080.0, 700.0)));
        let _ = window.set_size(fit(1400.0, 900.0));
        let _ = window.center();
    }
    // A saved position only counts on a connected screen (monitors change).
    let st = app.state();
    if let Some([x, y, w, h]) = st.settings.window.as_deref().map(|v| [v.first(), v.get(1), v.get(2), v.get(3)]).and_then(|v| Some([*v[0]?, *v[1]?, *v[2]?, *v[3]?])) {
        let visible = window.available_monitors().unwrap_or_default().iter().any(|m| {
            let (p, s) = (m.position(), m.size());
            (p.x..p.x + s.width as i32).contains(&(x + 40)) && (p.y..p.y + s.height as i32).contains(&(y + 20))
        });
        if visible {
            let _ = window.set_position(tauri::PhysicalPosition::new(x, y));
            let _ = window.set_size(tauri::PhysicalSize::new(w.max(1) as u32, h.max(1) as u32));
        }
    }
    if st.settings.maximized {
        let _ = window.maximize();
    }
    drop(st);
    api::apply_title_bar(&window, &theme);
    let _ = window.show();
    let last_zoom = Arc::new(std::sync::Mutex::new(1.0));
    fit_zoom(&window, &last_zoom);
    let (w, app2) = (window.clone(), app.clone());
    window.on_window_event(move |event| match event {
        WindowEvent::Resized(_) | WindowEvent::ScaleFactorChanged { .. } => fit_zoom(&w, &last_zoom),
        WindowEvent::CloseRequested { api, .. } => {
            if api::shell::CLOSING.load(std::sync::atomic::Ordering::SeqCst) {
                return;
            }
            {
                let mut st = app2.state();
                api::shell::remember_window(&w, &mut st);
                app2.save(&st);
            }
            if !api::confirm_close(&app2) {
                api.prevent_close();
            }
        }
        _ => {}
    });
    Ok(())
}

fn main() {
    let mut start_page = None;
    let args: Vec<String> = std::env::args().collect();
    if let Some(i) = args.iter().position(|a| a == "--page") {
        start_page = args.get(i + 1).cloned();
    }
    tauri::Builder::default()
        .register_asynchronous_uri_scheme_protocol("lst", |ctx, request, responder| {
            let handle = ctx.app_handle().clone();
            let uri = request.uri().clone();
            std::thread::spawn(move || with_app(&handle, |app| responder.respond(protocol(app, &uri))));
        })
        .invoke_handler(tauri::generate_handler![call])
        .setup(move |tauri_app| {
            let handle = tauri_app.handle().clone();
            let app = App::new(handle.clone(), start_page);
            tauri_app.manage(app.clone());
            {
                let a = app.clone();
                let _ = net::ROUTER_CHANGED.set(Box::new(move || a.changed()));
            }
            let (a1, a2, a3) = (app.clone(), app.clone(), app.clone());
            relay::start(move || a1.state().test_mode_on(), move |l| a2.log(l));
            net::start_listening(move |l| a3.log(l));
            let a = app.clone();
            net::start_beacon(move || api::friends::beacon(&a));
            open_window(&handle, &app)?;
            let a = app.clone();
            std::thread::spawn(move || {
                api::started(&a);
                // One poll every second for every page (CS2 starts without delay); the game watch every 5 seconds.
                let mut tick = 0u64;
                loop {
                    std::thread::sleep(Duration::from_secs(1));
                    api::poll_server(&a);
                    tick += 1;
                    if tick % 5 == 0 {
                        api::watch_game(&a);
                    }
                }
            });
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("could not start the launcher")
        .run(|handle, event| {
            if let tauri::RunEvent::Exit = event {
                // The router port closes again (internet mode); the lease would run out by itself.
                with_app(handle, |_| net::remove_mapping());
            }
        });
}
