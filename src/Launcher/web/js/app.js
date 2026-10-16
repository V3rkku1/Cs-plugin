// The window: floating glass sidebar, the pages, toasts and keyboard shortcuts.
import { html, render, useState, useEffect, useRef, useLayoutEffect } from "./vendor/preact-htm.js";
import { call, tryCall, useApi, on } from "./api.js";
import { Icon, Toasts, sheen } from "./ui.js";
import { PlayPage } from "./pages/play.js";
import { LivePage } from "./pages/live.js";
import { SkinsPage } from "./pages/skins.js";
import { HackerPage } from "./pages/hacker.js";
import { FriendsPage } from "./pages/friends.js";
import { SetupPage } from "./pages/setup.js";

const pages = [
  { id: "play", label: "Play", icon: "play", component: PlayPage },
  { id: "live", label: "Live Server", icon: "server", component: LivePage },
  { id: "skins", label: "Skins", icon: "skins", component: SkinsPage },
  { id: "hacker", label: "Hacker Mode", icon: "hacker", component: HackerPage },
  { id: "friends", label: "Friends", icon: "friends", component: FriendsPage },
  { id: "setup", label: "Setup", icon: "setup", component: SetupPage },
];

/** Other pages can switch pages: navigate("live"). */
export const navigate = id => window.dispatchEvent(new CustomEvent("navigate", { detail: id }));

const statusColors = { good: "var(--green)", warning: "var(--orange)", bad: "var(--red)" };

function Sidebar({ page, setPage, state }) {
  const nav = useRef();
  const [y, setY] = useState(0);
  useLayoutEffect(() => {
    const active = nav.current?.querySelector(".nav-item.active");
    if (active) setY(active.offsetTop);
  }, [page]);
  return html`<aside class="sidebar glass liquid">
    <div class="brand">
      <img src="app.png" alt="" />
      <div>
        <div class="name">Skin Tester</div>
        <div class="version">CS2 · ${state?.version ?? ""}</div>
      </div>
    </div>
    <nav class="nav" ref=${nav}>
      <div class="nav-indicator" style=${`transform: translateY(${y}px)`}></div>
      ${pages.map((p, i) => html`<button class=${"nav-item" + (p.id === page ? " active" : "")} onClick=${() => setPage(p.id)}>
        <${Icon} name=${p.icon} size=${20} /> ${p.label} <kbd>Ctrl ${i + 1}</kbd>
      </button>`)}
    </nav>
    <div class="spacer"></div>
    ${state?.updateAvailable && html`<button class="update-card" onClick=${() => tryCall("app.update")}>
      <${Icon} name="download" size=${22} stroke=${2} />
      <div><div class="title">${state.updateProgress ? "Updating…" : "Update available"}</div>
      <div class="detail">${state.updateProgress ?? "Click to install"}</div></div>
    </button>`}
    ${state && html`<div class="status-card" style=${`--c:${statusColors[state.statusKind]}`}>
      <span class="dot"></span>
      <div><div class="title">${state.statusTitle}</div><div class="detail">${state.statusDetail}</div></div>
    </div>`}
  </aside>`;
}

function App() {
  const [state] = useApi("app.state");
  const [page, setPageState] = useState(null);
  const [settings] = useApi("setup.state");

  useEffect(() => { if (page == null && state) setPageState(state.page); }, [state]);

  const setPage = id => {
    setPageState(id);
    tryCall("app.page", id);
  };

  useEffect(() => {
    const go = e => setPage(e.detail);
    window.addEventListener("navigate", go);
    const keys = e => {
      if (e.ctrlKey && e.key >= "1" && e.key <= "6") { setPage(pages[Number(e.key) - 1].id); e.preventDefault(); }
      else if (e.ctrlKey && e.key.toLowerCase() === "f") { setPage("skins"); setTimeout(() => window.dispatchEvent(new Event("focus-search")), 50); e.preventDefault(); }
      else if (e.key === "F5" && !e.shiftKey) { tryCall("play.startOrApply"); e.preventDefault(); }
      else if (e.key === "F5" && e.shiftKey) { tryCall("play.stop"); e.preventDefault(); }
    };
    window.addEventListener("keydown", keys);
    return () => { window.removeEventListener("navigate", go); window.removeEventListener("keydown", keys); };
  }, []);

  // Appearance from the Setup page.
  useEffect(() => {
    if (!settings) return;
    document.documentElement.classList.toggle("large-text", settings.textSize === "large");
    document.documentElement.classList.toggle("reduce-glass", settings.reduceGlass);
    document.documentElement.classList.toggle("reduce-motion", settings.reduceMotion);
  }, [settings?.textSize, settings?.reduceGlass, settings?.reduceMotion]);

  // The sheen on glass buttons and tiles follows the pointer.
  useEffect(() => {
    const move = e => { const el = e.target.closest?.(".sheen"); if (el) sheen({ currentTarget: el, clientX: e.clientX, clientY: e.clientY }); };
    window.addEventListener("pointermove", move);
    return () => window.removeEventListener("pointermove", move);
  }, []);

  const Current = pages.find(p => p.id === page)?.component;
  return html`<div class="app">
    <${Sidebar} page=${page} setPage=${setPage} state=${state} />
    <main class="main">${Current && html`<${Current} key=${page} />`}</main>
    <${Toasts} />
  </div>`;
}

render(html`<${App} />`, document.getElementById("app"));
