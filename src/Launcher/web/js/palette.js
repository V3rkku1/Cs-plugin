// Ctrl+K: type to jump to a page or run an action (start a quick start, stop, copy the invite code...).
import { html, useState, useEffect, useRef } from "./vendor/preact-htm.js";
import { call, tryCall } from "./api.js";
import { Icon, sheet } from "./ui.js";

const pageItems = [
  ["play", "Play", "play"], ["live", "Live Server", "server"], ["inventory", "Inventory", "skins"], ["cases", "Cases", "key"],
  ["hacker", "Hacker Mode", "hacker"], ["workshop", "Workshop", "map"], ["friends", "Friends", "friends"], ["setup", "Setup", "setup"],
];

async function actions(navigate) {
  const play = await call("play.state").catch(() => null);
  const list = pageItems.map(([id, label, icon]) => ({ label: `Go to ${label}`, icon, group: "Pages", run: () => navigate(id) }));
  if (play) {
    if (play.testMode) {
      list.push({ label: "Apply changes to the server", icon: "refresh", group: "Server", run: () => tryCall("play.apply") });
      list.push({ label: "Open CS2", icon: "game", group: "Server", run: () => tryCall("play.openGame") });
      list.push({ label: "Stop the server", icon: "stop", group: "Server", run: () => tryCall("play.stop") });
    } else {
      list.push({ label: "Start the server", icon: "play", group: "Server", run: () => tryCall("play.start") });
    }
    if (play.invite?.address) {
      list.push({ label: "Copy invite code", icon: "copy", group: "Friends", run: () => tryCall("friends.copy", { what: "code", address: play.invite.address }) });
      list.push({ label: "Copy IP address", icon: "copy", group: "Friends", run: () => tryCall("friends.copy", { what: "address", address: play.invite.address }) });
    }
    for (const p of play.presets)
      list.push({ label: p.title, note: p.subtitle, icon: "bolt", group: "Quick starts", run: () => tryCall("play.preset", { title: p.title, saved: p.saved }) });
  }
  list.push({ label: "Full screen on / off (F11)", icon: "sparkles", group: "Window", run: () => tryCall("app.fullscreen") });
  list.push({ label: "Check for launcher updates", icon: "download", group: "Setup", run: () => tryCall("app.checkUpdate") });
  list.push({ label: "Check for component updates", icon: "refresh", group: "Setup", run: () => tryCall("setup.checkUpdates") });
  return list;
}

/** Simple fuzzy match: every typed word must appear somewhere in the label, group or note. */
const matches = (item, query) => query.toLowerCase().split(/\s+/).filter(Boolean)
  .every(word => `${item.label} ${item.group} ${item.note ?? ""}`.toLowerCase().includes(word));

export async function openPalette(navigate) {
  const items = await actions(navigate);
  const chosen = await sheet(close => html`<${Palette} items=${items} close=${close} />`);
  chosen?.run();
}

function Palette({ items, close }) {
  const [query, setQuery] = useState("");
  const [index, setIndex] = useState(0);
  const list = useRef();
  const shown = items.filter(i => matches(i, query)).slice(0, 40);
  useEffect(() => setIndex(0), [query]);
  useEffect(() => list.current?.querySelector(".active")?.scrollIntoView({ block: "nearest" }), [index]);
  const key = e => {
    if (e.key === "ArrowDown") { setIndex(i => Math.min(i + 1, shown.length - 1)); e.preventDefault(); }
    else if (e.key === "ArrowUp") { setIndex(i => Math.max(i - 1, 0)); e.preventDefault(); }
    else if (e.key === "Enter" && shown[index]) close(shown[index]);
  };
  return html`<div class="palette">
    <label class="field palette-field">
      <${Icon} name="search" size=${18} />
      <input autofocus placeholder="Search pages, quick starts and actions…" value=${query}
        ref=${el => el && setTimeout(() => el.focus(), 0)}
        onInput=${e => setQuery(e.currentTarget.value)} onKeyDown=${key} />
      <kbd>Esc</kbd>
    </label>
    <div class="palette-list" ref=${list}>
      ${shown.length === 0 && html`<div class="empty">Nothing matches "${query}".</div>`}
      ${shown.map((item, i) => html`<button class=${"palette-item" + (i === index ? " active" : "")}
          onMouseMove=${() => setIndex(i)} onClick=${() => close(item)}>
        <${Icon} name=${item.icon} size=${18} />
        <span class="label">${item.label}</span>
        ${item.note && html`<span class="note">${item.note}</span>`}
        <span class="group">${item.group}</span>
      </button>`)}
    </div>
  </div>`;
}
