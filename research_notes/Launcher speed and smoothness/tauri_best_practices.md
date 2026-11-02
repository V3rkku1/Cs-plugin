# Tauri 2 on Windows (WebView2): startup speed and smoothness best practices

Scope: small Tauri 2.x launcher, Rust backend using ureq (blocking HTTP), Preact + htm frontend as plain ES modules, no bundler. Research done 2026-11-01. Sources mostly 2024-2026; versions noted where given.

## Startup: hidden window, background color, WebView2 cold-start cost, browser args, user data folder

### Takeaway
The cheapest wins are cosmetic and proven: set `backgroundColor` in the window config and an inline dark background plus `color-scheme` in `index.html` to kill the white flash, and optionally start `visible: false` and call `show()` after the first frame has painted. WebView2 cold start (process spin-up plus disk caches) is a fixed cost an app cannot remove. Microsoft's levers are: keep the Evergreen runtime, keep the user data folder (UDF) on the default local disk, and use one WebView. `additionalBrowserArgs` is not a documented startup accelerator, and GPU "bypass" flags were removed from at least one real app.

### Cited Findings
- Tauri `WindowConfig.visible`: "Whether the window is visible or not." Default `true`. — [Tauri config reference](https://v2.tauri.app/reference/config/)
- Tauri `WindowConfig.backgroundColor`: "Set the window and webview background color." Windows notes: "alpha channel is ignored for the window layer"; "On Windows 8 and newer, if alpha channel is not `0`, it will be ignored for the webview layer." — [Tauri config reference](https://v2.tauri.app/reference/config/)
- Tauri `transparent`: the Windows note says `noRedirectionBitmap` can help avoid a white flash when creating a transparent window. — [Tauri config reference](https://v2.tauri.app/reference/config/)
- WebView2's default background is white. The reported fix is to set the window `backgroundColor` so the native surface is dark before WebView paints. — [queryden issue #75](https://github.com/openidle-dev/queryden/issues/75), [Nembrix PR #110](https://github.com/Nembrix/nembrix/pull/110)
- Concrete white-flash fix from a real Tauri app (Nembrix PR #110) has three parts:
  - `tauri.conf.json` window `backgroundColor: "#1c1c20"`.
  - Inline `background:#1c1c20` and `color-scheme: dark` in `index.html`, so the first HTML paint is dark before the stylesheet loads.
  - State hydration from storage moved out of a post-paint `useEffect` and before the first `render()`, to avoid a content blink.
  — [Nembrix PR #110](https://github.com/Nembrix/nembrix/pull/110)
- Nembrix's separate follow-up (PR #127) sets `visible: false`, then calls `getCurrentWindow().show()` after two `requestAnimationFrame` callbacks. It targets a content-render blink, not the white flash. — [Nembrix PR #110 (describes #127)](https://github.com/Nembrix/nembrix/pull/110)
- Showing the window from JS needs the `core:window:allow-show` permission in capabilities. This is inferred from the Tauri v2 ACL model. The athas PR shows the same pattern of adding window permissions (`core:window:allow-minimize`). — [athas PR #728](https://github.com/athasdev/athas/pull/728)
- Reported bug: with `visible: false` and `show()` called inside the `setup()` hook, a later command that creates a child window made that child window appear and then disappear. — [tauri issue #9286 (via search summary)](https://github.com/tauri-apps/tauri/issues/9286)
- Microsoft (performance page dated 2026-01-12) describes cold start: "On a cold launch, WebView2 must spin up its processes and disk caches, which can introduce a noticeable delay". — [MS Learn: WebView2 performance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Microsoft: "Avoid rendering splash screens or simple dialogs with WebView2, due to startup costs" and "Initialize WebView2 only when displaying actual web content." — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Microsoft: "If Microsoft Edge and WebView2 versions match, and Microsoft Edge is running, the required WebView2 binaries are already in memory, improving launch performance." It also recommends the Evergreen runtime for ongoing performance fixes. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Microsoft on the UDF: "Keep the UDF in the default local app data folder, for performance". "Avoid slow drives or network shares". — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Microsoft: reuse a single WebView2 and navigate it to a new URL rather than destroying and re-creating one. Each WebView2 control spawns its own browser, renderer and GPU processes. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Microsoft: "Keep the initial payload light. Prefer sending static HTML initially". With SPA frameworks, "the entire framework code must be downloaded, parsed, and run, before anything can be displayed." Defer heavy components and lazy-load scripts after the initial UI appears. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Tauri `dataDirectory`: custom path for the webview's data (localStorage, cache), resolved relative to `localDataDir()` plus the window label. Webviews with different `additionalBrowserArgs` or `browserExtensionsEnabled` "must have different data directories". — [Tauri config reference](https://v2.tauri.app/reference/config/)
- Tauri `additionalBrowserArgs`: "Defines additional browser arguments on Windows." By default wry passes `--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection`. If you set this field, you must re-add those flags yourself. — [Tauri config reference](https://v2.tauri.app/reference/config/)
- Microsoft: do not disable the GPU (`disable-gpu`) except when troubleshooting. "The use of the GPU by WebView2 is critical for performance." — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- The athas Tauri app first added `--enable-gpu-rasterization --enable-zero-copy --ignore-gpu-blocklist`, then removed them before merge. The reason given was to stop "shipping WebView2 diagnostic GPU flags that bypass hardware safeguards." No measurements were reported. — [athas PR #728](https://github.com/athasdev/athas/pull/728)
- A Japanese write-up reports that `additionalBrowserArgs` flags (GPU disables and others, including the risky `--single-process`) cut memory from about 80-90 MB to about 30-40 MB. This is a memory claim, not a startup claim. — [zenn.dev article](https://zenn.dev/scirexs/articles/b761fc6c68c795)
- `useHttpsScheme` (default `false`) switches to `https://<scheme>.localhost` on Windows. Changing it between releases moves IndexedDB, cookies and localStorage, so the app loses access to old data. — [Tauri config reference](https://v2.tauri.app/reference/config/)
- Debug builds are noticeably slower than release builds for IPC and data transfer. A maintainer said "try comparing debug vs release mode, there should be a relatively big difference." — [tauri discussion #5511](https://github.com/orgs/tauri-apps/discussions/5511)

### Inferences
- Recommended launcher recipe:
  - In `tauri.conf.json`, set `backgroundColor` to the app's surface color.
  - In the `<head>` of `index.html`, put inline `<style>html,body{background:#xxx;color-scheme:dark}</style>`.
  - Optionally set `visible:false`, then call `getCurrentWindow().show()` after first render plus `requestAnimationFrame` x2.
  - Add a safety fallback for hidden-window mode: a Rust-side timer that shows the window after about 1-2 s if JS failed. Without it, a JS error leaves an invisible app.
- Do not add a WebView-rendered splash screen. Per Microsoft it costs more than it hides.
- Leave the UDF at the Tauri default (`%LOCALAPPDATA%\<identifier>\EBWebView`). Do not move it to a portable or network folder.
- Do not set `additionalBrowserArgs` for speed. If you set it, re-include the three default `--disable-features` entries.
- Always measure startup in release builds.

### Gaps
- No source gave measured millisecond numbers for WebView2 cold versus warm start in Tauri, or for the gain from `visible:false` plus show.
- No documented browser arg was found that shortens WebView2 startup.
- The exact Tauri default UDF path was not confirmed from docs in this session. The path given above is from prior knowledge.

## Rust side: async vs sync commands, spawn_blocking for ureq, release profile, devtools

### Takeaway
In Tauri 2, a command without `async` runs on the main thread, so a sync ureq call freezes the UI for the whole HTTP round trip. Mark it `#[tauri::command(async)]`, or make it `async` and wrap ureq in `tauri::async_runtime::spawn_blocking`. For release, use the profile recommended in the Tauri docs. Leave the `devtools` Cargo feature off in release, because it is opt-in.

### Cited Findings
- "Commands without the async keyword are executed on the main thread unless defined with `#[tauri::command(async)]`." — [Tauri: Calling Rust](https://v2.tauri.app/develop/calling-rust/)
- "Async commands are executed on a separate async task using async_runtime::spawn" and are "preferred in Tauri to perform heavy work in a manner that doesn't result in UI freezes or slowdowns." — [Tauri: Calling Rust](https://v2.tauri.app/develop/calling-rust/)
- Async commands cannot take borrowed args such as `&str` or `State<'_, T>` directly. Use owned types, or return a `Result`. — [Tauri: Calling Rust](https://v2.tauri.app/develop/calling-rust/)
- Real-world case: a Tauri plugin's sync network commands froze the UI for the whole HTTP round trip, up to a 30 s timeout. The fix was `#[tauri::command(async)]` on each I/O command. — [keylight-rust v0.6.1 release](https://github.com/keylight-dev/keylight-rust/releases/tag/v0.6.1)
- Creating a window inside a non-async command can deadlock on Windows. The docs require such commands to be `async`. — [tauri commit 69ae6f1](https://www.github.com/tauri-apps/tauri/commit/69ae6f14943f94285f4279c3a92f9003f74aacff)
- `tauri::async_runtime::spawn_blocking` runs a closure on an executor dedicated to blocking work. — [docs.rs tauri::async_runtime::spawn_blocking](https://docs.rs/crate/tauri/latest/target-redirect/x86_64-pc-windows-msvc/tauri/async_runtime/fn.spawn_blocking.html)
- Tauri's recommended release profile, verbatim:
  ```toml
  [profile.release]
  codegen-units = 1 # Allows LLVM to perform better optimization.
  lto = true # Enables link-time-optimizations.
  opt-level = "s" # Prioritizes small binary size. Use `3` if you prefer speed.
  panic = "abort" # Higher performance by disabling panic handlers.
  strip = true # Ensures debug symbols are removed.
  ```
  — [Tauri: App size](https://v2.tauri.app/concept/size/)
- `build.removeUnusedCommands: true` strips commands not allowed in capability files. It requires `tauri@2.4`, `tauri-build@2.1`, `tauri-plugin@2.1` and `tauri-cli@2.4`. It does not account for ACLs added at runtime. — [Tauri: App size](https://v2.tauri.app/concept/size/)
- Devtools: "requires `devtools` feature flag to enable it in **release** builds". So release builds exclude devtools unless the Cargo feature is enabled. On macOS it calls private APIs. — [Tauri config reference](https://v2.tauri.app/reference/config/)

### Inferences
- With ureq, the two options are equivalent in effect:
  - `#[tauri::command(async)] fn fetch(...) -> Result<T,String>`. Tauri runs a sync fn off the main thread.
  - `async fn` with `tauri::async_runtime::spawn_blocking(move || ureq::get(..).call()).await`.
- Never call blocking ureq directly inside a plain `async fn` command. It ties up a tokio worker thread.
- Startup prefetch: start ureq requests in `setup()` on a spawned or blocking thread, then push results to the frontend. Do not wait for the frontend to ask after load. This overlaps network time with WebView2 cold start.
- For a launcher where speed matters more than size, `opt-level = 3` is the documented alternative to `"s"`. The startup difference for a small app is likely negligible, because WebView2 dominates.
- `panic = "abort"` means a panic in a command kills the app. Keep commands returning `Result`.
- Check `Cargo.toml` for `tauri = { features = ["devtools"] }` and remove it for shipping builds.

### Gaps
- No benchmark was found that quantifies the Tauri startup gain from LTO, codegen-units or opt-level. The gain is likely small relative to WebView2 init.
- No source covered ureq in Tauri specifically. The pattern above is derived from the generic blocking-I/O guidance.

## Frontend: no-bundler ES module waterfalls, modulepreload, fonts, containment, animations, backdrop-filter, Preact tips

### Takeaway
Assets load from the local custom protocol, so there is no real network latency. The cost of an unbundled module graph is parse and evaluation plus sequential discovery round trips through the protocol handler. Add `<link rel="modulepreload">` for every module, or bundle once with esbuild. Keep the first paint as static HTML and CSS. Use `content-visibility:auto` for long lists, animate only `transform` and `opacity`, and avoid window transparency, acrylic and large blurs on Windows. With Preact, pass signals directly into htm templates so updates bypass VDOM diffing.

### Cited Findings
- `modulepreload` hints "convert a sequential import waterfall into a parallel fetch", because the browser can start all requests before parsing the module script. — [webjs PR #165](https://github.com/webjsdev/webjs/pull/165/files)
- `modulepreload` understands module dependency graphs, unlike plain preload, and only applies to `type="module"`. Support: Chrome 66+ and Edge 79+, so WebView2 is fine. — [modulepreload tool page](https://www.jquery.app/tools/modulepreload-tag-generator/)
- Preact's official no-build guide uses an import map for `preact`, `@preact/signals` and `htm/preact`. It uses esm.sh with `?external=preact` to avoid a duplicate Preact copy. — [Preact: No-Build Workflows](https://preactjs.com/guide/v11/no-build-workflows)
- Microsoft: static HTML "loads, parses, and renders very fast—faster than the time it would've taken JavaScript to produce the same UI". Defer heavy components and lazy-load scripts after the initial UI appears. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- Microsoft rendering advice:
  - Use CSS rather than JS for animations.
  - Debounce or throttle work.
  - Split long scripts.
  - Avoid layout thrashing, where a script alternates DOM reads and writes.
  — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)
- `content-visibility: auto` "enables the user agent to skip an element's rendering work, including layout and painting, until it is needed". Pair it with `contain-intrinsic-size` (the `auto` keyword remembers the last size). The demo dropped rendering from 232 ms to 30 ms (7x). Supported in Chrome and Edge 85+. — [web.dev: content-visibility](https://web.dev/articles/content-visibility)
- Fonts:
  - Preloading a font requires `crossorigin`, even for self-hosted fonts. Without it the preload misses the cache and the font is fetched twice.
  - WOFF2 is sufficient for modern engines.
  - Preload only the critical face.
  - `font-display: swap` shows fallback text immediately, at the cost of a possible layout shift.
  — [web.dev: Optimize web fonts](https://web.dev/learn/performance/optimize-web-fonts), [web.dev font best practices](https://web.dev/font-best-practices)
- Window transparency on Windows: a Tauri app PR states that `transparent: true` forces WebView2 into per-pixel alpha composition and loses GPU fast paths. It added `tauri.windows.conf.json` with `transparent: false`. Another app (OpenLess) reached the same conclusion. These are author reasoning, not measurements. — [athas PR #728](https://github.com/athasdev/athas/pull/728), [OpenLess PR #882](https://github.com/Open-Less/openless/pull/882.patch)
- The athas PR also added `will-change: opacity, transform, filter` to popups, scoped to Windows only via a `platform-windows` root class. — [athas PR #728](https://github.com/athasdev/athas/pull/728)
- window-vibrancy README: `apply_acrylic` and `apply_blur` "show bad performance when resizing or dragging the window" on Windows 10 v1903+ and Windows 11 22000+. — [window-vibrancy 0.8.0 README](https://docs.rs/crate/window-vibrancy/0.8.0/source/README.md)
- Resizing WebView2 is slow in general with hardware acceleration on (tauri #6322). No confirmed Tauri fix exists. Some users on Windows 11 report no lag. — [tauri issue #6322](https://github.com/tauri-apps/tauri/issues/6322)
- Preact Signals:
  - "With signals we can bypass Virtual DOM rendering and bind signal changes directly to DOM mutations."
  - A signal passed into a text position updates "in-place without Virtual DOM diffing".
  - A similar optimization applies to signals passed as DOM props.
  - Computed signals are lazy.
  - `batch()` merges multiple updates into one commit.
  — [Preact: Signals](https://preactjs.com/guide/v10/signals/)

### Inferences
- No-bundler option A:
  - Self-host `preact`, `htm` and `@preact/signals` ESM files. Never load them from a CDN at runtime in a launcher, because that adds network on every cold start and breaks offline use.
  - Use an import map.
  - Add `<link rel="modulepreload">` for every local module in `index.html`.
- No-bundler option B: a single `esbuild --bundle --minify --format=esm` step at release only. This turns N protocol requests into 1. It is a build step, though not a dev-loop bundler. Either option removes the waterfall. Option A keeps zero tooling.
- Render the shell (header and layout) as static HTML in `index.html`. Preact hydrates or replaces it, so the first paint does not wait on JS.
- CSS: animate `transform` and `opacity` only. Add `will-change` sparingly, only on elements about to animate.
  - Avoid `backdrop-filter` on large or full-window surfaces, and avoid animating `filter` or `backdrop-filter`.
  - Avoid the window-level `transparent:true` and acrylic effects.
  - A solid or semi-opaque background is the safe substitute on Windows.
- Use stable `key`s on list items, `memo` for heavy, rarely-changing subtrees, and signals for frequently-updated values (progress bars, download status). Then only the bound text or attribute node changes. These are standard Preact practice; only the signals claims are cited above.

### Gaps
- No source measured module-count cost over Tauri's custom protocol on Windows, or the bundled versus unbundled startup delta.
- No Tauri- or WebView2-specific source on CSS `backdrop-filter` cost was found. The transparency and acrylic findings are the closest evidence. The compositor-only-properties guidance (web.dev "Stick to compositor-only properties") was not fetched this session.
- Preact `memo` and key guidance was not fetched from the Preact docs this session.

## IPC: payload size, Channels, events vs commands, serialization cost

### Takeaway
IPC return values are JSON-serialized by default. That is fine for small launcher payloads, but use `tauri::ipc::Response` for large or binary data and `Channel` for streaming progress, such as downloads. Events are untyped, JSON-only and fire-and-forget. Batch messages. IPC on Windows is reported as notably slower than on macOS for large binary payloads.

### Cited Findings
- "Return values that implements serde::Serialize are serialized to JSON". This "can slow down your application if you try to return a large data such as a file or a download HTTP response." Use `tauri::ipc::Response` to return raw bytes without JSON. Commands can accept raw `ArrayBuffer`/`Uint8Array` bodies via `tauri::ipc::Request`. — [Tauri: Calling Rust](https://v2.tauri.app/develop/calling-rust/)
- "The Tauri channel is the recommended mechanism for streaming data such as streamed HTTP responses to the frontend." — [Tauri: Calling Rust](https://v2.tauri.app/develop/calling-rust/)
- "Unlike commands, events are not type safe, are always async, cannot return values and only supports JSON payloads." — [Tauri: Calling Rust](https://v2.tauri.app/develop/calling-rust/)
- Tauri v2 moved IPC onto the webview's custom URI scheme (PR #7170, 2023), with a postMessage fallback. 2.0 rc.3 added `InvokeResponseBody` for performance and fixed a hang in the postMessage fallback. — [tauri PR #7170](https://github.com/tauri-apps/tauri/pull/7170), [Tauri 2.0.0-rc.3 notes](https://tauri.app/release/tauri/v2.0.0-rc.3)
- A maintainer says v2 IPC is serialization-free for raw payloads but still goes through the fetch API. An informal Discord benchmark measured about 5 ms for 10 MB of binary on macOS versus about 200 ms on Windows. The benchmark is unverified. — [tauri discussion #11915 (via search summary)](https://github.com/orgs/tauri-apps/discussions/11915)
- A maintainer (Dec 2024) said "the new v2 IPC backend is the best we can offer for the near-ish future". Users reported that WebView2 shared memory "seems to be relatively slow". For maximum throughput the maintainer pointed to `register_uri_scheme_protocol`. — [tauri discussion #5511](https://github.com/orgs/tauri-apps/discussions/5511), [tauri discussion #5690](https://github.com/tauri-apps/tauri/discussions/5690)
- Microsoft: "Batch messages wherever possible, because frequent message passing can increase CPU usage". Implement asynchronous, batched communication. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)

### Inferences
- For a launcher, the payloads are small JSON objects (versions, news, settings), so JSON cost is negligible. The real wins are fewer round trips and avoiding chatty calls.
  - Merge startup data into one `get_initial_state` command.
  - Or have Rust prefetch in `setup()` and hand the result over in one call.
- Download or update progress should use a `Channel<Progress>` passed into the command. Throttle progress updates (for example every 100 ms or every 1%) rather than sending one per chunk.
- Images: serve them via the asset protocol or a URL, not base64 through IPC.

### Gaps
- No official Tauri benchmark of IPC latency on Windows was found. The 5 ms versus 200 ms figure is anecdotal.

## Known Tauri 2 / WebView2 performance issues and fixes

### Takeaway
The documented issues relevant to a launcher are:
- The white flash: fixed by `backgroundColor`, inline CSS and hidden-then-show.
- UI freezes from sync commands: fixed with async commands.
- Transparency and acrylic compositing cost on Windows: fixed by going opaque.
- Slow WebView2 resize: no fix.
- Slower large-payload IPC on Windows: use raw `Response` or a custom protocol.
- Runtime and environment problems, such as a missing or broken WebView2 runtime or elevation edge cases.

### Cited Findings
- White flash on Windows from WebView2's white default background. Fixed with the window `backgroundColor` plus inline HTML background. — [queryden #75](https://github.com/openidle-dev/queryden/issues/75), [Nembrix PR #110](https://github.com/Nembrix/nembrix/pull/110), [tauri issue #1564](https://github.com/tauri-apps/tauri/issues/1564)
- `visible:false` with `show()` in `setup()` breaks child windows created later via a command. — [tauri issue #9286](https://github.com/tauri-apps/tauri/issues/9286)
- Windows resize is slower in Tauri than in raw wry. It is attributed to WebView2 or Chromium with hardware acceleration, and the issue is unresolved. — [tauri issue #6322](https://github.com/tauri-apps/tauri/issues/6322)
- Transparent windows and acrylic or blur vibrancy degrade drag, resize and render performance on Windows. — [athas PR #728](https://github.com/athasdev/athas/pull/728), [window-vibrancy README](https://docs.rs/crate/window-vibrancy/0.8.0/source/README.md)
- Sync I/O commands freeze the UI. Fixed with `#[tauri::command(async)]`. — [keylight-rust v0.6.1](https://github.com/keylight-dev/keylight-rust/releases/tag/v0.6.1)
- A missing or corrupt WebView2 runtime makes the app open then close. Reinstalling the runtime fixes it. — [tauri issue #12030](https://github.com/tauri-apps/tauri/issues/12030)
- WebView2 fails to start when launched by an elevated Tauri app under Windows Administrator Protection. — [tauri issue #13926](https://github.com/tauri-apps/tauri/issues/13926)
- A WebView2 custom protocol handler can run off the main thread during shutdown or restart (reported on Tauri 2.10.3, code unchanged in 2.11.2). This is a correctness bug, not a speed issue. — [tauri issue #15408](https://github.com/tauri-apps/tauri/issues/15408)
- Microsoft notes that many past WebView2 memory and CPU issues were fixed in newer runtimes. Keep the runtime Evergreen, and troubleshoot by loading a minimal HTML page to separate runtime cost from content cost. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)

### Inferences
- Startup triage order: build in release, then compare against a minimal HTML page (per Microsoft), then check whether it is a cold or warm start. Use the Network panel in DevTools (debug build) to count module requests and find blocking JS.
- For profiling WebView2 startup itself, Microsoft's ETW profile (`WebView2.wprp`, https://aka.ms/wv2etw) with Windows Performance Analyzer is the official tool. — [MS Learn](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance)

### Gaps
- No Tauri-maintainer-published startup benchmark or official "performance" page for v2 was found. v2.tauri.app has a size page but no dedicated performance guide.
- Issue #9286 and #1564 details were taken from search summaries, not full fetches. Verify their current status before relying on them.
