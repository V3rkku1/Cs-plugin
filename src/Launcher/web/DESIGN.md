# Launcher UI: how it is built

The launcher window is a WebView2 showing `src/Launcher/web/` (HTML, CSS, JS). The C# side runs a
small ASP.NET server on 127.0.0.1 (random token) that serves these files and the API.

```
web/index.html          backdrop, SVG refraction filter, loads js/app.js
web/css/glass.css       the whole design system (Liquid Glass): tokens, glass, controls, layout
web/js/vendor/          preact + hooks + htm in one file (no build step)
web/js/api.js           call(), tryCall(), useApi(), useEvent(), useAction(), on(), img(), toast()
web/js/ui.js            shared components (see below)
web/js/icons.js         <Icon name=...> line icons
web/js/app.js           sidebar, page switching, keyboard shortcuts, toasts
web/js/pages/*.js       one file per page, exporting <XxxPage>
Web/Api.cs              [Call] dispatcher, OnUi() for dialogs, Push() events, Toast()
Web/Api.<Page>.cs       the page's calls: partial class Api, [Call("page.name")]
```

## C# calls

```csharp
public partial class Api
{
    public record ThingState(string Name, bool On);          // returned as JSON (camelCase)

    [Call("page.state")] private ThingState Get() => ...;    // no argument
    [Call("page.rename")] private void Rename(string name) => ...;          // one argument (JSON body)
    public record SetArgs(int Bots, string? Map);
    [Call("page.set")] private async Task Set(SetArgs a) { ... App.NotifyChanged(); }
}
```

* `App` is the shared `AppState` (settings, game folder, item db, log). After changing anything a
  page shows, call `App.NotifyChanged()`: every page reloads its `useApi` data.
* `throw new ApiException("message")` shows the message as a red toast (not logged).
  Other exceptions are logged as FAILED and also shown.
* `Toast(text, kind)` shows a toast; `Push("event.name", data)` sends a live event (progress etc.).
* Native dialogs (file / folder pickers) must run on the window thread: `await OnUi(() => ...)`.
  Ask "are you sure" in the page (`confirm()`), not with MessageBox.

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

`LanSkinTesterLauncher.exe --serve 5099 --token dev --web-dir <path to web>` serves the UI without a
window at http://127.0.0.1:5099/?t=dev ; with `--web-dir`, edits to web/ show on reload (no rebuild).
