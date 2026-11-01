# Launcher Rust/Tauri backend: startup and IPC performance audit

Scope: `/home/user/Cs-plugin/src/Launcher` (Cargo.toml, build.rs, tauri.conf.json, capabilities/, src/, src/api/, src/api/cases/). Read-only code audit; no web sources were used. Every "Source" below is a file:line in that folder (paths relative to `src/Launcher/`). Impact estimates are my judgment from the code, not measurements, so they are in the Inferences sections.

## Q1. Startup path: what runs before the window shows?

### Takeaway
Before `window.show()`, the Tauri `setup` closure runs on the main thread. It synchronously reads and parses `launcher.json` plus 8 item-database JSON caches, and builds two HashMap indexes. The JSON goes through a slow Value-then-struct double parse, and `show()` is called before the page has loaded. That is the biggest startup cost. CS2 discovery, the update check and the add-on check already run off the main thread. But the first of these holds the shared `State` mutex while it scans files, so the page's first IPC calls wait behind it.

### Cited Findings
- Order in `main()`: `update::wait_for_exit(pid)` runs first. When the launcher was started with `--after <pid>` (after a self-update), this polls sysinfo every 200 ms, up to 300 times (60 s), before Tauri is even built. — [main.rs:222-228](src/main.rs), [update.rs:191-205](src/update.rs)
- `setup` closure (main thread): `App::new` → `LauncherSettings::load()` (sync `fs::read_to_string` + `util::read_json`) and `ItemDatabase::load_cached(&itemdb::data_dir())`. — [main.rs:238-241](src/main.rs), [api/mod.rs:115-117](src/api/mod.rs), [settings.rs:121-133](src/settings.rs)
- `load_cached` reads 8 files one after another: weapons, skins, stickers, charms, agents, pins, musickits, cases. Each one is read with `fs::read_to_string` and parsed with `util::read_json`. It then builds `by_paint` and `stickers_by_id` HashMaps over all skins and stickers. — [itemdb.rs:152-191](src/itemdb.rs)
- How big these files are: the source says the full download "rewrites the cache (~25 MB)". The module comment calls the on-disk cache "small", so the actual on-disk size is unknown. — [itemdb.rs:1-3](src/itemdb.rs), [itemdb.rs:226](src/itemdb.rs)
- `util::read_json` parses text into `serde_json::Value`, walks it recursively with `strip_nulls`, then calls `serde_json::from_value`. That is two full passes plus a DOM allocation for every value. — [util.rs:100-104](src/util.rs), [util.rs:88-97](src/util.rs)
- `App::new` builds 2 ureq agents (`util::http_agent(300)`, plus `http_agent(60)` inside `PriceList::new`). They are cheap to build and make no network calls at construction. — [api/mod.rs:122-123](src/api/mod.rs), [prices.rs:75-86](src/prices.rs), [util.rs:124-131](src/util.rs)
- Next, setup spawns the UDP listener thread (`net::start_listening`) and the beacon thread (`net::start_beacon`, which sleeps 2 s first). Neither blocks. — [main.rs:246-249](src/main.rs), [net.rs:642-689](src/net.rs), [net.rs:693-720](src/net.rs)
- Then `open_window` runs synchronously in setup:
  - `WebviewWindowBuilder...visible(false)...build()`
  - `current_monitor()`, then `set_min_size`, `set_size`, `center` (a second resize and re-center after build)
  - `available_monitors()`, then `set_position` and `set_size` from saved settings
  - `maximize`, `apply_title_bar` (`set_background_color` + 5 `DwmSetWindowAttribute` calls)
  - `window.show()` immediately, then `fit_zoom` → `set_zoom`
  — [main.rs:124-191](src/main.rs), [api/theme.rs:240-287](src/api/theme.rs)
- `show()` is not tied to page readiness: there is no `on_page_load`, eval or ready event anywhere in `src`. Grep for `on_page_load|PageLoad|eval(` returned nothing. — [main.rs:189](src/main.rs)
- After `open_window`, a thread runs `api::started`. It takes `app.state()` and holds it while it runs:
  - `GameFolder::is_valid`, or `GameFolder::find(steam_path())`: a registry read plus `libraryfolders.vdf` read and regex
  - `app.set_game`: `settings.save()` (disk write) and possibly `db.write_plugin_skin_names` (serializes every skin to `skins.json`)
  - `server::discover_modes`: reads and parses `gamemodes.txt` via KeyValues, plus `maps_of`. The static `MODES` is fresh on every launch, so this runs every start when Rush mode exists.
  — [api/shell.rs:190-203](src/api/shell.rs), [api/mod.rs:260-278](src/api/mod.rs), [server.rs:185-194](src/server.rs), [game.rs:647-720](src/game.rs), [game.rs:99-116](src/game.rs)
- `started` then spawns the add-on staleness check (after a 5 s sleep) and `check_for_update` (a GitHub HTTP call, then every 15 min). Both run on their own threads. — [api/shell.rs:204-225](src/api/shell.rs), [update.rs:46-75](src/update.rs)
- No network calls, process spawns or `Regex::new` run on the main thread during setup. The only process enumeration before the window is `wait_for_exit`, and only on the post-update start. — [main.rs:215-266](src/main.rs)

### Inferences
- The fix with the most impact: move `ItemDatabase::load_cached` off the main thread.
  - How: start with `ItemDatabase::default()` and load it in the `started` thread, then call `app.reload_db()`-style swap + `changed()`. The pattern already exists at [api/mod.rs:280-291](src/api/mod.rs).
  - Also: replace `read_json`'s Value round-trip for the item-db files with `serde_json::from_slice::<Vec<T>>` (`fs::read` instead of `read_to_string`). The structs already use `#[serde(default)]` at [itemdb.rs:19-20](src/itemdb.rs). Nulls would still fail, though: handle that with a `deserialize_with` that treats null as default, or keep `read_json` only as a fallback when the fast parse fails.
  - Estimated impact: tens to a few hundred ms off time-to-window if the cache is multi-MB. Likely 2-3x faster parsing than the double pass.
- Show only when the page is ready: keep `visible(false)`, and call `show()` from a `app.ready` IPC call or from `on_page_load(PageLoadEvent::Finished)`, with a timeout fallback of about 1-2 s. This removes the empty or flash frame. Impact is perceived, not measured: no blank window.
- `started` holds the `State` lock across file scanning. Compute `path`, `find_game_mode` and the skin-name write without the lock, then lock only to assign. Otherwise the page's first `app.state`, `setup.*` and `play.*` calls block for the duration. Impact: the first data render could be tens of ms faster, more on a cold HDD.
- `discover_modes` reparses `gamemodes.txt` on every launch. Caching the result in `launcher.json` keyed by the `steam.inf` PatchVersion would skip it. Low to medium impact.

### Gaps
- No timing data. The real sizes of the cached `skins.json` and `stickers.json` on a user machine are unknown, because the data folder is not in the repo.

## Q2. Are Tauri commands sync or async? Which block?

### Takeaway
There is one command, `call`. It is `async` and wraps every API call in `spawn_blocking`, so IPC never runs on the UI thread. Blocking comes from somewhere else: one global `State` mutex and one global `Inv` mutex. The poll and beacon threads hold `State` while doing process enumeration and disk reads every second. Some handlers hold it across network calls, or call `on_ui` and dialogs.

### Cited Findings
- `#[tauri::command] async fn call` → `tauri::async_runtime::spawn_blocking(move || api::call(...))`. — [main.rs:42-52](src/main.rs)
- Design note: "never hold it across a download or a dialog". All `cases.*`, `inventory.*` and `tradeup.*` calls serialize on `app.inv()`. — [api/mod.rs:1-3](src/api/mod.rs), [api/mod.rs:328-337](src/api/mod.rs)
- The poll thread runs `api::poll_server` every 1 s and `watch_game` every 5 s. — [main.rs:252-263](src/main.rs)
- `poll_server` holds `app.state()` while it does all of the following:
  - reads the plugin status file (`server_status`, an `fs::read_to_string` + `read_json` double parse)
  - `isolate_client` → `find_server()` → `proc_of` → a new `sysinfo::System` + process refresh every call, then a `gameinfo.gi` read + regex
  - `test_mode_on()`, which costs one more `gameinfo.gi` read, a `session_file().exists()` and another `find_server()`
  - `game::workshop_downloaded` (a directory walk for `.vpk`) when a workshop id is set
  - `handle_join_when_up`, which calls `test_mode_on()` up to 2 more times
  — [api/play.rs:634-658](src/api/play.rs), [api/play.rs:707-708](src/api/play.rs), [api/mod.rs:91-111](src/api/mod.rs), [session.rs:34-38](src/session.rs), [session.rs:112-135](src/session.rs), [session.rs:201-205](src/session.rs), [session.rs:210-224](src/session.rs), [game.rs:140-153](src/game.rs), [game.rs:273-281](src/game.rs)
- `watch_game` holds `state` while calling `session::find_server()`, `game::metamod_enabled` (a file read) and `session::cs2_running()` (a full `ProcessesToUpdate::All` enumeration, cached for only 1 s). — [api/shell.rs:455-488](src/api/shell.rs), [session.rs:72-90](src/session.rs)
- `friends::beacon` runs every 3 s from the beacon thread. It takes `state` and calls `test_mode_on()` and `server_status()`, so the same process-enumeration and file reads repeat. — [api/friends.rs:557-574](src/api/friends.rs), [net.rs:700-718](src/net.rs)
- `net::start_listening` calls `own_addresses()` for every received UDP packet. That runs `nics()`, a full netdev interface enumeration, per packet. `start_beacon` calls `broadcast_addresses()` (also `nics()`) every 3 s. — [net.rs:108-127](src/net.rs), [net.rs:660-662](src/net.rs)
- `poll_server` calls `net::renew_if_needed()` after it drops the lock. That function can make a UPnP SOAP POST with a 6 s timeout on the poll thread, which stalls the next ticks. — [api/play.rs:654](src/api/play.rs), [net.rs:470-481](src/net.rs), [net.rs:236-246](src/net.rs)
- `api::App::on_ui` runs dialogs with `run_on_main_thread` and blocks the worker on `rx.recv()`. — [api/mod.rs:220-236](src/api/mod.rs)
- `confirm_close` runs a modal `rfd::MessageDialog` from inside the `CloseRequested` handler, which is the main thread. Before that, the handler does a sync `settings.save()`. — [main.rs:197-209](src/main.rs), [api/shell.rs:498-520](src/api/shell.rs)
- On `RunEvent::Exit`, `net::remove_mapping()` sends a blocking SOAP `DeletePortMapping` (6 s timeout) when a mapping is active. This delays process exit. — [main.rs:269-273](src/main.rs), [net.rs:484-497](src/net.rs)
- Network calls made inside IPC calls (on a worker thread, so the UI does not freeze, but the page waits for the answer):
  - `workshop.search` fetches a Steam Workshop HTML page, then `load_details` POSTs in batches of 50
  - `setup.checkUpdates` makes 2 sequential GETs
  - `app.checkUpdate` → `update::check` makes 1-2 GitHub GETs
  All use `app.http` with a 300 s global timeout.
  — [api/workshop.rs:68-100](src/api/workshop.rs), [api/workshop.rs:280-315](src/api/workshop.rs), [api/setup.rs:117-141](src/api/setup.rs), [installer.rs:388-406](src/installer.rs), [util.rs:124-131](src/util.rs), [api/mod.rs:122](src/api/mod.rs)
- `workshop.search` takes `app.state()` only after the HTTP calls, so it follows the "no lock across download" rule. — [api/workshop.rs:93](src/api/workshop.rs)
- The `lst://` protocol is registered as asynchronous but spawns one `std::thread` per request. `media::image` then blocks on a 6-slot condvar while downloading. A grid of 100 thumbnails therefore creates 100 OS threads, about 94 of them parked. — [main.rs:230-236](src/main.rs), [media.rs:10-43](src/media.rs)

### Inferences
- Fix 1: make `test_mode_on` and `find_server` cheap.
  - Cache `find_server()` for about 1 s, like `cs2_ids` does at [session.rs:72-80](src/session.rs).
  - Or reuse one `System` in a static Mutex instead of `System::new()` per `proc_of`.
  - Cache `metamod_enabled(gameinfo.gi)` by file mtime.
  - Compute `test_mode_on()` once per tick in `poll_server` and pass it down.
  - Estimated impact: each 1 s tick currently does 3-5 process lookups and 2-4 `gameinfo.gi` reads while holding `State`. Cutting this to at most 1 removes most of the lock-hold time that IPC calls wait behind. On Windows this likely also cuts idle CPU noticeably (inference: sysinfo's Windows backend may enumerate all processes even for `ProcessesToUpdate::Some`; not verified here).
- Fix 2: do slow reads outside the lock. In `poll_server`, clone `st.game`, drop the lock, do the status read, process checks and file checks, then re-lock to update `st.play`. Do the same in `watch_game` and `friends::beacon`.
- Fix 3: cache `own_addresses()` for about 10 s in the UDP listener loop instead of enumerating NICs on every packet. Impact is low to medium (CPU on busy LANs).
- Fix 4: replace thread-per-request in `lst://` with a fixed pool. For example, use `tauri::async_runtime::spawn_blocking`: it is already bounded and reused. Also check the disk cache before taking a slot, which the code already does at [media.rs:29-31](src/media.rs). Impact: smoother image-heavy pages (inventory and cases), with less thread creation and memory.
- Fix 5: use a separate short-timeout agent (for example 15-20 s) for interactive calls such as `workshop.search`, images and update checks, instead of the shared 300 s one. A stalled request can otherwise hold a page spinner, or one of the 6 image slots, for up to 5 minutes.
- Fix 6: run `remove_mapping` on exit with a short timeout (about 1-2 s), or fire-and-forget it before the window closes.

### Gaps
- No profiling of lock contention. Which IPC calls the frontend sends right after load, and after each `changed` or `play.tick` event, is frontend scope and was not checked here.

## Q3. Caching: repeated HTTP fetches, file reads, regex recompiles

### Takeaway
Most regexes are already `LazyLock`. Two are still compiled on every call: one in `setup` view building, which runs on each Setup render, and one in `latest_versions`. The price list is cached on disk (12 h) and Steam per-item prices once per run. Inventory items stay in memory after the first read. Per-call disk re-reads remain for `loadout.json`, `inventory-removed.json`, the plugin status file and `gameinfo.gi`. `cases.inventory` rebuilds up to 50,000 views every time it is called.

### Cited Findings
- `Regex::new(r"(\d+)$")` is compiled inside `newer()`. It is called twice per `setup` view build (Metamod and CSS rows). — [api/setup.rs:266-279](src/api/setup.rs), [api/setup.rs:347](src/api/setup.rs), [api/setup.rs:366](src/api/setup.rs)
- `Regex::new(r"mmsource-(.+)-windows")` is compiled per `latest_versions()` call. That happens only on a manual check, so the impact is low. — [installer.rs:390-396](src/installer.rs)
- Already static `LazyLock` regexes:
  - `IS_METAMOD` [game.rs:136](src/game.rs)
  - `PATH` [game.rs:100](src/game.rs)
  - play [api/play.rs:464](src/api/play.rs), [api/play.rs:1691](src/api/play.rs)
  - workshop [api/workshop.rs:83](src/api/workshop.rs)
  - theme [api/theme.rs:17-18](src/api/theme.rs)
  - logs [api/logs.rs:90](src/api/logs.rs)
  - server [server.rs:211](src/server.rs), [server.rs:216](src/server.rs), [server.rs:497-502](src/server.rs), [server.rs:617](src/server.rs)
  - net [net.rs:868-869](src/net.rs)
- Price list: `ensure()` loads the disk cache once per currency change. It re-downloads in a background thread when the cache is older than 12 h. Per-item Steam lookups are queued on a gate with a 3.5 s spacing, and `notify` coalesces `changed` events to at most one per 3 s. — [prices.rs:100-140](src/prices.rs), [prices.rs:88-98](src/prices.rs), [prices.rs:200-231](src/prices.rs)
- `price_of` calls `app.prices.ensure()` for every item. Each call locks `inner`. `price()` then lowercases the market name, and `market()` formats a new String. All of this runs for every inventory row. — [api/cases.rs:213-216](src/api/cases.rs), [prices.rs:170-176](src/prices.rs)
- `Inv::items` caches the parsed inventory in memory after the first read. The first read uses the `read_json` double parse, on a file of up to `MAX_INVENTORY = 50_000` items. — [api/cases.rs:106-131](src/api/cases.rs), [api/cases.rs:31](src/api/cases.rs)
- `Inv::save` serializes and rewrites the whole inventory (`safe_write`) on every mutation, for example every `cases.open`. `app.save(&st)` also rewrites `launcher.json` in the same call. Both happen while holding `inv` and `state`. — [api/cases.rs:134-142](src/api/cases.rs), [api/cases.rs:500-504](src/api/cases.rs), [api/cases.rs:520-524](src/api/cases.rs)
- `read_loadout` → `LoadoutStore::open` re-reads and re-parses the plugin's `loadout.json` from disk on every `cases.open`, `cases.openMany` and `cases.inventory` call. It can also write it back via `prune_unowned` → `store.save()`. — [api/cases.rs:274-283](src/api/cases.rs), [items.rs:257-272](src/items.rs)
- `cases.inventory` does all of this on every call:
  - `read_loadout` (disk)
  - `sync_stat_trak`, which may trigger a full save
  - one `view()` per loose item, each with a price lookup
  - `last_removed()`, which re-reads and parses `inventory-removed.json` just to get `.len()`
  It returns every item in one JSON IPC payload. — [api/cases.rs:531-549](src/api/cases.rs), [api/cases.rs:1325-1329](src/api/cases.rs)
- `State::server_status()` re-reads the status file on every call. It is called from `poll_server` every 1 s, from `beacon` every 3 s, and from handlers such as `cases.tryInGame`. — [api/mod.rs:104-111](src/api/mod.rs), [api/cases.rs:578](src/api/cases.rs)
- `ItemDatabase::weapon_of_item` lazily builds its name map with a `OnceLock`, which is good. `weapon(def)` is a linear scan, which is fine because the list is small. — [itemdb.rs:132](src/itemdb.rs), [itemdb.rs:148-150](src/itemdb.rs), [itemdb.rs:194-208](src/itemdb.rs)
- Workshop details are cached in `static DETAILS` for the process lifetime. Search result pages are not cached. — [api/workshop.rs:17](src/api/workshop.rs), [api/workshop.rs:68-100](src/api/workshop.rs)
- Images are cached on disk by a SHA-1 of the URL, and served with `Cache-Control: max-age=86400`. `/theme/background` and `/theme/custom.css` are re-read from disk on every request with `no-cache`, and `background_path()` stats up to 4 files. — [media.rs:24-31](src/media.rs), [main.rs:73-99](src/main.rs), [media.rs:60-65](src/media.rs)

### Inferences
- Move the regex in `newer()` to a `static LazyLock<Regex>`, or replace it with `l.rsplit(|c: char| !c.is_ascii_digit()).next()`. The same applies to `latest_versions`. Low impact per call, but it is a trivial fix.
- Cache `PlayerLoadout` in `Inv` by `loadout.json` mtime, and keep a removed-count in memory. This saves one or two file reads and JSON parses per inventory or case call. The impact is medium on large loadouts and HDDs.
- For `cases.inventory`:
  - Call `prices.ensure()` once per request rather than once per item.
  - Pre-lowercase price keys only once (already done at [prices.rs:65-68](src/prices.rs)).
  - Consider paging, or a delta, instead of returning up to 50k views.
  Impact: high for users with large inventories. The cost is linear in item count, on both serialization and the webview's `JSON.parse`.
- Debounce `Inv::save` on rapid `cases.open` (for example, write on a background thread with coalescing). Today every open does a full rewrite of a file that can be multi-MB, while holding both locks.
- Give `/theme/background` an ETag or mtime-based cache instead of `no-cache`, so a page reload doesn't re-transfer the image.

### Gaps
- Typical inventory size and `loadout.json` size on real installs are unknown.

## Q4. Window config (tauri.conf.json and builder)

### Takeaway
The window is created in code (`"windows": []`), with `visible(false)` and no transparency or custom decorations, which is good. But `show()` comes before the page loads. The background color is set after build instead of on the builder. Devtools is compiled in for release, with a runtime `--devtools` flag. CSP is `null`.

### Cited Findings
- `tauri.conf.json` contents:
  - `"app": { "withGlobalTauri": true, "windows": [], "security": { "csp": null } }`
  - `"build": { "frontendDist": "web" }`
  - `"bundle": { "active": false }`
  — [tauri.conf.json:1-10](tauri.conf.json)
- Builder settings: `.visible(false)`, `.center()`, `inner_size(1400,900)`, `.devtools(devtools)`, and a custom `data_directory(util::data_dir().join("webview"))`. It sets no `transparent`, no `decorations(false)` and no `shadow` override. — [main.rs:125-149](src/main.rs)
- `set_background_color` is called in `apply_title_bar` after `build()`, just before `show()`. The builder never gets `.background_color(...)`. — [api/theme.rs:241-247](src/api/theme.rs), [main.rs:150](src/main.rs), [main.rs:188-189](src/main.rs)
- The window is resized and re-centered after `build()` using monitor info. It may then be moved and resized again from saved settings, and maximized, all before `show()`. — [main.rs:151-186](src/main.rs)
- `fit_zoom` runs after `show()` and calls `set_zoom` when the window is bigger than 1480x920. This causes a visible re-layout and zoom just after the first paint on large or maximized windows. — [main.rs:189-191](src/main.rs), [main.rs:106-122](src/main.rs)
- Cargo: `tauri = { version = "2", features = ["devtools"] }`. This enables the devtools API in release builds; the runtime gate is `cfg!(debug_assertions) || --devtools`. — [Cargo.toml:16](Cargo.toml), [main.rs:125](src/main.rs)
- Capabilities: only `core:default` for window `main`. — [capabilities/default.json:1-6](capabilities/default.json)

### Inferences
- Pass the theme background on the builder: `WebviewWindowBuilder::background_color(Color(r,g,b,255))`. The theme is already read at [main.rs:126](src/main.rs). This way the native window and webview paint the theme color from the first frame. Keep `apply_title_bar` for the DWM attributes. Impact: removes the white or default flash on WebView2.
- Move `show()` (and `fit_zoom` before it) to after the page signals ready, or to `on_page_load` Finished. `set_zoom` before `show` avoids the post-show zoom jump.
- Compute the final size and position before `build()`, from saved settings when they are valid, else from monitor fit. Then call `.inner_size()` and `.position()` on the builder, so only one native resize happens. Low impact, but it removes extra layout passes.
- Dropping the `devtools` feature from release builds (make it a cargo feature) removes the devtools code paths. The startup impact is small or negligible: WebView2 ships devtools in the runtime. The bigger reason is security and hygiene, and it would drop the `--devtools` flag in release.
- `csp: null` has no performance cost, but it is noted for completeness.

### Gaps
- Whether the frontend sends a ready signal is outside this backend scope (see the frontend notes).

## Q5. Cargo release profile and binary size

### Takeaway
The release profile is `opt-level = "s"`, `lto = "thin"`, `strip = true`, with default `codegen-units` (16) and `panic` (unwind). That is a reasonable size-first setup. `opt-level = "s"` trades some CPU speed in JSON parsing and regex for size. `panic = "abort"` and `codegen-units = 1` are left on the table.

### Cited Findings
- `[profile.release] opt-level = "s"`, `lto = "thin"` (comment: "Thin LTO, default codegen units: a few % bigger exe, builds on every core."), `strip = true`. There is no `codegen-units` and no `panic` setting. — [Cargo.toml:39-43](Cargo.toml)
- Heavy dependencies that add to binary size:
  - `ureq` 3 with gzip + brotli
  - `regex`, `zip`, `chrono`, `sysinfo`
  - `netdev`
  - `rfd` (native dialogs)
  - `roxmltree`
  - `arboard`
  - `tauri` with `devtools`
  — [Cargo.toml:15-33](Cargo.toml)
- `build.rs` embeds `LanSkinTester.dll` and `gamedata/lanskintester.json` into the exe with `include_bytes!` when `LST_PLUGIN_DIR` is set. This makes the exe larger by the plugin's size; the bytes are only touched when the plugin is installed. — [build.rs:1-25](build.rs), [installer.rs:373-378](src/installer.rs)
- The debug binary in `target/debug` is 355 MB. That does not say anything about release size, and no release artifact is present in `target/`. — local `ls -la target/debug/LanSkinTesterLauncher`

### Inferences
- `opt-level = 3` (or `2`) for the main crate, with `"s"` kept for deps via `[profile.release.package."*"]`, or simply `opt-level = 3`. This speeds up `serde_json` parsing of the item DB and inventory, at roughly +10-30% exe size (estimate). Combine it with the Q1 fix, which removes the parse from the critical path anyway.
- `panic = "abort"` shrinks the binary, removing the unwind tables. Caution: `App::state()` relies on `unwrap_or_else(|e| e.into_inner())` for poison recovery ([api/mod.rs:152-159](src/api/mod.rs)). `on_ui` panics on purpose ([api/mod.rs:233](src/api/mod.rs)). And `spawn_blocking` turns a worker panic into an `Err` string ([main.rs:49-51](src/main.rs)). With abort, any panic in an IPC worker would kill the whole launcher, so this is a behavior change, not a free win.
- `codegen-units = 1` with `lto = "fat"` gives a slightly smaller and faster exe, but much slower builds. The existing comment shows thin LTO was chosen for build time. The startup impact is marginal, a few ms of page-in for a smaller exe.
- Exe size affects cold start mainly through disk page-in. With `strip` and `"s"`, this is likely a minor factor next to webview creation and the item-DB parse.

### Gaps
- No release build size was measured: there is no Windows toolchain run here, and `target/x86_64-pc-windows-msvc` holds only CACHEDIR.TAG.

## Q6. How the frontend is served (custom protocol vs embedding) and compression

### Takeaway
`web/` (about 876 KB on disk, including roughly 360 KB of woff2 fonts) is embedded into the binary at compile time through `frontendDist` and `generate_context!`, and served from memory by Tauri's built-in asset protocol. Dynamic content (item images, theme background, custom.css) goes through the custom `lst://` async protocol.

### Cited Findings
- `"build": { "frontendDist": "web" }` and `tauri::generate_context!()` embed `web/` assets. The window loads `WebviewUrl::App("index.html")`. — [tauri.conf.json:6](tauri.conf.json), [main.rs:267](src/main.rs), [main.rs:127](src/main.rs)
- `web/` totals 876 KB. The largest files are:
  - `fonts/opendyslexic-latin-{400,700}` (115 KB, 120 KB)
  - `css/glass.css` (61 KB)
  - `js/pages/inventory.js` (57 KB)
  - `js/lineups-data.js` (49 KB)
  - `app.png` (45 KB)
  - five Inter weights at about 24 KB each
  — local `du -sh web` and `ls -la` listing
- `withGlobalTauri: true` injects the global `__TAURI__` bundle into every page. — [tauri.conf.json:7](tauri.conf.json)
- `lst://localhost/...` is handled by `register_asynchronous_uri_scheme_protocol` → a thread per request → `protocol()`. Images get `max-age=86400`; theme files get `no-cache`. — [main.rs:54-102](src/main.rs), [main.rs:230-236](src/main.rs)
- The `ureq` client uses the `gzip` and `brotli` features for HTTP downloads. That covers network fetches, not the asset serving. — [Cargo.toml:19](Cargo.toml)

### Inferences
- Embedded assets are served from memory, so there is no disk I/O for the UI shell. Tauri's codegen compresses embedded assets with brotli by default (`bundle`/`build` compression). This is from my knowledge of Tauri 2's `tauri-codegen` defaults and was not verified in this repo. The serving cost is negligible. Front-end parse and execute time, and the 2 dyslexia fonts if they are loaded eagerly, matter more.
- Lazy-load OpenDyslexic with `font-display` and load it only when that setting is on. Lazy-load page modules: that is in frontend scope, noted for the report writer.
- `/theme/background` with `no-cache` plus a full `fs::read` on every request means a large user background image is re-read on every reload. Add `ETag` or `Last-Modified`.

### Gaps
- Whether Tauri's asset compression is enabled or disabled here: `tauri.conf.json` has no explicit `build.removeUnusedCommands` or compression keys. The default was not verified against the generated code.

## Ranked fix list (summary for the report writer)
1. **Load the item DB off the main thread, and parse it in a single pass** ([itemdb.rs:152-191](src/itemdb.rs), [util.rs:100-104](src/util.rs), [api/mod.rs:117](src/api/mod.rs)). Impact: high on time-to-window.
2. **Show the window after the page is ready, and set `background_color` on the builder** ([main.rs:189](src/main.rs), [api/theme.rs:241](src/api/theme.rs)). Impact: high on perceived smoothness (no flash, no zoom jump).
3. **Shrink the time `State` is held in the 1 s poll, the 5 s watch and the 3 s beacon.** Cache `find_server` and `metamod_enabled`, compute `test_mode_on` once, and do I/O outside the lock ([api/play.rs:634-658](src/api/play.rs), [session.rs:112-135](src/session.rs), [session.rs:201-205](src/session.rs), [api/friends.rs:557-574](src/api/friends.rs)). Impact: high on IPC latency jitter and idle CPU.
4. **Don't hold `State` during start-up discovery** in `api::started` / `set_game` / `discover_modes` ([api/shell.rs:190-203](src/api/shell.rs), [api/mod.rs:260-278](src/api/mod.rs)). Impact: medium on the first data paint.
5. **`cases.inventory` and case opens:**
   - Cache the loadout and the removed-count.
   - Call `ensure()` once per request.
   - Debounce full-inventory saves.
   - Consider paging the 50k-item payload.
   ([api/cases.rs:274-283](src/api/cases.rs), [api/cases.rs:531-549](src/api/cases.rs), [api/cases.rs:134-142](src/api/cases.rs)). Impact: medium to high for big inventories.
6. **`lst://` thread-per-request → bounded pool; shorter HTTP timeout for interactive and image fetches** ([main.rs:233](src/main.rs), [media.rs:32-38](src/media.rs), [util.rs:124](src/util.rs)). Impact: medium on image-heavy pages.
7. **Cache NIC addresses in the UDP listener** ([net.rs:660](src/net.rs)). Impact: low to medium.
8. **Make the `newer()` regex static** ([api/setup.rs:270](src/api/setup.rs)). Impact: low.
9. **Use a short timeout for exit UPnP cleanup** ([main.rs:272](src/main.rs), [net.rs:484](src/net.rs)). Impact: faster close when port mapping was used.
10. **Release profile:** consider `opt-level = 3` for parse speed, and gate the `devtools` feature. Treat `panic = "abort"` with care ([Cargo.toml:16](Cargo.toml), [Cargo.toml:39-43](Cargo.toml)). Impact: low to medium.
