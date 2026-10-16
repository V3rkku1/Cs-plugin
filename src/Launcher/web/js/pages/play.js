// Play: the server's status with one clear action, and the quick starts (built in and saved).
import { html, useState } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, on } from "../api.js";
import { Page, Section, Button, Icon, confirm, prompt } from "../ui.js";

// The launcher asks before some actions (restart for retakes, start without the router port, replace a
// setup, stop while CS2 runs). Listening here, at module level, catches F5 / Shift+F5 from any page.
on("play.ask", async a => {
  if (await confirm({ title: a.title, message: a.message, ok: a.ok, danger: a.danger })) tryCall(a.call, a.args);
});

export const go = page => window.dispatchEvent(new CustomEvent("navigate", { detail: page }));

/** play.state, reloaded on every change and whenever the running server's status changes. */
export const usePlay = () => useApi("play.state", undefined, ["play.tick"]);

/** Runs a call and shows a short "done" state on its button. Returns [run, doneKey, busyKey]. */
export function useFlash() {
  const [done, setDone] = useState(null);
  const [busy, setBusy] = useState(null);
  const run = async (key, name, args) => {
    setBusy(key);
    try {
      await call(name, args);
      setDone(key);
      setTimeout(() => setDone(d => (d === key ? null : d)), 1800);
    } catch { /* toast shown */ }
    finally { setBusy(null); }
  };
  return [run, done, busy];
}

/** Copy buttons for this PC's address and invite code, with a short "Copied" on the pressed one. */
export function CopyButtons({ address, size = "sm", message = false }) {
  const [run, done, busy] = useFlash();
  const button = (what, label, kind) => html`<${Button} size=${size} kind=${kind} disabled=${!address} busy=${busy === what}
      icon=${done === what ? "check" : "copy"} onClick=${() => run(what, "friends.copy", { what, address })}>
    ${done === what ? "Copied" : label}<//>`;
  return html`<div class="hstack">
    ${button("address", "Copy IP", "default")}
    ${button("code", "Copy Invite Code", "prominent")}
    ${message && button("message", "Copy Invite Message", "default")}
  </div>`;
}

const tileLooks = {
  "DM vs Expert bots": ["crosshair", "blue"],
  "Free-for-all": ["shuffle", "purple"],
  "Skin showcase": ["sparkles", "pink"],
  "Aim practice": ["target", "orange"],
  "Competitive 5v5": ["shield", "green"],
  "Arms Race": ["bolt", "teal"],
  "Hacker vs bots": ["hacker", "green"],
  "Retakes": ["clock", "red"],
  "Plugin retakes": ["layers", "indigo"],
  "Retake buy": ["tag", "yellow"],
  "Demolition": ["map", "orange"],
  "Weapons Expert": ["star", "yellow"],
  "AWP Deathmatch": ["crosshair", "green"],
  "Flying Scoutsman": ["sparkles", "teal"],
  "Knife Fight": ["skins", "red"],
  "Deagle Headshots": ["target", "red"],
  "1v1 Aim Duel": ["person", "indigo"],
  "Wingman 2v2": ["friends", "blue"],
  "Rush 3v3": ["run", "pink"],
  "Catch the hacker": ["eye", "purple"],
};

function Stat({ icon, label, value, color = "blue" }) {
  return html`<div class="stat">
    <div class="stat-label"><span style=${`color:var(--${color})`}><${Icon} name=${icon} size=${14} stroke=${2} /></span>${label}</div>
    <div class="stat-value">${value}</div>
  </div>`;
}

function Hero({ state }) {
  const s = state.settings;
  const st = state.status;
  const on = state.testMode;
  const phase = !state.ready ? "setup" : !on ? "ready" : st ? "running" : "starting";
  const label = { setup: "Setup needed", ready: "Ready", running: "Running", starting: "Starting…" }[phase];
  const title = phase === "running" ? st.map : phase === "starting" ? "Server starting…" : phase === "ready" ? "Ready to play" : "Finish setup first";
  const text = {
    setup: "Open Setup and press Install Everything. Then come back here and press Start.",
    ready: "Pick a quick start below, or press Start. The server opens in its own window and CS2 joins it.",
    starting: state.liveDetail,
    running: "VAC is off until you press Stop. Only start CS2 from here until then - starting it from Steam could get you VAC-kicked.",
  }[phase];
  const mode = s.mode === "Retakes" && s.retakeStyle === 1 ? "Plugin retakes" : s.mode;
  const bots = s.bots === 0 ? "None" : `${s.bots} · ${state.difficulties[s.difficulty]}`;

  return html`<section class=${`hero glass ${phase}`}>
    <div class="hero-glow"></div>
    <div class="hero-main">
      <div class="hero-badge"><span class="pulse"></span>${label}</div>
      <h2>${title}</h2>
      <p class="hero-text">${text}</p>
      <div class="stats">
        <${Stat} icon="game" label="Mode" value=${st ? st.mode : mode} color="purple" />
        <${Stat} icon="map" label="Map" value=${st ? st.map : (s.workshopMap ? "Workshop" : s.map)} color="teal" />
        <${Stat} icon="person" label="Players" value=${st ? st.humans : "–"} color="green" />
        <${Stat} icon="bot" label="Bots" value=${st ? st.bots : bots} color="orange" />
      </div>
      ${state.startError && html`<div class="hero-error"><${Icon} name="warning" size=${16} stroke=${2} /><span>${state.startError}</span></div>`}
      <div class="hero-actions">
        ${on
          ? html`<${Button} kind="prominent" size="lg" icon="refresh" title="Send the Live Server settings to the running server (F5)"
              onClick=${() => tryCall("play.apply")}>Apply Changes<//>`
          : html`<${Button} kind="prominent" size="lg" icon="play" disabled=${!state.ready} busy=${state.busy} title="Start the server and CS2 (F5)"
              onClick=${() => tryCall("play.start")}>${state.busy ? "Starting…" : "Start"}<//>`}
        ${on && html`<${Button} size="lg" icon="game" onClick=${() => tryCall("play.openGame")}>Open CS2<//>`}
        ${on && html`<${Button} size="lg" kind="destructive" icon="stop" title="Close the server and CS2, back to normal (Shift+F5)"
            onClick=${() => tryCall("play.stop")}>Stop<//>`}
        ${!state.ready && html`<${Button} size="lg" icon="setup" onClick=${() => go("setup")}>Open Setup<//>`}
        <${Button} kind="plain" icon="sliders" onClick=${() => go("live")}>Server Settings<//>
      </div>
    </div>
    <aside class="hero-invite">
      <div class="invite-head"><span class="row-icon" style="--c: var(--indigo)"><${Icon} name="friends" size=${18} stroke=${2} /></span>Invite Friends</div>
      <div class=${"invite-address" + (state.invite.address ? " mono" : " faint")}>${state.invite.address ?? "No address yet"}</div>
      <div class="small muted">${state.invite.address ? `${state.invite.how} · change on the Friends page` : state.invite.text}</div>
      <${CopyButtons} address=${state.invite.address} />
      <${Button} kind="plain" size="sm" onClick=${() => go("friends")}>Friends Page <${Icon} name="chevronRight" size=${14} /><//>
    </aside>
  </section>`;
}

function Tile({ preset, index, disabled }) {
  const [icon, color] = preset.saved ? ["save", "indigo"] : tileLooks[preset.title] ?? ["play", ["blue", "purple", "pink", "orange", "green", "teal"][index % 6]];
  const remove = async e => {
    e.stopPropagation();
    if (await confirm({ title: "Delete setup?", message: `Delete the saved setup "${preset.title}"?`, ok: "Delete", danger: true }))
      tryCall("play.deleteSetup", preset.title);
  };
  return html`<button class="tile quick glass sheen" style=${`--c: var(--${color})`} disabled=${disabled}
      title=${preset.saved ? `Your setup · ${preset.subtitle}` : preset.subtitle}
      onClick=${() => tryCall("play.preset", { title: preset.title, saved: preset.saved })}>
    <span class="quick-icon"><${Icon} name=${icon} size=${20} stroke=${2} /></span>
    <span class="title">${preset.title}</span>
    <span class="subtitle">${preset.subtitle}</span>
    ${preset.saved && html`<span class="tile-delete" role="button" title="Delete this setup" onClick=${remove}><${Icon} name="x" size=${13} stroke=${2.4} /></span>`}
  </button>`;
}

export function PlayPage() {
  const [state] = usePlay();
  if (!state) return html`<${Page} title="Play" />`;

  const save = async () => {
    const name = await prompt({ title: "Save Setup", message: "Saves the current Live Server settings (mode, map, bots, rules) as a quick start.", placeholder: "Name", ok: "Save" });
    if (name?.trim()) tryCall("play.saveSetup", { name });
  };
  const builtIn = state.presets.filter(p => !p.saved);
  const saved = state.presets.filter(p => p.saved);

  return html`<${Page} title="Play" subtitle="Pick a quick start, or press Start.">
    <${Hero} state=${state} />

    <${Section} title="Quick Start" note=${state.testMode ? "applies to the running server" : "starts the server right away"} class="quick-section">
      <div class="tiles quick-tiles">
        ${builtIn.map((p, i) => html`<${Tile} key=${p.title} preset=${p} index=${i} disabled=${!state.hasGame} />`)}
      </div>
    <//>

    <${Section} title="Your Setups" note=${saved.length ? "× deletes one" : ""} class="quick-section">
      <div class="tiles quick-tiles">
        ${saved.map((p, i) => html`<${Tile} key=${p.title} preset=${p} index=${i} disabled=${!state.hasGame} />`)}
        <button class="tile quick add" onClick=${save}>
          <span class="quick-icon"><${Icon} name="plus" size=${20} stroke=${2.2} /></span>
          <span class="title">Save Current Setup</span>
          <span class="subtitle">${state.settings.mode} · ${state.settings.map} · ${state.settings.bots === 0 ? "no bots" : `${state.settings.bots} bots`}</span>
        </button>
      </div>
    <//>
  <//>`;
}
