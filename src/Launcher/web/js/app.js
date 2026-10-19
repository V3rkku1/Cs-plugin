// The window: floating glass sidebar, the pages, toasts and keyboard shortcuts.
import { html, render, useState, useEffect, useRef, useLayoutEffect } from "./vendor/preact-htm.js";
import { tryCall, useApi, prefetch } from "./api.js";
import { Icon, Toasts, sheen } from "./ui.js";
import { openPalette } from "./palette.js";
import { UpdatePopup, openUpdate } from "./update.js";
import { applyTheme, watchCustomCss } from "./theme.js";
import { CsgoPlayPage } from "./pages/csgo.js";
import { CasesPage } from "./pages/cases.js";
import { InventoryPage } from "./pages/inventory.js";
import { SetupPage } from "./pages/setup.js";

const pages = [
  { id: "play", label: "Play", icon: "play", component: CsgoPlayPage },
  { id: "inventory", label: "Inventory", icon: "skins", component: InventoryPage },
  { id: "cases", label: "Cases", icon: "key", component: CasesPage },
  { id: "setup", label: "Setup", icon: "setup", component: SetupPage },
];

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
        <div class="version">CS:GO · ${state?.version ?? ""}</div>
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
        <${Icon} name=${p.icon} size=${20} /> ${p.label} <kbd>Ctrl ${i + 1}</kbd>
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

function App() {
  const [state] = useApi("app.state");
  const [page, setPageState] = useState(null);
  const [settings] = useApi("setup.state");

  useEffect(() => { if (page == null && state) setPageState(state.page); }, [state]);

  // Every page's data is ready before you click it: no empty page while it loads.
  useEffect(() => prefetch(["legacy.state", "cases.inventory"]), []);

  const setPage = id => {
    setPageState(id);
    tryCall("app.page", id);
  };

  useEffect(() => {
    const go = e => setPage(e.detail);
    window.addEventListener("navigate", go);
    const keys = e => {
      if (e.ctrlKey && e.key.toLowerCase() === "k") { openPalette(setPage); e.preventDefault(); }
      else if (e.ctrlKey && e.key >= "1" && e.key <= String(pages.length)) { setPage(pages[Number(e.key) - 1].id); e.preventDefault(); }
      else if (e.ctrlKey && e.key.toLowerCase() === "f") { setPage("inventory"); setTimeout(() => window.dispatchEvent(new Event("focus-search")), 50); e.preventDefault(); }
      else if (e.key === "F11") { tryCall("app.fullscreen"); e.preventDefault(); }
    };
    window.addEventListener("keydown", keys);
    return () => { window.removeEventListener("navigate", go); window.removeEventListener("keydown", keys); };
  }, []);

  // Appearance from the Setup page.
  useEffect(() => {
    if (!settings) return;
    document.documentElement.classList.toggle("large-text", settings.textSize === "large");
    document.documentElement.classList.toggle("small-text", settings.textSize === "small");
    document.documentElement.classList.toggle("reduce-glass", settings.reduceGlass);
    document.documentElement.classList.toggle("reduce-motion", settings.reduceMotion);
  }, [settings?.textSize, settings?.reduceGlass, settings?.reduceMotion]);

  // Theme colours, corners, font and background picture from Setup › Appearance.
  useEffect(() => { if (settings) applyTheme(settings.theme, settings.reduceGlass); }, [JSON.stringify(settings?.theme ?? null), settings?.reduceGlass]);
  useEffect(() => watchCustomCss(), []);

  // The sheen on glass buttons and tiles follows the pointer.
  useEffect(() => {
    const move = e => { const el = e.target.closest?.(".sheen"); if (el) sheen({ currentTarget: el, clientX: e.clientX, clientY: e.clientY }); };
    window.addEventListener("pointermove", move);
    return () => window.removeEventListener("pointermove", move);
  }, []);

  // The old Skins page was replaced by the Inventory ("skins" may still be the saved last page).
  const Current = pages.find(p => p.id === (page === "skins" ? "inventory" : page))?.component;
  return html`<div class="app">
    <${Sidebar} page=${page} setPage=${setPage} state=${state} />
    <main class="main">${Current && html`<${Current} key=${page} />`}</main>
    <${UpdatePopup} state=${state} />
    <${Toasts} />
  </div>`;
}

render(html`<${App} />`, document.getElementById("app"));
