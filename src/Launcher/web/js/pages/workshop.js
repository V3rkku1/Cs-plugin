// Workshop: maps Steam has downloaded for CS2 (click Host to play one), and a search of the Steam Workshop.
import { html, useState, useEffect } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, img } from "../api.js";
import { Page, Section, Button, Segmented, Select, TextField, Icon } from "../ui.js";

function MapCard({ item, hosting, state }) {
  const live = hosting === item.id;
  return html`<div class=${"ws-card glass" + (live ? " live" : "")}>
    <div class="ws-pic">${item.preview ? html`<img src=${img(item.preview, 512)} alt="" loading="lazy" />` : html`<${Icon} name="map" size=${34} />`}
      ${!item.cs2 && html`<span class="ws-badge warn" title="Made for CS:GO: it may not load in CS2">CS:GO</span>`}
      ${live && html`<span class="ws-badge good">Hosting</span>`}</div>
    <div class="ws-body">
      <div class="ws-title" title=${item.title}>${item.title}</div>
      <div class="ws-meta faint small">${[item.size, item.updated, item.tags].filter(Boolean).join(" · ")}</div>
      ${item.downloaded && state && html`<${Select} width=${170} value=${state.mapModes[item.id] ?? ""} onChange=${v => tryCall("workshop.setMode", { id: item.id, mode: v })}
        options=${[{ value: "", label: "Mode: as set" }, ...state.modes.map(m => ({ value: m, label: m }))]} />`}
      <div class="ws-actions">
        ${item.downloaded
          ? html`<${Button} size="sm" kind="prominent" icon="play" onClick=${() => tryCall("workshop.host", item.id)}>${live ? "Restart" : "Host"}<//>`
          : html`<${Button} size="sm" icon="download" onClick=${() => tryCall("workshop.subscribe", item.id)}>Subscribe<//>`}
        <${Button} size="sm" kind="plain" onClick=${() => tryCall("workshop.subscribe", item.id)}>Steam page<//>
      </div>
    </div>
  </div>`;
}

export function WorkshopPage() {
  const [state, reload] = useApi("workshop.state");
  const [text, setText] = useState("");
  const [sort, setSort] = useState("popular");
  const [results, setResults] = useState(null);
  const [busy, setBusy] = useState(false);

  const search = async (q = text, s = sort) => {
    setBusy(true);
    const found = await call("workshop.search", { text: q, sort: s, page: 1 }).catch(() => null);
    setBusy(false);
    if (found) setResults(found);
  };
  useEffect(() => { search("", "popular"); }, []);
  // Steam downloads in the background after Subscribe: look again when the window gets focus.
  useEffect(() => { window.addEventListener("focus", reload); return () => window.removeEventListener("focus", reload); }, []);

  return html`<${Page} title="Workshop" subtitle="Your downloaded maps, and new ones from the Steam Workshop. Host starts the server on the map."
      actions=${html`<${Button} icon="refresh" onClick=${reload}>Refresh<//>`}>
    <div class="stack">
    <${Section} title="Downloaded" note=${state ? `${state.downloaded.length} map${state.downloaded.length === 1 ? "" : "s"}` : ""}
        footer="Subscribe to a map in Steam: it downloads by itself and shows up here. Unsubscribe in Steam to remove it.">
      ${!state ? html`<div class="empty small">Loading…</div>`
        : !state.hasGame ? html`<div class="empty small">Pick your CS2 folder on Setup first.</div>`
        : state.downloaded.length === 0 ? html`<div class="empty small"><${Icon} name="map" size=${28} /><div>No Workshop maps downloaded yet. Find one below and press Subscribe.</div></div>`
        : html`<div class="ws-grid">${state.downloaded.map(i => html`<${MapCard} key=${i.id} item=${i} hosting=${state.hosting} state=${state} />`)}</div>`}
    <//>
    <${Section} title="Find Maps" footer="Results come from the Steam Workshop. Maps marked CS:GO were made for the old game and may not load.">
      <div class="hstack ws-search">
        <div class="grow"><${TextField} icon="search" placeholder="Search maps: aim, surf, 1v1, awp…" value=${text} onInput=${setText} onEnter=${() => search()} /></div>
        <${Segmented} options=${[{ value: "popular", label: "Popular" }, { value: "new", label: "New" }, { value: "top", label: "Top Rated" }]}
          value=${sort} onChange=${v => { setSort(v); search(text, v); }} />
        <${Button} kind="prominent" icon="search" busy=${busy} onClick=${() => search()}>Search<//>
      </div>
      ${results == null ? html`<div class="empty small">${busy ? "Searching…" : ""}</div>`
        : results.length === 0 ? html`<div class="empty small">Nothing found.</div>`
        : html`<div class="ws-grid">${results.map(i => html`<${MapCard} key=${i.id} item=${i} hosting=${state?.hosting} state=${state} />`)}</div>`}
    <//>
    </div>
  <//>`;
}
