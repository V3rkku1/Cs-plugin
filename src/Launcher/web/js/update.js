// Launcher updates: the "what's new" popup that shows up while the launcher is open, and its details sheet.
import { html, useState, useEffect } from "./vendor/preact-htm.js";
import { tryCall } from "./api.js";
import { Icon, Button, sheet } from "./ui.js";

const DISMISSED = "lst-update-dismissed";
const dismissed = () => { try { return localStorage.getItem(DISMISSED); } catch { return null; } };
const dismiss = commit => { try { localStorage.setItem(DISMISSED, commit); } catch { /* private window */ } };

function Notes({ notes }) {
  if (!notes?.length) return html`<p class="update-none">Fixes and improvements.</p>`;
  return html`<ul class="update-notes">${notes.map(n => html`<li>${n}</li>`)}</ul>`;
}

/** The full list and the install button (sidebar card, Setup › Launcher). */
export function openUpdate(state) {
  sheet(close => html`<div class="update-sheet">
    <div class="update-head">
      <div class="update-badge"><${Icon} name="download" size=${24} stroke=${2} /></div>
      <div>
        <h2>Launcher update</h2>
        <div class="muted">${state.version} → build ${state.updateCommit}</div>
      </div>
    </div>
    <h3>What's new</h3>
    <${Notes} notes=${state.updateNotes} />
    <p class="muted small">The launcher closes, swaps in the new version and opens again (a few seconds). Your loadout, inventory and settings stay.</p>
    <div class="buttons">
      <${Button} onClick=${() => close()}>Later<//>
      <${Button} kind="prominent" icon="download" onClick=${() => { close(); tryCall("app.update"); }}>Update Now<//>
    </div>
  </div>`);
}

/** Pops up once per new build while the launcher is open; "Later" hides it until the next build. */
export function UpdatePopup({ state }) {
  const [hidden, setHidden] = useState(null);
  const commit = state?.updateCommit;
  useEffect(() => { setHidden(dismissed()); }, [commit]);
  if (!state?.updateAvailable || !commit || state.updateProgress || hidden === commit) return null;
  const notes = state.updateNotes ?? [];
  const later = () => { dismiss(commit); setHidden(commit); };
  return html`<div class="update-popup" role="dialog" aria-label="Launcher update">
    <div class="update-head">
      <div class="update-badge"><${Icon} name="download" size=${20} stroke=${2} /></div>
      <div>
        <div class="title">New launcher version</div>
        <div class="muted small">build ${commit} · ${notes.length ? `${notes.length} change${notes.length === 1 ? "" : "s"}` : "fixes and improvements"}</div>
      </div>
    </div>
    ${notes.length > 0 && html`<ul class="update-notes short">${notes.slice(0, 4).map(n => html`<li>${n}</li>`)}</ul>`}
    ${notes.length > 4 && html`<button class="link-button" onClick=${() => openUpdate(state)}>See all ${notes.length} changes</button>`}
    <div class="update-actions">
      <${Button} size="sm" onClick=${later}>Later<//>
      <${Button} size="sm" kind="prominent" icon="download" onClick=${() => tryCall("app.update")}>Update Now<//>
    </div>
  </div>`;
}
