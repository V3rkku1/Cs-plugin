// Setup: CS2 folder, the install checklist, maintenance, folders and the activity log.
import { html, useState, useEffect } from "../vendor/preact-htm.js";
import { tryCall, useApi, useEvent } from "../api.js";
import { Page, Section, Row, Button, Segmented, Toggle, Slider, Stepper, TextField, StateIcon, Progress, LogView, confirm, prompt } from "../ui.js";
import { PRESETS, DEFAULTS } from "../theme.js";
import { openUpdate } from "../update.js";

/** This launcher's version, update check and automatic updates. */
function LauncherSection() {
  const [app] = useApi("app.state");
  const [checked, setChecked] = useState(false);
  if (!app) return null;
  const check = async () => {
    const newer = await tryCall("app.checkUpdate");
    if (newer === false) setChecked(true);
  };
  const resetAll = async () => {
    if (await confirm({
      title: "Reset the launcher?",
      message: "Are you sure you want to do this? It deletes everything the launcher keeps:\n" +
        "• your inventory and case history\n• your equipped loadout\n• saved setups, friends and recent servers\n" +
        "• all settings, the theme and the backups\n\nThis can't be undone. The launcher restarts like a fresh install. " +
        "The mods in your CS2 folder stay (Uninstall Everything removes those).",
      ok: "Delete Everything", danger: true,
    })) tryCall("app.resetAll");
  };
  const subtitle = app.updateProgress ?? (app.updateAvailable ? `Build ${app.updateCommit} is out - you have ${app.version}`
    : checked ? `You have the newest (${app.version})` : `You have ${app.version}`);
  return html`<${Section} title="Launcher" footer="New versions show up here and as a popup while the launcher is open. With automatic updates on, a new version installs by itself when you open the launcher.">
    <${Row} icon=${{ name: "download", color: "blue" }} title=${app.updateAvailable ? "Update Available" : "Launcher Version"} subtitle=${subtitle}>
      ${app.updateAvailable
        ? html`<${Button} size="sm" kind="prominent" disabled=${!!app.updateProgress} onClick=${() => openUpdate(app)}>See What's New<//>`
        : html`<${Button} size="sm" busy=${app.checkingUpdate} onClick=${check}>Check<//>`}
    <//>
    <${Row} icon=${{ name: "refresh", color: "green" }} title="Update Automatically" subtitle="Install new versions when the launcher opens">
      <${Toggle} value=${app.autoUpdate} onChange=${v => tryCall("app.autoUpdate", v)} />
    <//>
    <${Row} icon=${{ name: "info", color: "orange" }} title="Windows Notifications" subtitle="When the launcher is in the background: the server is up, a friend joined, an update is out">
      <${Toggle} value=${app.notifications} onChange=${v => tryCall("app.notifications", v)} />
    <//>
    <${Row} icon=${{ name: "sparkles", color: "purple" }} title="Show the Tour" subtitle="The short first-start guide again">
      <${Button} size="sm" onClick=${() => window.dispatchEvent(new Event("show-tour"))}>Show<//>
    <//>
    <${Row} icon=${{ name: "trash", color: "red" }} title="Reset Launcher" subtitle="Delete all launcher data and start fresh">
      <${Button} size="sm" kind="destructive" disabled=${app.testMode} onClick=${resetAll}>Reset<//>
    <//>
  <//>`;
}

/** One install checklist row: state, progress bar while it runs, and its button. */
function StepRow({ step, p, call }) {
  const running = p !== undefined && p !== null;
  return html`<div class="row">
    <${StateIcon} state=${running ? "busy" : step.state} />
    <div class="text">
      <div class="title">${step.title}</div>
      <div class="subtitle">${step.detail}</div>
      ${running && html`<${Progress} value=${p} />`}
    </div>
    <div class="accessory">
      <${Button} size="sm" kind=${step.state === "ok" ? "default" : "prominent"} disabled=${!step.enabled}
        onClick=${() => tryCall(call, step.id)}>${step.action}<//>
    </div>
  </div>`;
}

const setTheme = changes => tryCall("setup.theme", changes);

/** Colour swatch that opens the system colour picker; saves when the picker closes. */
function ColorField({ value, onCommit }) {
  return html`<label class="color-field" style=${`--swatch:${value}`}>
    <input type="color" value=${value} onChange=${e => onCommit(e.currentTarget.value)} />
    <span class="mono">${value.toUpperCase()}</span>
  </label>`;
}

/** Slider that saves when released and shows the dragged value meanwhile. */
function ThemeSlider({ value, onCommit, ...props }) {
  const [v, setV] = useState(null);
  useEffect(() => setV(null), [value]);
  return html`<${Slider} ...${props} width=${200} value=${v ?? value} onChange=${setV} onCommit=${onCommit} />`;
}

function Appearance({ state }) {
  const t = { ...DEFAULTS, ...state.theme };
  const preset = PRESETS.find(p => p.accent === t.accent && p.background === t.background && p.panel === t.panel)?.id;
  const reset = async () => {
    if (await confirm({ title: "Reset appearance?", message: "Back to the default colours, corners and font, and no background picture?\n(custom.css is kept.)", ok: "Reset" }))
      tryCall("setup.themeReset");
  };
  const paste = async () => {
    const code = await prompt({ title: "Use a Theme Code", message: "Paste a theme code a friend sent you (it starts with LST1-).", placeholder: "LST1-…", ok: "Apply" });
    if (code?.trim()) tryCall("setup.themeImport", code.trim());
  };
  const num = v => String(Math.round(v));
  return html`
    <${Section} title="Theme" footer="Applies right away. Each colour can still be changed after picking a theme.">
      <div class="theme-presets">
        ${PRESETS.map(p => html`<button key=${p.id} class=${"theme-preset" + (p.id === preset ? " selected" : "")}
            style=${`--p-bg:${p.background};--p-panel:${p.panel};--p-accent:${p.accent}`}
            onClick=${() => setTheme({ accent: p.accent, background: p.background, panel: p.panel })}>
          <span class="preview"><span class="bar"></span><span class="dot"></span></span>
          <span class="name">${p.name}</span>
        </button>`)}
      </div>
    <//>
    <${Section} title="Appearance">
      <${Row} icon=${{ name: "sparkles", color: "blue" }} title="Accent Colour" subtitle="Main buttons, links and selected items">
        <${ColorField} value=${t.accent} onCommit=${v => setTheme({ accent: v })} /><//>
      <${Row} icon=${{ name: "layers", color: "indigo" }} title="Background Colour">
        <${ColorField} value=${t.background} onCommit=${v => setTheme({ background: v })} /><//>
      <${Row} icon=${{ name: "layers", color: "teal" }} title="Panel Colour" subtitle="Cards and the sidebar">
        <${ColorField} value=${t.panel} onCommit=${v => setTheme({ panel: v })} /><//>
      <${Row} icon=${{ name: "eye", color: "purple" }} title="Panel Opacity"
          subtitle=${state.reduceGlass ? "Reduce Transparency is on: panels stay solid" : "Lower lets the background (or your picture) show through"}>
        <${ThemeSlider} min=${40} max=${100} value=${Number(t.opacity)} format=${v => `${Math.round(v)}%`} onCommit=${v => setTheme({ opacity: num(v) })} /><//>
      <${Row} icon=${{ name: "layers", color: "blue" }} title="Title Bar" subtitle="The window's top bar (colours need Windows 11)">
        <${Segmented} options=${[{ value: "theme", label: "Match Theme" }, { value: "accent", label: "Accent" }, { value: "system", label: "Windows" }]}
          value=${state.theme.titlebar ?? "theme"} onChange=${v => setTheme({ titlebar: v })} /><//>
      <${Row} icon=${{ name: "shield", color: "orange" }} title="Corners">
        <${Segmented} options=${[{ value: "0", label: "Square" }, { value: "50", label: "Small" }, { value: "100", label: "Round" }, { value: "140", label: "Rounder" }]}
          value=${t.radius} onChange=${v => setTheme({ radius: v })} /><//>
      <${Row} icon=${{ name: "textsize", color: "blue" }} title="Font">
        <${Segmented} options=${[{ value: "inter", label: "Inter" }, { value: "segoe", label: "Segoe UI" }, { value: "system", label: "System" }, { value: "mono", label: "Mono" }, { value: "dyslexic", label: "OpenDyslexic" }]}
          value=${t.font} onChange=${v => setTheme({ font: v })} /><//>
      <${Row} icon=${{ name: "textsize", color: "green" }} title="Text Size">
        <${Segmented} options=${[{ value: "small", label: "Small" }, { value: "normal", label: "Normal" }, { value: "large", label: "Large" }]} value=${state.textSize}
          onChange=${v => tryCall("setup.appearance", { textSize: v })} /><//>
      <${Row} icon=${{ name: "import", color: "pink" }} title="Background Picture" subtitle=${state.theme.image ? "Your picture is the background" : "Any PNG, JPG or WebP"}>
        ${state.theme.image && html`<${Button} size="sm" onClick=${() => tryCall("setup.themeImageRemove")}>Remove<//>`}
        <${Button} size="sm" onClick=${() => tryCall("setup.themeImage")}>Choose…<//><//>
      ${state.theme.image && html`<${Row} icon=${{ name: "sun", color: "yellow" }} title="Darken Picture" subtitle="Keeps text readable over a bright picture">
        <${ThemeSlider} min=${0} max=${90} value=${Number(t.dim)} format=${v => `${Math.round(v)}%`} onCommit=${v => setTheme({ dim: num(v) })} /><//>`}
      <${Row} icon=${{ name: "layers", color: "indigo" }} title="Reduce Transparency" subtitle="Always fully solid panels">
        <${Toggle} value=${state.reduceGlass} onChange=${v => tryCall("setup.appearance", { reduceGlass: v })} /><//>
      <${Row} icon=${{ name: "sparkles", color: "pink" }} title="Reduce Motion" subtitle="No animations">
        <${Toggle} value=${state.reduceMotion} onChange=${v => tryCall("setup.appearance", { reduceMotion: v })} /><//>
    <//>
    <${Section} title="Share and Customize" footer="custom.css is in the launcher's data folder and loads after the built-in styles: change anything. Click the launcher after saving it.">
      <${Row} icon=${{ name: "copy", color: "blue" }} title="Theme Code" subtitle="Share your look, or use a friend's">
        <${Button} size="sm" onClick=${() => tryCall("setup.themeExport")}>Copy<//>
        <${Button} size="sm" onClick=${paste}>Paste…<//><//>
      <${Row} icon=${{ name: "terminal", color: "green" }} title="Custom CSS" subtitle="For full control: your own styles file">
        <${Button} size="sm" onClick=${() => tryCall("setup.customCss")}>Open custom.css<//><//>
      <${Row} icon=${{ name: "refresh", color: "red" }} title="Reset Appearance" subtitle="Default colours, corners and font">
        <${Button} size="sm" kind="destructive" onClick=${reset}>Reset<//><//>
    <//>`;
}

export function SetupPage() {
  const [state] = useApi("setup.state");
  const [progress, setProgress] = useState({});
  useEvent("setup.progress", ({ id, p }) => setProgress(old => ({ ...old, [id]: p })));
  if (!state) return html`<${Page} title="Setup" />`;

  const busy = state.busy;
  const allOk = state.steps.every(s => s.state === "ok");
  const uninstall = async () => {
    if (await confirm({
      title: "Uninstall everything?",
      message: "This removes Metamod, CounterStrikeSharp and the LAN Skin Tester plugin from your CS2 folder and puts gameinfo.gi back to normal.\n\nYour saved loadout is backed up to the launcher data folder.",
      ok: "Uninstall", danger: true,
    })) tryCall("setup.uninstall");
  };

  return html`<${Page} title="Setup" subtitle="Install, update or remove everything the skin test needs.">
    <div class="cols wrap" style="--n:2; grid-template-columns: minmax(0,1.25fr) minmax(0,1fr)">
      <div class="stack">
        <${Section} title="Game">
          <${Row} icon=${{ name: "game", color: "orange" }} title="Counter-Strike 2"
              subtitle=${state.folder ?? "Not found - press Browse or Find"} subtitleClass=${state.folder ? "" : "warn"}>
            <${Button} size="sm" onClick=${() => tryCall("setup.browse")}>Browse…<//>
            <${Button} size="sm" onClick=${() => tryCall("setup.find")}>Find<//>
          <//>
        <//>

        <${Section} title="Components" note=${allOk ? "everything is installed" : ""}>
          ${state.steps.map(step => html`<${StepRow} key=${step.id} step=${step} p=${progress[step.id]} call="setup.step" />`)}
          <div class="row block">
            <${Button} kind="prominent" size="lg" block icon="download" disabled=${!state.hasGame || busy} busy=${busy}
              onClick=${() => tryCall("setup.all")}>${busy ? "Working…" : allOk ? "Reinstall Everything" : "Install Everything"}<//>
          </div>
        <//>

        <${LauncherSection} />

        <${Section} title="Maintenance">
          <${Row} icon=${{ name: "download", color: "green" }} title="Update Everything"
              subtitle="Newest Metamod, CounterStrikeSharp, server plugins, skin data and launcher (also runs by itself after a CS2 update)">
            <${Button} size="sm" kind="prominent" disabled=${!state.hasGame || busy} onClick=${() => tryCall("setup.updateAll")}>Update<//>
          <//>
          <${Row} icon=${{ name: "refresh", color: "blue" }} title="Check Mod Updates" subtitle="Compare with the newest Metamod and CounterStrikeSharp">
            <${Button} size="sm" disabled=${busy} onClick=${() => tryCall("setup.checkUpdates")}>Check<//>
          <//>
          <${Row} icon=${{ name: "zip", color: "indigo" }} title="Install from Zip" subtitle="If a download fails: get the zip from the links below">
            <${Button} size="sm" disabled=${!state.hasGame || busy} onClick=${() => tryCall("setup.zip")}>Choose Zip…<//>
          <//>
          <${Row} icon=${{ name: "wrench", color: "teal" }} title="Repair" subtitle="Reinstall the plugin and fix leftovers after a crash">
            <${Button} size="sm" disabled=${!state.hasGame || busy} onClick=${() => tryCall("setup.repair")}>Repair<//>
          <//>
          <${Row} icon=${{ name: "trash", color: "red" }} title="Uninstall Everything" subtitle="Remove all mods from your CS2 folder (loadout is backed up)">
            <${Button} size="sm" kind="destructive" disabled=${!state.hasGame || busy} onClick=${uninstall}>Uninstall<//>
          <//>
        <//>

        <${Section} title="Folders and Links">
          <${Row} icon=${{ name: "folder", color: "blue" }} title="CS2 Folder" onClick=${() => tryCall("app.openFolder", "game")} />
          <${Row} icon=${{ name: "folder", color: "purple" }} title="Plugin Folder" onClick=${() => tryCall("app.openFolder", "plugin")} />
          <${Row} icon=${{ name: "folder", color: "orange" }} title="Server Configs" onClick=${() => tryCall("app.openFolder", "cfg")} />
          <${Row} icon=${{ name: "folder", color: "green" }} title="Launcher Data" onClick=${() => tryCall("app.openFolder", "data")} />
          <${Row} icon=${{ name: "link", color: "teal" }} title="Metamod Downloads" subtitle="Opens in your browser" onClick=${() => tryCall("app.openUrl", state.metamodPage)} />
          <${Row} icon=${{ name: "link", color: "teal" }} title="CounterStrikeSharp Downloads" subtitle="Opens in your browser" onClick=${() => tryCall("app.openUrl", state.cssPage)} />
        <//>
      </div>

      <div class="stack">
        <${Appearance} state=${state} />

        <${Section} title="Log">
          <${LogView} height=${520} />
        <//>
      </div>
    </div>
  <//>`;
}
