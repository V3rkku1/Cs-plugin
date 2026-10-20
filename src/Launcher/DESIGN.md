# Launcher UI: how it is built

The launcher is a Tauri app (Rust): one window (WebView2 on Windows) showing `src/Launcher/web/` (HTML, CSS,
JS), embedded in the exe. The page calls Rust with `invoke("call", { name, args })`, gets events as "lst", and
loads pictures, the theme background and custom.css through the `lst://` protocol.

```
web/index.html          backdrop, SVG refraction filter, loads js/app.js
web/css/glass.css       the whole design system (Liquid Glass): tokens, glass, controls, layout
web/js/vendor/          preact + hooks + htm in one file (no build step)
web/js/api.js           call(), tryCall(), useApi(), useEvent(), on(), img(), toast(), navigate()
web/js/ui.js            shared components (see below)
web/js/icons.js         <Icon name=...> line icons
web/js/app.js           sidebar, page switching, keyboard shortcuts, toasts
web/js/pages/*.js       one file per page, exporting <XxxPage>
src/main.rs             window, lst:// protocol, the 1-second poll
src/api/mod.rs          App (state, log, push(), toast(), changed(), pick() for dialogs), the call dispatcher
src/api/<page>.rs       the page's calls: match name { "page.name" => ... }
src/*.rs                the rest: server settings, installer, game folder, item data, prices, network...
```

Run it: `cargo run --manifest-path src/Launcher/Cargo.toml` (Windows; on Linux it needs webkit2gtk). Tests:
`cargo test`. The plugin goes inside the exe when `LST_PLUGIN_DIR` points at its build (build.sh does this).

## Rust calls

```rust
// src/api/thing.rs
#[derive(Deserialize, Default)]
#[serde(rename_all = "camelCase", default)]
struct SetArgs { bots: i32, map: Option<String> }       // the page sends camelCase JSON

pub fn call(app: &Arc<App>, name: &str, a: &Value) -> R<Value> {
    match name {
        "thing.state" => ok(json!({ "name": "x", "on": true })),       // returned as JSON
        "thing.set" => {
            let a: SetArgs = arg(a)?;                                   // null/missing = Default
            let mut st = app.state();
            st.settings.server.bots = a.bots;
            app.save(&st);
            drop(st);                                                   // never hold state across I/O
            app.changed();                                              // every page reloads its data
            ok(())
        }
        _ => Err(unknown(name)),
    }
}
```

* `app.state()` is the shared `State` (settings, game folder, item data, each page's state) behind one mutex.
  Don't hold it across a download or a dialog. Inventory calls (`cases.`, `inventory.`, `tradeup.`) also hold
  the inventory gate (`app.inv()`), always taken before `state`.
* `Err(bad("message"))` shows the message as a red toast (not logged). Other errors (`?` on io errors) are
  logged as FAILED and also shown.
* `app.toast(text, kind)` shows a toast; `app.push("event.name", data)` sends a live event (progress etc.).
* File and folder pickers: `app.pick(|d| d.pick_folder())` (runs on the window's thread). Ask "are you sure"
  in the page (`confirm()`), not with a message box.
* Files the plugin or older launchers read (launcher.json, loadouts.json, inventory.json, item data) keep
  their PascalCase names: those structs use `#[serde(rename_all = "PascalCase")]` and go to the page through
  `util::web()`, which renames them to camelCase like the page expects.

## Page JS

```js
import { html, useState } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, useEvent } from "../api.js";
import { Page, Section, Row, Button, Toggle, Segmented, Slider, Stepper, TextField, Select, Badge,
         StateIcon, Progress, Icon, confirm, prompt, sheet } from "../ui.js";

export function ThingPage() {
  const [state] = useApi("page.state");            // reloads on every "changed" event
  if (!state) return html`<${Page} title="Thing" />`;
  return html`<${Page} title="Thing" subtitle="What this page is for.">
    <div class="cols wrap" style="--n:2">
      <div class="stack">
        <${Section} title="Group" footer="Small print under the card.">
          <${Row} icon=${{ name: "bot", color: "orange" }} title="Bots" subtitle="How many">
            <${Stepper} value=${state.bots} min=${0} max=${31} onChange=${v => tryCall("page.set", { bots: v })} />
          <//>
        <//>
      </div>
    </div>
  <//>`;
}
```

* `tryCall` = call and ignore the error (it is already shown as a toast). Use `call` when you need the result.
* Components: `Page` (large title), `Section` (caption + glass card; `pad` for free content), `Row`
  (title / subtitle / accessory; `onClick` makes it a button row; `icon={name,color}`), `Button`
  (`kind`: default | prominent | plain | destructive, `size`: sm | lg, `icon`, `busy`, `tint`),
  `Toggle`, `Segmented` (`cols` to wrap), `Slider`, `Stepper`, `TextField` (`multiline`, `icon`, `onEnter`),
  `Select` (glass pop-up menu), `Badge`, `StateIcon`, `Progress`, `Icon`, `confirm()`, `prompt()`, `sheet()`.
* Layout helpers in glass.css: `.cols` (+ `--n`, `.wrap` stacks on narrow windows), `.stack`, `.hstack`,
  `.tiles` (+ `--tile` min width) with `.tile.glass.sheen` cards, `.muted`, `.faint`, `.small`.
* Colours: `var(--blue|green|red|orange|yellow|purple|pink|teal|indigo)`, text `--text`, `--text-2`, `--text-3`.
* Pictures from the internet go through the launcher cache: `<img src=${img(url, 256)} />`.
* Page-only CSS goes in `web/css/pages/<page>.css`, linked from index.html.

## Look

Liquid Glass: content floats on glass over a living colour backdrop. Keep to the system: inset grouped
sections, 56px rows, capsule buttons, one prominent (blue) action per area, large bold page titles,
iOS colours for meaning (green ok / running, orange warning, red danger). No hard borders, no flat grey boxes.

## Run it

`cargo run` in src/Launcher (web/ is built into the exe: run it again after editing). Debug builds have the
developer tools (right-click is off: open them with Ctrl+Shift+I); `--devtools` allows them in a release build,
`--page live` opens one page.
