// Cases: open CS2 containers like in the game (reel, real odds), keep what you unbox and equip it.
import { html, useState, useEffect, useRef } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, img } from "../api.js";
import { Page, Section, Button, Segmented, TextField, Icon, prompt, sheet } from "../ui.js";

const CARD = 168; // reel card width incl. gap
const SPIN_MS = 6500;
const TYPE_NAMES = {
  "Case": "Weapon Cases", "Sticker Capsule": "Sticker Capsules", "Autograph Capsule": "Autographs", "Souvenir": "Souvenirs",
  "Souvenir Highlight": "Highlights", "Music Kit Box": "Music Kits", "Pins": "Pins", "Patch Capsule": "Patches", "Graffiti": "Graffiti",
};
const wearName = w => w < 0.07 ? "Factory New" : w < 0.15 ? "Minimal Wear" : w < 0.38 ? "Field-Tested" : w < 0.45 ? "Well-Worn" : "Battle-Scarred";
const pct = c => c >= 0.01 ? `${(c * 100).toFixed(2)}%` : `${(c * 100).toFixed(2)}%`;
const equippable = i => (i.kind === "skin" && i.def) || ((i.kind === "pin" || i.kind === "music") && i.collectibleId);

// ---- sounds (Web Audio, no files) ----
let audio;
function beep(freq, ms, gain = 0.05, type = "square") {
  try {
    audio ??= new AudioContext();
    const o = audio.createOscillator(), g = audio.createGain();
    o.type = type; o.frequency.value = freq;
    g.gain.setValueAtTime(gain, audio.currentTime);
    g.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + ms / 1000);
    o.connect(g).connect(audio.destination);
    o.start(); o.stop(audio.currentTime + ms / 1000);
  } catch { /* no sound */ }
}
const tick = () => beep(2100, 22, 0.035);
function fanfare(color) {
  const rare = color === "#e4ae39" || color === "#ffd700";
  const notes = rare ? [523, 659, 784, 1047, 1319] : color === "#eb4b4b" ? [523, 659, 784, 1047] : [523, 659, 784];
  notes.forEach((n, i) => setTimeout(() => beep(n, 260, 0.06, "triangle"), i * 110));
}

function ItemPic({ src, size = 256 }) {
  const [bad, setBad] = useState(false);
  return src && !bad ? html`<img src=${img(src, size)} alt="" loading="lazy" onError=${() => setBad(true)} />` : html`<${Icon} name="skins" size=${30} />`;
}

// ---- the reel ----
function Reel({ result, onDone }) {
  const strip = useRef(), box = useRef();
  useEffect(() => {
    const width = box.current.clientWidth;
    const target = result.win * CARD + CARD * (0.12 + Math.random() * 0.76) - width / 2;
    const start = performance.now();
    let last = -1, frame;
    // Fast start, long slow end like CS2's case reel.
    const ease = t => 1 - Math.pow(1 - t, 4.2);
    const step = now => {
      const t = Math.min(1, (now - start) / SPIN_MS);
      const x = target * ease(t);
      strip.current.style.transform = `translateX(${-x}px)`;
      const under = Math.floor((x + width / 2) / CARD);
      if (under !== last) { last = under; if (t < 1) tick(); }
      if (t < 1) frame = requestAnimationFrame(step);
      else setTimeout(onDone, 450);
    };
    frame = requestAnimationFrame(step);
    return () => cancelAnimationFrame(frame);
  }, []);
  return html`<div class="reel" ref=${box}>
    <div class="reel-strip" ref=${strip}>
      ${result.reel.map((r, i) => html`<div key=${i} class=${"reel-card" + (r.rare ? " rare" : "")} style=${`--c:${r.color}`}>
        <div class="pic">${r.rare ? html`<span class="rare-star">★</span>` : html`<${ItemPic} src=${r.image} size=${256} />`}</div>
        <div class="name">${r.name}</div>
      </div>`)}
    </div>
    <div class="reel-marker"></div>
  </div>`;
}

function Reveal({ item, onAgain, onBack }) {
  useEffect(() => fanfare(item.color), []);
  const title = (item.souvenir ? "Souvenir " : "") + (item.statTrak && item.kind === "skin" ? "StatTrak™ " : "") + item.name;
  return html`<div class="reveal" style=${`--c:${item.color}`}>
    <div class="reveal-glow"></div>
    <div class="reveal-pic"><${ItemPic} src=${item.image} size=${512} /></div>
    <div class="reveal-grade">${item.grade}${item.phase ? ` · ${item.phase}` : ""}</div>
    <h2 class="reveal-name">${title}</h2>
    ${item.kind === "skin" && item.paint > 0 && html`<div class="reveal-stats mono">
      <span>${wearName(item.wear)}</span><span>Float ${item.wear.toFixed(9)}</span><span>Pattern ${item.seed}</span></div>`}
    ${item.kind === "skin" && item.paint === 0 && html`<div class="reveal-stats mono"><span>Vanilla</span><span>Pattern ${item.seed}</span></div>`}
    <div class="hstack reveal-actions">
      ${equippable(item) && html`<${Button} kind="prominent" icon="check" onClick=${() => tryCall("cases.equip", item.uid)}>Equip<//>`}
      <${Button} icon="refresh" onClick=${onAgain}>Open Another<//>
      <${Button} kind="plain" onClick=${onBack}>Back to Case<//>
    </div>
  </div>`;
}

function CaseSheet({ id, close }) {
  const [contents] = useApi("cases.contents", id);
  const [phase, setPhase] = useState("view"); // view | spin | reveal
  const [result, setResult] = useState(null);
  const open = async () => {
    try { audio ??= new AudioContext(); audio.resume?.(); } catch { /* no sound */ }
    const r = await call("cases.open", id).catch(() => null);
    if (!r) return;
    setResult(r);
    setPhase("spin");
  };
  if (!contents) return html`<div class="case-sheet"><div class="empty small">Loading…</div></div>`;
  return html`<div class="case-sheet">
    <div class="case-head">
      <div class="case-head-pic"><${ItemPic} src=${contents.image} size=${256} /></div>
      <div class="grow"><h2>${contents.name}</h2><div class="faint small">${TYPE_NAMES[contents.type] ?? contents.type}</div></div>
      <${Button} kind="plain" icon="x" onClick=${() => close(undefined)} title="Close" />
    </div>
    ${phase === "spin" && html`<${Reel} result=${result} onDone=${() => setPhase("reveal")} />`}
    ${phase === "reveal" && html`<${Reveal} item=${result.item} onAgain=${open} onBack=${() => setPhase("view")} />`}
    ${phase === "view" && html`
      <div class="case-open-row">
        <${Button} kind="prominent" size="lg" icon="key" onClick=${open}>Unlock Container<//>
        <span class="faint small">Odds as in CS2. What you unbox goes to your inventory here, not on Steam.</span>
      </div>
      <div class="case-contents scroll">
        ${contents.groups.map(g => html`<div class="grade-group">
          <div class="grade-head" style=${`--c:${g.color}`}><span class="dot"></span>${g.grade}<span class="faint">${pct(g.chance)}</span></div>
          <div class="grade-items">
            ${g.grade === "Rare Special Item"
              ? html`<div class="content-card rare" style="--c:#e4ae39"><div class="pic"><span class="rare-star">★</span></div>
                  <div class="name">${g.items.length} knives / gloves</div></div>`
              : g.items.map(i => html`<div class="content-card" style=${`--c:${i.color}`} title=${i.name}>
                  <div class="pic"><${ItemPic} src=${i.image} size=${256} /></div><div class="name">${i.name}</div></div>`)}
          </div>
        </div>`)}
      </div>`}
  </div>`;
}

function Inventory() {
  const [items, reload] = useApi("cases.inventory");
  const [q, setQ] = useState("");
  if (!items) return null;
  const shown = items.filter(i => !q || `${i.name} ${i.caseName}`.toLowerCase().includes(q.toLowerCase()));
  return html`<${Section} title="Inventory" note=${`${items.length} item${items.length === 1 ? "" : "s"}`}
      footer="Equip puts a gun skin, knife, gloves, pin or music kit on in game. Stickers: add them to a gun on the Skins page.">
    <div class="hstack inv-filter"><div class="grow"><${TextField} icon="search" placeholder="Search your inventory" value=${q} onInput=${setQ} /></div></div>
    ${shown.length === 0 ? html`<div class="empty small">${items.length ? "Nothing matches." : "Nothing unboxed yet. Open a case!"}</div>`
      : html`<div class="inv-grid">${shown.map(i => html`<div key=${i.uid} class="inv-card" style=${`--c:${i.color}`}
          title=${i.kind === "skin" && i.paint ? `${wearName(i.wear)} · ${i.wear.toFixed(9)} · pattern ${i.seed}\nFrom ${i.caseName}` : `From ${i.caseName}`}>
        <div class="pic"><${ItemPic} src=${i.image} size=${256} />${i.statTrak && html`<span class="st">ST</span>`}${i.souvenir && html`<span class="st sv">SV</span>`}</div>
        <div class="name">${i.name}</div>
        <div class="meta faint">${i.kind === "skin" && i.paint ? `${wearName(i.wear)} · ${i.wear.toFixed(4)}` : i.grade}</div>
        <div class="acts">
          ${equippable(i) && html`<${Button} size="sm" onClick=${() => tryCall("cases.equip", i.uid)}>Equip<//>`}
          <${Button} size="sm" kind="plain" icon="trash" title="Remove" onClick=${() => tryCall("cases.remove", i.uid).then(reload)} />
        </div>
      </div>`)}</div>`}
  <//>`;
}

export function CasesPage() {
  const [state] = useApi("cases.state");
  const [view, setView] = useState("cases");
  const [type, setType] = useState("Case");
  const [q, setQ] = useState("");
  if (!state) return html`<${Page} title="Cases" />`;

  const types = [...new Set(state.cases.map(c => c.type))];
  const shown = state.cases.filter(c => (types.length < 2 || c.type === type) && (!q || c.name.toLowerCase().includes(q.toLowerCase())));
  const openCase = id => sheet(close => html`<${CaseSheet} id=${id} close=${close} />`, { wide: true });
  const enterCode = async () => {
    const code = await prompt({ title: "Code", message: "Enter the code.", placeholder: "Code", ok: "OK" });
    if (code?.trim()) tryCall("cases.unlock", code.trim());
  };

  return html`<${Page} title="Cases" subtitle=${`Open cases like in CS2 and use what you unbox. ${state.opened} opened so far.`}
      actions=${html`<${Segmented} options=${[{ value: "cases", label: "Cases" }, { value: "inventory", label: `Inventory (${state.inventoryCount})` }]} value=${view} onChange=${setView} />`}>
    ${view === "inventory" ? html`<${Inventory} />` : state.dbEmpty
      ? html`<${Section}><div class="empty"><${Icon} name="download" size=${30} /><div>The case list isn't downloaded yet. Press Update on Setup › Skin, sticker and charm lists.</div></div><//>`
      : html`<div class="stack">
        <div class="hstack case-filters">
          ${types.length > 1 && html`<${Segmented} options=${types.map(t => ({ value: t, label: TYPE_NAMES[t] ?? t }))} value=${type} onChange=${setType} />`}
          <div class="grow"><${TextField} icon="search" placeholder="Search cases" value=${q} onInput=${setQ} /></div>
        </div>
        <div class="case-grid">
          ${shown.map(c => html`<button key=${c.id} class="case-tile glass" onClick=${() => openCase(c.id)} title=${c.name}>
            <div class="pic"><${ItemPic} src=${c.image} size=${256} /></div>
            <div class="name">${c.name}</div>
            <div class="faint small">${c.count} items${c.rare ? " · ★ knives / gloves" : ""}</div>
          </button>`)}
        </div>
        ${!state.unlocked && html`<div class="case-code faint small"><a href="#" onClick=${e => { e.preventDefault(); enterCode(); }}>Have a code?</a></div>`}
      </div>`}
  <//>`;
}
