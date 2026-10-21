// Cases: open CS2 containers like in the game (reel, real odds), keep what you unbox and equip it.
import { html, useState, useEffect, useRef } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, navigate } from "../api.js";
import { Page, Section, Button, Segmented, Select, TextField, Icon, ItemPic, prompt, sheet } from "../ui.js";
import { money, wearName, WearBar } from "./inventory.js";

const CARD = 168; // reel card width incl. gap
const SPIN_MS = 7200;
const TYPE_NAMES = {
  "Case": "Weapon Cases", "Sticker Capsule": "Sticker Capsules", "Autograph Capsule": "Autographs", "Souvenir": "Souvenirs",
  "Souvenir Highlight": "Highlights", "Music Kit Box": "Music Kits", "Pins": "Pins", "Patch Capsule": "Patches", "Graffiti": "Graffiti", "Terminal": "Terminals",
};
const pct = c => `${(c * 100).toFixed(2)}%`;
const equippable = i => (i.kind === "skin" && i.def) || ((i.kind === "pin" || i.kind === "music") && i.collectibleId);

// ---- sounds: synthesized with Web Audio (no game files), shaped after CS2's case sounds ----
let audio, master;
let muted = (() => { try { return localStorage.getItem("cases.muted") === "1"; } catch { return false; } })();
export const isMuted = () => muted;
export function setMuted(v) { muted = v; try { localStorage.setItem("cases.muted", v ? "1" : "0"); } catch { /* no storage */ } }
function ctx() {
  if (!audio) {
    audio = new AudioContext();
    master = audio.createGain();
    master.gain.value = 0.9;
    // A little room so it doesn't sound dry.
    const comp = audio.createDynamicsCompressor();
    master.connect(comp).connect(audio.destination);
  }
  audio.resume?.();
  return audio;
}
let noiseBuffer;
function noise(a) {
  if (!noiseBuffer) {
    noiseBuffer = a.createBuffer(1, a.sampleRate * 0.5, a.sampleRate);
    const d = noiseBuffer.getChannelData(0);
    for (let i = 0; i < d.length; i++) d[i] = Math.random() * 2 - 1;
  }
  const n = a.createBufferSource();
  n.buffer = noiseBuffer;
  return n;
}
/** A short filtered noise hit: clicks, ticks, metal. */
function hit(at, { freq = 3200, q = 6, gain = 0.25, decay = 0.03, type = "bandpass" } = {}) {
  const a = ctx(), n = noise(a), f = a.createBiquadFilter(), g = a.createGain();
  f.type = type; f.frequency.value = freq; f.Q.value = q;
  g.gain.setValueAtTime(gain, at);
  g.gain.exponentialRampToValueAtTime(0.0001, at + decay);
  n.connect(f).connect(g).connect(master);
  n.start(at, Math.random() * 0.3); n.stop(at + decay + 0.02);
}
/** A bell-like tone: a few partials that ring out. */
function bell(at, freq, { gain = 0.12, decay = 1.2 } = {}) {
  const a = ctx();
  [[1, 1], [2.01, 0.45], [3.02, 0.22], [4.5, 0.08]].forEach(([mult, amp]) => {
    const o = a.createOscillator(), g = a.createGain();
    o.type = "sine"; o.frequency.value = freq * mult;
    g.gain.setValueAtTime(0.0001, at);
    g.gain.exponentialRampToValueAtTime(gain * amp, at + 0.008);
    g.gain.exponentialRampToValueAtTime(0.0001, at + decay / mult);
    o.connect(g).connect(master);
    o.start(at); o.stop(at + decay + 0.05);
  });
}
/** A low thump for big drops. */
function thump(at, gain = 0.5) {
  const a = ctx(), o = a.createOscillator(), g = a.createGain();
  o.type = "sine"; o.frequency.setValueAtTime(120, at); o.frequency.exponentialRampToValueAtTime(40, at + 0.35);
  g.gain.setValueAtTime(gain, at); g.gain.exponentialRampToValueAtTime(0.0001, at + 0.45);
  o.connect(g).connect(master); o.start(at); o.stop(at + 0.5);
}
/** 1 (blue and below) to 5 (gold) by rarity colour. */
const gradeLevel = color => ({ "#e4ae39": 5, "#ffd700": 5, "#eb4b4b": 4, "#d32ce6": 3, "#8847ff": 2 })[color] ?? 1;
const sound = {
  /** The reel passing an item: a crisp plastic tick, a little different every time. */
  tick() {
    if (muted) return;
    const a = ctx(), t = a.currentTime;
    hit(t, { freq: 3800 + Math.random() * 700, q: 9, gain: 0.32, decay: 0.022 });
    hit(t, { freq: 900, q: 2, gain: 0.08, decay: 0.03, type: "lowpass" });
  },
  /** Turning the key: two metal clicks, a clunk and the lid. */
  unlock() {
    if (muted) return;
    const a = ctx(), t = a.currentTime;
    hit(t, { freq: 5200, q: 12, gain: 0.25, decay: 0.03 });
    hit(t + 0.09, { freq: 4300, q: 12, gain: 0.22, decay: 0.035 });
    hit(t + 0.22, { freq: 600, q: 1.5, gain: 0.35, decay: 0.12, type: "lowpass" });
    thump(t + 0.22, 0.25);
    hit(t + 0.32, { freq: 2000, q: 0.7, gain: 0.08, decay: 0.45 });
  },
  /** The drop, by grade: one soft tone for blue, up to a full chord with sparkle for gold. */
  reveal(color) {
    if (muted) return;
    const a = ctx(), t = a.currentTime + 0.02;
    const level = gradeLevel(color);
    const chords = [[523.3], [523.3, 659.3], [523.3, 659.3, 784], [392, 523.3, 659.3, 784, 1046.5], [392, 523.3, 659.3, 784, 1046.5, 1318.5]];
    if (level >= 4) thump(t, level === 5 ? 0.6 : 0.45);
    chords[level - 1].forEach((f, i) => bell(t + i * (level >= 4 ? 0.07 : 0.11), f, { gain: 0.1 + level * 0.015, decay: 1.2 + level * 0.3 }));
    if (level === 5) for (let i = 0; i < 14; i++) hit(t + 0.25 + i * 0.06, { freq: 6000 + Math.random() * 3000, q: 15, gain: 0.06, decay: 0.08 });
  },
};

// ---- the reel ----
function Reel({ result, onDone, skip }) {
  const strip = useRef(), box = useRef();
  const [landed, setLanded] = useState(false);
  useEffect(() => {
    const width = box.current.clientWidth;
    // Stop somewhere on the winning card, not always the middle (like the game).
    const target = result.win * CARD + CARD * (0.08 + Math.random() * 0.84) - width / 2;
    const start = performance.now();
    // Every spin takes a slightly different time, like the game.
    const spinMs = SPIN_MS * (0.94 + Math.random() * 0.12);
    let last = -1, lastX = 0, frame;
    // Quick start, long slow end: CS2's reel slows down over its last few seconds.
    const ease = t => 1 - Math.pow(1 - t, 4.6);
    const step = now => {
      const t = skip.current ? 1 : Math.min(1, (now - start) / spinMs);
      const x = target * ease(t);
      strip.current.style.transform = `translate3d(${-x}px,0,0)`;
      // Motion blur while it's fast.
      const speed = x - lastX;
      lastX = x;
      strip.current.style.filter = speed > 18 ? `blur(${Math.min(3, speed / 22).toFixed(1)}px)` : "none";
      const under = Math.floor((x + width / 2) / CARD);
      if (under !== last) { last = under; if (t < 1) sound.tick(); }
      if (t < 1) frame = requestAnimationFrame(step);
      else { setLanded(true); setTimeout(onDone, skip.current ? 250 : 1100); }
    };
    frame = requestAnimationFrame(step);
    return () => cancelAnimationFrame(frame);
  }, []);
  return html`<div class=${"reel" + (landed ? " landed" : "")} ref=${box} onClick=${() => { skip.current = true; }} title="Click to skip">
    <div class="reel-strip" ref=${strip}>
      ${result.reel.map((r, i) => html`<div key=${i} class=${"reel-card" + (r.rare ? " rare" : "") + (landed && i === result.win ? " winner" : "")} style=${`--c:${r.color}`}>
        <div class="pic">${r.rare && !r.image ? html`<span class="rare-star">★</span>` : html`<${ItemPic} src=${r.image} size=${256} />`}</div>
        <div class="name">${r.rare ? "Rare Special Item" : splitName(r.name)[1] || r.name}</div>
      </div>`)}
    </div>
    <div class="reel-marker"></div>
  </div>`;
}

function Reveal({ view, cur, onAgain, onQuick, onBack, onInventory }) {
  const item = view.item;
  const [equipped, setEquipped] = useState(view.equipped);
  useEffect(() => sound.reveal(item.color), []);
  const title = (item.souvenir ? "Souvenir " : "") + (item.statTrak && item.kind === "skin" ? "StatTrak™ " : "") + item.name;
  return html`<div class="reveal" style=${`--c:${item.color}`}>
    <div class="reveal-glow"></div>
    ${["#d32ce6", "#eb4b4b", "#e4ae39", "#ffd700"].includes(item.color) && html`<div class="reveal-rays"></div>`}
    <div class="reveal-pic"><${ItemPic} src=${item.image} size=${512} /></div>
    <div class="reveal-grade">${item.grade}${item.phase ? ` · ${item.phase}` : ""}</div>
    <h2 class="reveal-name">${title}</h2>
    ${item.kind === "skin" && item.paint > 0 && html`<div class="reveal-stats mono">
      <span>${wearName(item.wear)}</span><span>Float ${item.wear.toFixed(9)}</span><span>Pattern ${item.seed}</span></div>
      <div class="reveal-wear"><${WearBar} wear=${item.wear} /></div>`}
    ${item.kind === "skin" && item.paint === 0 && html`<div class="reveal-stats mono"><span>Vanilla</span><span>Pattern ${item.seed}</span></div>`}
    <div class="reveal-price">${view.price != null ? `Market ${money(view.price, cur)}` : "No market price yet"}</div>
    <div class="hstack reveal-actions">
      ${equippable(item) && html`<${Button} kind="prominent" icon="check" disabled=${equipped}
        onClick=${() => call("cases.equip", item.uid).then(() => setEquipped(true), () => {})}>${equipped ? "Equipped" : "Equip"}<//>`}
      <${Button} icon="skins" onClick=${onInventory}>Inventory<//>
      <${Button} icon="refresh" onClick=${onAgain}>Open Another<//>
      <${Button} icon="bolt" onClick=${onQuick}>Quick Open<//>
      <${Button} kind="plain" onClick=${onBack}>Back to Case<//>
    </div>
  </div>`;
}

/** "AK-47 | Redline" -> ["AK-47", "Redline"]; "★ Karambit" -> ["★ Karambit", ""]. */
const splitName = n => { const b = n.indexOf("|"); return b < 0 ? [n, ""] : [n.slice(0, b).trim(), n.slice(b + 1).trim()]; };

function ContentCard({ name, image, color, price, cur, rare, count }) {
  const [weapon, finish] = splitName(name);
  return html`<div class=${"content-card" + (rare ? " rare" : "")} style=${`--c:${color}`} title=${name}>
    <div class="pic">${image ? html`<${ItemPic} src=${image} size=${256} />` : html`<span class="rare-star">★</span>`}</div>
    ${price != null && html`<div class="price">${money(price, cur)}+</div>`}
    <div class="cc-text">${rare
      ? html`<div class="cc-finish">Rare Special Item</div><div class="cc-weapon">${count} knives / gloves</div>`
      : html`<div class="cc-weapon">${finish ? weapon : ""}</div><div class="cc-finish">${finish || weapon}</div>`}</div>
  </div>`;
}

/** Several quick opens in a row: the drops side by side, the best one highlighted. */
function MultiReveal({ drops, cur, onAgain, onBack, onInventory }) {
  const best = drops.reduce((a, b) => (gradeLevel(b.item.color) - gradeLevel(a.item.color) || (b.price ?? 0) - (a.price ?? 0)) > 0 ? b : a, drops[0]);
  useEffect(() => sound.reveal(best.item.color), []);
  return html`<div class="multi-reveal">
    <div class="multi-grid">${drops.map((v, n) => html`<div key=${n} class=${"multi-drop" + (v === best ? " best" : "")} style=${`--c:${v.item.color}`}
        title=${`${v.item.statTrak && v.item.kind === "skin" ? "StatTrak™ " : ""}${v.item.name}`}>
      <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
      <div class="cc-weapon">${v.item.statTrak && v.item.kind === "skin" ? "StatTrak™ " : ""}${v.weapon}</div>
      <div class="cc-finish">${v.finish}</div>
      <div class="faint small">${v.item.kind === "skin" && v.item.paint > 0 ? wearName(v.item.wear) : v.item.grade}${v.price != null ? ` · ${money(v.price, cur)}` : ""}</div>
    </div>`)}</div>
    <div class="hstack reveal-actions">
      <${Button} kind="prominent" icon="refresh" onClick=${onAgain}>Open ${drops.length} More<//>
      <${Button} icon="skins" onClick=${onInventory}>Inventory<//>
      <${Button} kind="plain" onClick=${onBack}>Back to Case<//>
    </div>
  </div>`;
}

function CaseSheet({ id, close }) {
  const [contents] = useApi("cases.contents", id);
  const [phase, setPhase] = useState("view"); // view | unlock | spin | reveal | multi
  const [result, setResult] = useState(null);
  const [drops, setDrops] = useState([]);
  const [recent, setRecent] = useState([]);
  const skip = useRef(false);
  const busy = phase === "unlock" || phase === "spin" || phase === "opening";
  // Quick open skips the unlock animation and the reel and goes straight to the reveal.
  const open = async (fast = false) => {
    skip.current = false;
    if (!fast) {
      try { ctx(); } catch { /* no sound */ }
      setPhase("unlock");
      sound.unlock();
    }
    const [r] = await Promise.all([call("cases.open", id).catch(() => null), new Promise(ok => setTimeout(ok, fast ? 0 : 900))]);
    if (!r) { setPhase("view"); return; }
    setResult(r);
    if (fast) { setRecent(list => [r.item, ...list].slice(0, 30)); setPhase("reveal"); }
    else setPhase("spin");
  };
  const openMany = async n => {
    try { ctx(); } catch { /* no sound */ }
    setPhase("opening");
    const got = await call("cases.openMany", { id, count: n }).catch(() => null);
    if (!got?.length) { setPhase("view"); return; }
    setDrops(got);
    setRecent(list => [...got.slice().reverse(), ...list].slice(0, 30));
    setPhase("multi");
  };
  const landed = () => { setRecent(list => [result.item, ...list].slice(0, 30)); setPhase("reveal"); };
  // Space opens (Shift+Space: quick open); during the spin it skips to the drop. Focused buttons keep their own Space.
  useEffect(() => {
    const key = e => {
      if (e.code !== "Space" || e.repeat || e.target.closest?.("input, textarea, button")) return; // held Space: one open
      e.preventDefault();
      if (phase === "spin") skip.current = true;
      else if (phase === "view" || phase === "reveal") open(e.shiftKey);
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  });
  if (!contents) return html`<div class="case-sheet"><div class="empty small">Loading…</div></div>`;
  const cur = contents.currency;
  const toInventory = () => { close(undefined); navigate("inventory"); };

  return html`<div class=${"case-sheet phase-" + phase}>
    <div class="case-topbar">
      <div class="grow"><h2>${contents.name}</h2><div class="faint small">${TYPE_NAMES[contents.type] ?? contents.type} · Free to open · Odds as in CS2</div></div>
      <${Button} kind="plain" icon="x" onClick=${() => close(undefined)} title="Close" />
    </div>

    ${phase === "view" && html`<div class="case-hero">
      <div class="case-hero-pic"><${ItemPic} src=${contents.image} size=${512} /></div>
      <div class="case-hero-side">
        <div class="odds-legend">${contents.groups.map(g => html`<div class="odd" style=${`--c:${g.color}`}>
          <span class="dot"></span><span class="grow">${g.grade}</span><b>${pct(g.chance)}</b></div>`)}</div>
        <div class="hstack open-buttons">
          <${Button} kind="prominent" icon="key" disabled=${busy} onClick=${() => open()} title="Space">Unlock Container<//>
          <${Button} icon="bolt" disabled=${busy} onClick=${() => open(true)} title="Skip the animation (Shift+Space)">Quick Open<//>
        </div>
        <div class="hstack open-many">
          <${Button} size="sm" disabled=${busy} onClick=${() => openMany(5)}>Open ×5<//>
          <${Button} size="sm" disabled=${busy} onClick=${() => openMany(10)}>Open ×10<//>
        </div>
        <div class="faint small">Free and unlimited. What you unbox goes to your Inventory here, not to Steam. Space opens, click the reel to skip.</div>
      </div>
    </div>
    <div class="case-contents scroll">
      ${contents.groups.map(g => html`<div class="grade-group">
        <div class="grade-head" style=${`--c:${g.color}`}><span class="dot"></span>${g.grade}<span class="faint">${pct(g.chance)}</span></div>
        <div class="grade-items">
          ${g.grade === "Rare Special Item"
            ? html`<${ContentCard} name="" image=${g.image} color="#e4ae39" rare count=${g.items.length} />`
            : g.items.map(i => html`<${ContentCard} name=${i.name} image=${i.image} color=${i.color} price=${i.price} cur=${cur} />`)}
        </div>
      </div>`)}
    </div>`}

    ${phase === "unlock" && html`<div class="case-stage"><div class="case-unlocking"><div class="pic"><${ItemPic} src=${contents.image} size=${512} /></div></div>
      <div class="stage-caption">Unlocking…</div></div>`}
    ${phase === "opening" && html`<div class="case-stage"><div class="case-unlocking"><div class="pic"><${ItemPic} src=${contents.image} size=${512} /></div></div>
      <div class="stage-caption">Opening…</div></div>`}
    ${phase === "spin" && html`<div class="case-stage">
      <div class="stage-case"><${ItemPic} src=${contents.image} size=${256} /></div>
      <${Reel} result=${result} onDone=${landed} skip=${skip} />
      <div class="stage-caption">Unlocking ${contents.name}… <span class="faint">click or Space to skip</span></div></div>`}
    ${phase === "reveal" && html`<div class="case-stage"><${Reveal} key=${result.item.item.uid} view=${result.item} cur=${cur} onAgain=${() => open()} onQuick=${() => open(true)} onBack=${() => setPhase("view")}
      onInventory=${toInventory} /></div>`}
    ${phase === "multi" && html`<div class="case-stage"><${MultiReveal} drops=${drops} cur=${cur} onAgain=${() => openMany(drops.length)} onBack=${() => setPhase("view")}
      onInventory=${toInventory} /></div>`}

    ${recent.length > 0 && phase !== "spin" && html`<div class="recent-drops">
      <span class="faint small">This session · ${recent.length}</span>
      ${recent.map((v, n) => html`<div key=${n} class="recent" style=${`--c:${v.item.color}`} title=${`${v.item.statTrak ? "StatTrak™ " : ""}${v.item.name}${v.price != null ? " · " + money(v.price, cur) : ""}`}>
        <${ItemPic} src=${v.item.image} size=${128} /></div>`)}
    </div>`}
  </div>`;
}

export function CasesPage() {
  const [state] = useApi("cases.state");
  const [type, setType] = useState("Case");
  const [mute, setMute] = useState(isMuted());
  const [q, setQ] = useState("");
  if (!state) return html`<${Page} title="Cases" />`;

  const types = [...new Set(state.cases.map(c => c.type))];
  const shown = state.cases.filter(c => (types.length < 2 || c.type === type) && (!q || c.name.toLowerCase().includes(q.toLowerCase())));
  const openCase = id => sheet(close => html`<${CaseSheet} id=${id} close=${close} />`, { wide: true });
  const enterCode = async () => {
    const code = await prompt({ title: "Admin Code", message: "Enter the admin code.", placeholder: "Code", ok: "OK" });
    if (code?.trim()) tryCall("cases.unlock", code.trim());
  };

  const cur = state.currency;
  return html`<${Page} title="Cases" subtitle=${`Free to open · ${state.opened} opened · inventory worth ${money(state.inventoryValue, cur)}` +
    (state.keysUsed || state.casesCost ? ` · for real: ${state.keysUsed} key${state.keysUsed === 1 ? "" : "s"} + ${money(state.casesCost, cur)} of cases` +
      ` = ${money(state.spent, cur)} · ${state.profit >= 0 ? "profit" : "loss"} ${money(Math.abs(state.profit), cur)}` : "")}
      actions=${html`
        <${Button} icon=${mute ? "x" : "bolt"} onClick=${() => { setMuted(!mute); setMute(!mute); }} title="Case opening sounds">${mute ? "Sound off" : "Sound on"}<//>
        <${Select} width=${90} value=${cur} onChange=${v => tryCall("cases.currency", v)} options=${["USD", "EUR", "GBP"]} />
        <${Button} icon="skins" onClick=${() => navigate("inventory")}>Inventory (${state.inventoryCount})<//>`}>
    ${state.dbEmpty
      ? html`<${Section}><div class="empty"><${Icon} name="download" size=${30} /><div>The case list isn't downloaded yet. Press Update on Setup › Skin, sticker and charm lists.</div></div><//>`
      : html`<div class="stack">
        <div class="hstack case-filters">
          ${types.length > 1 && html`<${Segmented} options=${types.map(t => ({ value: t, label: TYPE_NAMES[t] ?? t }))} value=${type} onChange=${setType} />`}
          <div class="grow"><${TextField} icon="search" placeholder="Search cases" value=${q} onInput=${setQ} /></div>
        </div>
        <div class="case-grid">
          ${shown.map(c => html`<div key=${c.id} class="case-tile glass" role="button" tabindex="0" onClick=${() => openCase(c.id)} title=${c.name}>
            <div class="pic"><${ItemPic} src=${c.image} size=${256} /></div>
            <div class="name">${c.name}</div>
            <div class="faint small">${c.rare ? "★ knives / gloves" : `${c.count} items`}</div>
          </div>`)}
        </div>
        <div class="case-code faint small">${state.unlocked
          ? html`Admin: every skin is unlocked on this PC · <a href="#" onClick=${e => { e.preventDefault(); tryCall("cases.lock"); }}>back to unbox only</a>`
          : html`<a href="#" onClick=${e => { e.preventDefault(); enterCode(); }}>Admin code</a>`}</div>
      </div>`}
  <//>`;
}
