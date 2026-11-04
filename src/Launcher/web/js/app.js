// The window: floating glass sidebar, the pages, toasts and keyboard shortcuts.
import { html, render, useState, useEffect, useRef, useLayoutEffect } from "./vendor/preact-htm.js";
import { tryCall, useApi, prefetch } from "./api.js";
import { Icon, Toasts, sheen } from "./ui.js";
import { openPalette } from "./palette.js";
import { UpdatePopup, openUpdate } from "./update.js";
import { applyTheme, watchCustomCss } from "./theme.js";
import { PlayPage } from "./pages/play.js";

// Only Play loads with the window; the other pages load on first open (and are warmed when the launcher is idle).
const lazy = (load, name) => { let C = null; const get = () => load().then(m => (C = m[name])); return { get, component: props => C ? html`<${C} ...${props} />` : null, ready: () => C }; };
const LAZY = {
  live: lazy(() => import("./pages/live.js"), "LivePage"),
  inventory: lazy(() => import("./pages/inventory.js"), "InventoryPage"),
  cases: lazy(() => import("./pages/cases.js"), "CasesPage"),
  trainer: lazy(() => import("./pages/trainer.js"), "TrainerPage"),
  stats: lazy(() => import("./pages/stats.js"), "StatsPage"),
  workshop: lazy(() => import("./pages/workshop.js"), "WorkshopPage"),
  friends: lazy(() => import("./pages/friends.js"), "FriendsPage"),
  setup: lazy(() => import("./pages/setup.js"), "SetupPage"),
};

/** Last page and look, kept in the browser so the first frame already shows them (app.state fixes them up). */
const local = {
  get: k => { try { return JSON.parse(localStorage.getItem(k)); } catch { return null; } },
  set: (k, v) => { try { localStorage.setItem(k, JSON.stringify(v)); } catch { /* no storage */ } },
};

const pages = [
  { id: "play", label: "Play", icon: "play" },
  { id: "live", label: "Live Server", icon: "server" },
  { id: "inventory", label: "Inventory", icon: "skins" },
  { id: "cases", label: "Cases", icon: "key" },
  { id: "trainer", label: "Trainer", icon: "target" },
  { id: "stats", label: "Stats", icon: "sliders" },
  { id: "workshop", label: "Workshop", icon: "map" },
  { id: "friends", label: "Friends", icon: "friends" },
  { id: "setup", label: "Setup", icon: "setup" },
];

// The first-start tour: what to press first, one step at a time (again from Setup › Show the Tour).
const TOUR = [
  ["Welcome!", "Try CS2 skins, knives and gloves on your own private server, play with bots and friends, and train. Here's the quick way in.", null],
  ["1 · Setup", "Press Install Everything once. It adds what the server needs to your CS2 folder (your normal CS2 isn't changed).", "setup"],
  ["2 · Play", "Pick a quick start. The server starts in its own window and CS2 joins by itself. Stop puts CS2 back to normal.", "play"],
  ["3 · Cases & Inventory", "Open cases for free, then Equip what you unboxed in the Inventory. It shows in game right away.", "cases"],
  ["4 · Trainer & Stats", "Learn lineups, run drills and aim training. Stats shows if you're getting better.", "trainer"],
];

function Tour({ setPage, ready }) {
  const [step, setStep] = useState(() => { try { return localStorage.getItem("tour.done") ? -1 : 0; } catch { return 0; } });
  // Already set up (an existing user after an update): no tour, unless asked for in Setup.
  const auto = useRef(true);
  useEffect(() => { if (ready && auto.current && step === 0) { auto.current = false; setStep(-1); try { localStorage.setItem("tour.done", "1"); } catch { /* no storage */ } } }, [ready]);
  useEffect(() => {
    const show = () => { auto.current = false; setStep(0); };
    window.addEventListener("show-tour", show);
    return () => window.removeEventListener("show-tour", show);
  }, []);
  if (step < 0) return null;
  const done = () => { setStep(-1); try { localStorage.setItem("tour.done", "1"); } catch { /* no storage */ } };
  const [title, text, page] = TOUR[step];
  const last = step === TOUR.length - 1;
  return html`<div class="glass liquid" role="dialog" aria-label="Tour"
      style="position:fixed;right:24px;bottom:24px;width:360px;z-index:50;padding:18px;border-radius:16px;box-shadow:var(--shadow)">
    <div class="small faint">${step + 1} / ${TOUR.length}</div>
    <h3 style="margin:4px 0 6px">${title}</h3>
    <div style="margin-bottom:14px">${text}</div>
    <div class="hstack" style="gap:8px">
      <button class="btn plain sm" onClick=${done}>Skip</button>
      <div class="grow"></div>
      ${step > 0 && html`<button class="btn sm" onClick=${() => setStep(step - 1)}>Back</button>`}
      ${page && html`<button class="btn sm" onClick=${() => setPage(page)}>Show me</button>`}
      <button class="btn prominent sm" onClick=${() => { if (last) done(); else { setStep(step + 1); if (TOUR[step + 1][2]) setPage(TOUR[step + 1][2]); } }}>${last ? "Done" : "Next"}</button>
    </div>
  </div>`;
}

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
        ${state && !state.updateAvailable && html`<button class=${"check-update" + (state.checkingUpdate ? " busy" : "")}
            disabled=${state.checkingUpdate} onClick=${() => tryCall("app.checkUpdate")}
            title=${state.lastUpdateCheck ? `Last checked ${state.lastUpdateCheck}` : "Look for a newer launcher"}>
          <${Icon} name="refresh" size=${12} stroke=${2.2} /> ${state.checkingUpdate ? "Checking…" : "Check for updates"}
        </button>`}
      </div>
    </div>
    <button class="search-button" onClick=${() => openPalette(setPage)}>
      <${Icon} name="search" size=${16} /> Search <kbd>Ctrl K</kbd>
    </button>
    <nav class="nav" ref=${nav}>
      <div class="nav-indicator" style=${`transform: translateY(${y}px)`}></div>
      ${pages.map((p, i) => html`<button class=${"nav-item" + (p.id === page ? " active" : "")} onClick=${() => setPage(p.id)}>
        <${Icon} name=${p.icon} size=${20} /> ${p.label} ${i < 9 && html`<kbd>Ctrl ${i + 1}</kbd>`}
      </button>`)}
    </nav>
    <div class="spacer"></div>
    ${state?.updateAvailable && html`<button class="update-card" onClick=${() => state.updateProgress || openUpdate(state)}>
      <${Icon} name="download" size=${22} stroke=${2} />
      <div><div class="title">${state.updateProgress ? "Updating…" : "Update available"}</div>
      <div class="detail">${state.updateProgress ?? `Build ${state.updateCommit ?? ""} · see what's new`}</div></div>
    </button>`}
    ${state && html`<div class="status-card" style=${`--c:${statusColors[state.statusKind]}`}>
      <span class="dot"></span>
      <div><div class="title">${state.statusTitle}</div><div class="detail">${state.statusDetail}</div></div>
    </div>`}
  </aside>`;
}

/** Look from Setup › Appearance: classes, theme colours, corners, font, background picture. */
function applyAppearance(a) {
  const root = document.documentElement.classList;
  root.toggle("large-text", a.textSize === "large");
  root.toggle("small-text", a.textSize === "small");
  root.toggle("reduce-glass", !!a.reduceGlass);
  root.toggle("reduce-motion", !!a.reduceMotion);
  applyTheme(a.theme ?? {}, a.reduceGlass);
}

// Its own component: Setup's state changing re-renders this, not the page.
function Appearance() {
  const [settings] = useApi("setup.state");
  const look = settings && { textSize: settings.textSize, reduceGlass: settings.reduceGlass, reduceMotion: settings.reduceMotion, theme: settings.theme };
  const key = JSON.stringify(look);
  useEffect(() => { if (look) { applyAppearance(look); local.set("appearance", look); } }, [key]);
  return null;
}

// The parts that follow app.state live in their own components: a new state re-renders them, not the page.
// (Both share one app.state load through useApi.)
function Nav({ page, setPage, initPage }) {
  const [state] = useApi("app.state");
  useEffect(() => { if (state) initPage(state.page); }, [state?.page]);
  return html`<${Sidebar} page=${page} setPage=${setPage} state=${state} />`;
}

function Overlays({ setPage }) {
  const [state] = useApi("app.state");
  return html`<${UpdatePopup} state=${state} /><${Tour} setPage=${setPage} ready=${state?.ready} />`;
}

function App() {
  // The old Skins page was replaced by the Inventory ("skins" may still be the saved last page); a removed page
  // (Hacker Mode) opens Play.
  const known = id => (id === "skins" ? "inventory" : pages.some(p => p.id === id) ? id : "play");
  const [page, setPageState] = useState(() => { const p = local.get("page"); return p ? known(p) : null; });
  // Bumped when a lazy page's code arrives, so it renders.
  const [, loaded] = useState(0);

  // Every page's data is ready before you click it: no empty page while it loads. Page code too, once idle.
  useEffect(() => {
    prefetch(["play.state", "cases.inventory", "friends.state"]);
    const idle = window.requestIdleCallback ?? (f => setTimeout(f, 1500));
    idle(() => Object.values(LAZY).forEach(l => l.get().catch(() => undefined)));
  }, []);

  const setPage = id => {
    setPageState(id);
    local.set("page", id);
    tryCall("app.page", id);
  };
  // No page until app.state (or the saved one) says which: otherwise Play mounts (and loads) first, then gets swapped.
  const initPage = id => setPageState(p => { if (p != null) return p; const k = known(id); local.set("page", k); return k; });

  useEffect(() => {
    const go = e => setPage(e.detail);
    window.addEventListener("navigate", go);
    const keys = e => {
      if (e.ctrlKey && e.key.toLowerCase() === "k") { openPalette(setPage); e.preventDefault(); }
      else if (e.ctrlKey && e.key >= "1" && e.key <= "9") { setPage(pages[Number(e.key) - 1].id); e.preventDefault(); }
      else if (e.ctrlKey && e.key.toLowerCase() === "f") { setPage("inventory"); setTimeout(() => window.dispatchEvent(new Event("focus-search")), 50); e.preventDefault(); }
      else if (e.key === "F11") { tryCall("app.fullscreen"); e.preventDefault(); }
      else if (e.key === "F5" && !e.shiftKey) { tryCall("play.startOrApply"); e.preventDefault(); }
      else if (e.key === "F5" && e.shiftKey) { tryCall("play.stop"); e.preventDefault(); }
      // The browser's own shortcuts (reload, print, find) don't belong in the launcher.
      else if (e.ctrlKey && "rpgu".includes(e.key.toLowerCase())) e.preventDefault();
    };
    window.addEventListener("keydown", keys);
    return () => { window.removeEventListener("navigate", go); window.removeEventListener("keydown", keys); };
  }, []);

  useEffect(() => watchCustomCss(), []);

  // The sheen on glass buttons and tiles follows the pointer.
  useEffect(() => {
    const move = e => { const el = e.target.closest?.(".sheen"); if (el) sheen({ currentTarget: el, clientX: e.clientX, clientY: e.clientY }); };
    window.addEventListener("pointermove", move);
    return () => window.removeEventListener("pointermove", move);
  }, []);

  const lazyPage = page != null && page !== "play" && LAZY[page];
  useEffect(() => { if (lazyPage && !lazyPage.ready()) lazyPage.get().then(() => loaded(n => n + 1), () => undefined); }, [page]);
  const Current = page == null ? null : lazyPage ? lazyPage.component : PlayPage;
  return html`<div class="app">
    <${Nav} page=${page} setPage=${setPage} initPage=${initPage} />
    <main class="main">${Current && html`<${Current} key=${page} />`}</main>
    <${Overlays} setPage=${setPage} />
    <${Appearance} />
    <${Toasts} />
  </div>`;
}

window.addEventListener("contextmenu", e => e.preventDefault());
// The saved look before the first render: no flash of the default theme.
{ const look = local.get("appearance"); if (look) applyAppearance(look); }
render(html`<${App} />`, document.getElementById("app"));
