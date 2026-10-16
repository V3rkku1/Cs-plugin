// Skins: category + weapon column, a gallery of finishes, and an inspector with the preview, wear,
// pattern, StatTrak, name tag, model, stickers + charm and Equip. Import and Loadouts open as sheets.
import { html, useState, useEffect, useRef } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, img } from "../api.js";
import { Section, Row, Button, Toggle, Segmented, Slider, Stepper, TextField, Select, Badge, Icon, confirm, prompt, sheet } from "../ui.js";

const CATEGORIES = ["Knives", "Gloves", "Pistols", "Rifles", "SMGs", "Heavy"];
const CONDITIONS = [
  ["FN", "Factory New", 0, 0.07], ["MW", "Minimal Wear", 0.07, 0.15], ["FT", "Field-Tested", 0.15, 0.38],
  ["WW", "Well-Worn", 0.38, 0.45], ["BS", "Battle-Scarred", 0.45, 1],
];
const EFFECTS = ["All", "Paper", "Holo", "Foil", "Glitter", "Gold", "Embroidered", "Lenticular"];
const MODELS = [{ value: "auto", label: "Automatic" }, { value: "legacy", label: "Old (legacy)" }, { value: "new", label: "New" }];
const PAGE = 120; // sticker cards drawn per step while scrolling

/** Kept while switching pages, like the old page kept the open weapon. */
const remember = { dataKey: null, cat: 0, def: 0 };
/** Sticker / charm names and pictures seen so far: "sticker:12" -> { name, image, color }. */
const meta = new Map();

// Ctrl+F: app.js switches here and asks for the search box.
let wantFocus = false;
function focusSearch() {
  const input = document.querySelector(".skins-search input");
  if (!input) return;
  wantFocus = false;
  input.focus();
  input.select();
}
window.addEventListener("focus-search", () => { wantFocus = true; focusSearch(); });

const condIndex = w => { const i = CONDITIONS.findIndex(c => w < c[3]); return i < 0 ? 4 : i; };
const round6 = n => Math.round(n * 1e6) / 1e6;
const fresh = () => ({ paint: 0, wear: 0.01, seed: 0, statTrak: false, statTrakCount: 0, nametag: "", legacy: null, stickers: [], keychain: null });
const clone = s => JSON.parse(JSON.stringify(s));
const looksLikeItem = t => t.includes("csgo_econ_action_preview") || t.startsWith("!g") || t.includes("/market/listings/730/");
const goSetup = () => window.dispatchEvent(new CustomEvent("navigate", { detail: "setup" }));

const icons = {
  knife: `<path d="M3 21 14.5 9.5l2 2L5 23z" transform="translate(0 -2)"/><path d="M14.5 7.5 20.5 3c.6 3.6-.6 6.8-3.5 9.5"/>`,
  glove: `<path d="M7 21v-6.5L4.5 11a1.6 1.6 0 0 1 2.4-2L9 11V4.5a1.5 1.5 0 0 1 3 0V10V3.5a1.5 1.5 0 0 1 3 0V10V5a1.5 1.5 0 0 1 3 0v8.5c0 3.6-2.2 7.5-6 7.5z"/>`,
  charm: `<circle cx="12" cy="15" r="5"/><path d="M12 10V6M10 4.5a2 2 0 1 1 4 0 2 2 0 0 1-4 0"/>`,
};
const LocalIcon = ({ name, size = 20 }) => html`<svg class="icon" width=${size} height=${size} viewBox="0 0 24 24" fill="none" stroke="currentColor"
  stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" dangerouslySetInnerHTML=${{ __html: icons[name] }} />`;

/** An item picture through the launcher cache, with a soft placeholder when it can't load. */
function Pic({ src, size = 256, icon = "skins" }) {
  const [bad, setBad] = useState(false);
  useEffect(() => setBad(false), [src]);
  if (!src || bad) return html`<div class="pic-ph">${icons[icon] ? html`<${LocalIcon} name=${icon} size=${28} />` : html`<${Icon} name=${icon} size=${28} />`}</div>`;
  return html`<img src=${img(src, size)} loading="lazy" decoding="async" draggable="false" alt="" onError=${() => setBad(true)} />`;
}

// ------------------------------------------------------------------------------------------------
// Page
// ------------------------------------------------------------------------------------------------

export function SkinsPage() {
  const [state, reloadState] = useApi("skins.state");
  const [cat, setCat] = useState(remember.cat);
  const [def, setDefState] = useState(remember.def);
  const [weapon] = useApi("skins.weapon", def || 0);
  const [work, setWork] = useState(null);
  const [search, setSearch] = useState("");
  const savedJson = useRef();

  const setDef = d => { remember.def = d; setDefState(d); setSearch(""); };
  const choose = (i, st) => {
    const list = st.categories[i]?.weapons ?? [];
    const equipped = i === 0 ? st.knife : i === 1 ? st.gloves : 0;
    remember.cat = i;
    setCat(i);
    setDef((list.find(w => w.def === equipped) ?? list[0])?.def ?? 0);
  };

  // First visit, or the CS2 folder / item data changed: start on the equipped knife.
  useEffect(() => {
    if (!state) return;
    if (state.dataKey !== remember.dataKey) { remember.dataKey = state.dataKey; choose(0, state); }
    else if (!def) choose(cat, state);
  }, [state]);

  // A working copy of the weapon's saved settings; kept while editing, replaced when the saved one changes.
  useEffect(() => {
    if (!weapon || weapon.def !== def) return;
    weapon.stickers.forEach(s => meta.set("sticker:" + s.id, s));
    weapon.charms.forEach(s => meta.set("charm:" + s.id, s));
    const json = JSON.stringify(weapon.saved);
    if (work?.def === def && savedJson.current === json) return;
    savedJson.current = json;
    setWork({ def, skin: weapon.saved ? clone(weapon.saved) : fresh() });
  }, [weapon]);

  useEffect(() => { if (wantFocus) focusSearch(); });

  // Whole-loadout changes pick what is equipped now (e.g. the new random knife).
  const afterWholeChange = async () => {
    const st = await reloadState();
    if (st) choose(cat, st);
  };

  const openImport = async () => {
    const item = await sheet(close => html`<${ImportSheet} close=${close} />`, { wide: true });
    if (!item) return;
    await tryCall("skins.equip", { def: item.def, skin: item.skin, imported: true }).then(() => {});
    const st = await reloadState();
    const i = st?.categories.findIndex(c => c.weapons.some(w => w.def === item.def)) ?? -1;
    if (i >= 0) { remember.cat = i; setCat(i); }
    setDef(item.def);
  };

  const openLoadouts = async () => {
    const info = await call("skins.loadouts").catch(() => null);
    if (!info) return;
    const r = await sheet(close => html`<${LoadoutsSheet} info=${info} close=${close} />`, { wide: true });
    if (!r) return;
    if (r.choice === "save") {
      const d = new Date(), p = n => String(n).padStart(2, "0");
      const name = await prompt({ title: "Save Loadout", message: "Name for this loadout:", ok: "Save",
        value: `Loadout ${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}.${p(d.getMinutes())}` });
      if (name?.trim()) tryCall("skins.loadoutSave", name);
      return;
    }
    if (r.choice === "reset" && !await confirm({ title: "Reset everything?", ok: "Reset", danger: true,
      message: "Remove every skin, the knife and the gloves? (Save the loadout first if you want to keep it.)" })) return;
    const calls = { load: ["skins.loadoutLoad", r.name], random: ["skins.randomize"], reset: ["skins.reset"] };
    if (await call(...calls[r.choice]).then(() => true, () => false)) afterWholeChange();
  };

  const actions = html`
    <${Button} icon="import" onClick=${openImport}>Import Item<//>
    <${Button} icon="layers" onClick=${openLoadouts}>Loadouts<//>`;

  return html`<div class="page skins-page">
    <header class="page-header">
      <div><h1>Skins</h1><p>Pick a finish, tune it, press Equip. It changes in game within a second.</p></div>
      <div class="actions">${actions}</div>
    </header>
    ${!state ? null : html`<div class="skins-layout">
      <${WeaponColumn} state=${state} cat=${cat} def=${def} onCat=${i => choose(i, state)} onPick=${setDef} />
      ${weapon?.def === def && work?.def === def
        ? html`
          <${Gallery} state=${state} weapon=${weapon} skin=${work.skin} search=${search} setSearch=${setSearch}
            onPick=${skin => setWork({ def, skin })} />
          <${Inspector} state=${state} weapon=${weapon} skin=${work.skin} setSkin=${skin => setWork({ def, skin })} />`
        : html`<div class="skins-panel glass skins-gallery"></div><div class="skins-panel glass skins-inspector"></div>`}
    </div>`}
  </div>`;
}

// ------------------------------------------------------------------------------------------------
// Left: categories and weapons
// ------------------------------------------------------------------------------------------------

function WeaponColumn({ state, cat, def, onCat, onPick }) {
  const list = state.categories[cat]?.weapons ?? [];
  const icon = cat === 0 ? "knife" : cat === 1 ? "glove" : "crosshair";
  useEffect(() => document.querySelector(".weapon-row.active")?.scrollIntoView({ block: "nearest" }), [def, cat]);
  return html`<aside class="skins-panel glass skins-weapons">
    <${Segmented} options=${CATEGORIES} value=${cat} onChange=${onCat} cols=${3} />
    <div class="weapon-list scroll">
      ${list.length === 0 && html`<div class="empty small">Nothing here without the item data.</div>`}
      ${list.map(w => html`<button key=${w.def} class=${"weapon-row" + (w.def === def ? " active" : "")} onClick=${() => onPick(w.def)}
          style=${w.color ? `--c:${w.color}` : ""}>
        <div class="thumb"><${Pic} src=${w.image} size=${128} icon=${icon} /></div>
        <div class="text">
          <div class="name">${w.name}</div>
          <div class=${"detail" + (w.color ? " rar" : "")}>${w.detail}</div>
        </div>
        ${w.equipped && html`<span class="dot-equipped" title="Equipped"></span>`}
      </button>`)}
    </div>
  </aside>`;
}

// ------------------------------------------------------------------------------------------------
// Middle: finishes
// ------------------------------------------------------------------------------------------------

function Gallery({ state, weapon, skin, search, setSearch, onPick }) {
  const words = search.toLowerCase().split(" ").filter(Boolean);
  const finishes = weapon.finishes.filter(f => words.every(w => f.name.toLowerCase().includes(w) || f.rarity.toLowerCase().includes(w)));
  const savedPaint = weapon.saved?.paint;
  const icon = weapon.isKnife ? "knife" : weapon.isGloves ? "glove" : "crosshair";

  const pick = f => {
    const s = { ...skin, paint: f ? f.paint : 0 };
    // Keep the wear inside what this skin can actually have.
    if (f && (s.wear < f.min || s.wear > f.max)) s.wear = Math.max(f.min, 0.00001) + (f.max - f.min) * 0.02;
    onPick(s);
    return s;
  };
  const equip = f => tryCall("skins.equip", { def: weapon.def, skin: pick(f) });

  const card = (f, key) => {
    const paint = f ? f.paint : 0;
    return html`<button key=${key} class=${"finish sheen" + (skin.paint === paint ? " selected" : "")} style=${`--c:${f?.color || "#8e8e93"}`}
        onClick=${() => pick(f)} onDblClick=${() => equip(f)} title=${f ? `${f.name} · ${f.rarity}` : "No paint"}>
      <div class="pic"><${Pic} src=${f ? f.image : weapon.image} size=${256} icon=${icon} /></div>
      <div class="name">${f ? f.name : "Vanilla"}</div>
      <div class="rarity">${f ? f.rarity : "No paint"}</div>
      ${savedPaint === paint && html`<span class="equipped-tag">Equipped</span>`}
    </button>`;
  };

  return html`<section class="skins-panel glass skins-gallery">
    <div class="gallery-head">
      <div class="text">
        <h2>${weapon.name}</h2>
        <div class="muted small">${weapon.finishes.length > 0
          ? `${weapon.finishes.length} finishes · double-click a card to equip`
          : "Pick a finish by paint ID or import an item"}</div>
      </div>
      <div class="skins-search"><${TextField} icon="search" placeholder="Search finishes" value=${search} onInput=${setSearch} /></div>
    </div>
    <div class="finish-grid scroll">
      ${!weapon.isGloves && words.length === 0 && card(null, "vanilla")}
      ${finishes.map(f => card(f, f.paint))}
      ${state.dbEmpty && html`<div class="empty grid-wide">
        <${Icon} name="download" size=${34} />
        <div>Skin list not downloaded yet.</div>
        <div class="faint small">You can still import items with a !gen code or inspect link.</div>
        <div style="margin-top:12px"><${Button} size="sm" kind="prominent" onClick=${goSetup}>Open Setup<//></div>
      </div>`}
      ${!state.dbEmpty && finishes.length === 0 && words.length > 0 && html`<div class="empty grid-wide">
        <${Icon} name="search" size=${30} /><div>No skins match your search.</div></div>`}
    </div>
  </section>`;
}

// ------------------------------------------------------------------------------------------------
// Right: inspector
// ------------------------------------------------------------------------------------------------

function Inspector({ state, weapon, skin, setSkin }) {
  const info = weapon.finishes.find(f => f.paint === skin.paint);
  const upd = patch => setSkin({ ...skin, ...patch });
  const paintable = skin.paint !== 0;
  const color = info?.color || "";
  const title = `${weapon.name} | ${info ? info.name : paintable ? `Paint #${skin.paint}` : "Vanilla"}`;
  const ci = condIndex(skin.wear);
  const dirty = JSON.stringify(skin) !== JSON.stringify(weapon.saved ?? fresh());
  const [equip, equipping] = useBusy(() => call("skins.equip", { def: weapon.def, skin }));
  const icon = weapon.isKnife ? "knife" : weapon.isGloves ? "glove" : "crosshair";

  const setWear = v => upd({ wear: Math.max(round6(v), 0.00001) });
  const pickCondition = i => {
    const c = CONDITIONS[i];
    const min = Math.max(c[2], info?.min ?? 0), max = Math.min(c[3], info?.max ?? 1);
    upd({ wear: round6(Math.max(min + (max - min) * 0.25, 0.00001)) });
  };

  const pickSticker = async slot => {
    const cur = skin.stickers.find(s => s.slot === slot);
    const r = await pickAttachment("sticker", `Pick a sticker for slot ${slot + 1}`, cur?.id ?? 0, cur?.wear ?? 0);
    if (!r) return;
    upd({ stickers: [...skin.stickers.filter(s => s.slot !== slot), { slot, id: r.id, wear: r.wear, scale: 1, rotation: 0, offsetX: 0, offsetY: 0 }] });
  };
  const pickCharm = async () => {
    const r = await pickAttachment("charm", "Pick a charm", skin.keychain?.id ?? 0, 0);
    if (r) upd({ keychain: { id: r.id, offsetX: 0, offsetY: 0, offsetZ: 0, seed: 0 } });
  };

  return html`<aside class="skins-panel glass skins-inspector" style=${color ? `--c:${color}` : ""}>
    <div class="inspector-body scroll">
      <div class=${"preview" + (color ? " tinted" : "")}>
        <${Pic} src=${info?.image || weapon.image} size=${512} icon=${icon} />
        <div class="chips">
          ${paintable && html`<span class="chip">${CONDITIONS[ci][1]} · ${skin.wear.toFixed(6)}</span>`}
          ${skin.statTrak && html`<span class="chip st">StatTrak™ ${skin.statTrakCount}</span>`}
        </div>
        ${skin.nametag.trim() && html`<div class="nametag">“${skin.nametag.trim()}”</div>`}
      </div>
      <div class="item-title">
        <h2>${title}</h2>
        ${info && html`<div class="rar-line"><span class="rar-dot"></span>${info.rarity}<span class="faint"> · Float ${info.min.toFixed(2)} – ${info.max.toFixed(2)}</span></div>`}
      </div>

      <${Section} title="Wear" pad>
        <div class=${"wear" + (paintable ? "" : " off")}>
          <${Slider} min=${0} max=${1} step=${0.001} value=${skin.wear} onChange=${setWear}
            tint=${color || undefined} format=${() => CONDITIONS[ci][0]} />
          <${Segmented} class="cond" value=${ci} onChange=${pickCondition} disabled=${!paintable}
            options=${CONDITIONS.map((c, i) => ({ value: i, label: c[0], disabled: !!info && !(c[3] > info.min && c[2] < info.max) }))} />
          <div class="exact"><span class="muted">Exact float</span>
            <${Stepper} value=${skin.wear} min=${0} max=${1} step=${0.001} width=${92} disabled=${!paintable}
              format=${v => v.toFixed(6)} onChange=${setWear} /></div>
        </div>
      <//>

      <${Section} title="Details">
        ${weapon.finishes.length === 0 && html`<${Row} title="Paint ID" subtitle="Finish number from the game files">
          <${Stepper} value=${skin.paint} min=${0} max=${100000} width=${64} onChange=${v => upd({ paint: v })} />
        <//>`}
        <${Row} title="Pattern">
          <${Button} kind="plain" size="sm" icon="dice" disabled=${!paintable} title="Random pattern"
            onClick=${() => upd({ seed: Math.floor(Math.random() * 1001) })}>Random<//>
          <${Stepper} value=${skin.seed} min=${0} max=${1000} width=${48} disabled=${!paintable} onChange=${v => upd({ seed: Math.round(v) })} />
        <//>
        <${Row} title="StatTrak™">
          ${skin.statTrak && html`<${Stepper} value=${skin.statTrakCount} min=${0} max=${999999} width=${62}
            onChange=${v => upd({ statTrakCount: Math.round(v) })} />`}
          <${Toggle} value=${skin.statTrak} onChange=${v => upd({ statTrak: v })} />
        <//>
        <${Row} title="Name Tag">
          <${TextField} width=${170} maxLength=${20} placeholder="None" value=${skin.nametag} onInput=${v => upd({ nametag: v })} />
        <//>
        <${Row} title="Model" subtitle="If the skin looks stretched">
          <${Select} options=${MODELS} value=${skin.legacy == null ? "auto" : skin.legacy ? "legacy" : "new"}
            onChange=${v => upd({ legacy: v === "auto" ? null : v === "legacy" })} />
        <//>
      <//>

      ${!weapon.isGloves && html`<${Section} title=${weapon.isKnife ? "Stickers" : "Stickers and Charm"} note="right-click to remove" pad>
        <div class="slots">
          ${[0, 1, 2, 3, 4].map(slot => {
            const st = skin.stickers.find(s => s.slot === slot);
            return html`<${Slot} key=${slot} label=${String(slot + 1)} item=${st && (meta.get("sticker:" + st.id) ?? { name: `#${st.id}` })}
              onPick=${() => pickSticker(slot)} onClear=${() => upd({ stickers: skin.stickers.filter(s => s.slot !== slot) })} />`;
          })}
          ${!weapon.isKnife && html`<${Slot} label="Charm" charm item=${skin.keychain && (meta.get("charm:" + skin.keychain.id) ?? { name: `#${skin.keychain.id}` })}
            onPick=${pickCharm} onClear=${() => upd({ keychain: null })} />`}
        </div>
      <//>`}

      <div class="share">
        <${Button} kind="plain" size="sm" icon="copy" onClick=${() => tryCall("skins.copy", { def: weapon.def, skin, kind: "gen" })}>Copy !gen<//>
        <${Button} kind="plain" size="sm" icon="link" onClick=${() => tryCall("skins.copy", { def: weapon.def, skin, kind: "link" })}>Copy Inspect Link<//>
      </div>
    </div>

    <div class="inspector-foot">
      ${!state.pluginInstalled
        ? html`<div class="foot-note warn"><${Icon} name="warning" size=${15} /> Install the plugin first (<a onClick=${goSetup}>Setup page</a>).</div>`
        : dirty && html`<div class="foot-note"><span class="pulse"></span>${weapon.saved || weapon.equipped ? "Unsaved changes" : "Not equipped yet"}</div>`}
      <div class="foot-buttons">
        <${Button} kind="prominent" size="lg" icon="check" class="grow" busy=${equipping} disabled=${!state.pluginInstalled}
          onClick=${equip}>Equip<//>
        <${Button} kind="destructive" size="lg" disabled=${!state.pluginInstalled || !weapon.equipped}
          onClick=${() => tryCall("skins.unequip", weapon.def)}>Unequip<//>
      </div>
    </div>
  </aside>`;
}

function useBusy(fn) {
  const [busy, setBusy] = useState(false);
  return [async () => {
    if (busy) return;
    setBusy(true);
    try { await fn(); } catch { /* toast shown */ } finally { setBusy(false); }
  }, busy];
}

/** One sticker / charm slot: "+" when empty, the picture when filled. Right-click removes. */
function Slot({ label, item, onPick, onClear, charm }) {
  return html`<button class=${"slot" + (item ? " filled" : "") + (charm ? " charm" : "")} onClick=${onPick}
      onContextMenu=${e => { e.preventDefault(); if (item) onClear(); }}
      title=${item ? `${item.name}  (right-click to remove)` : `Add (${label})`}>
    ${item ? html`<${Pic} src=${item.image} size=${128} icon=${charm ? "charm" : "sticker"} />
        <span class="x" onClick=${e => { e.stopPropagation(); onClear(); }}><${Icon} name="x" size=${11} stroke=${2.6} /></span>`
      : html`<${Icon} name="plus" size=${18} stroke=${2} /><span class="label">${label}</span>`}
  </button>`;
}

// ------------------------------------------------------------------------------------------------
// Sticker / charm picker
// ------------------------------------------------------------------------------------------------

function pickAttachment(kind, title, currentId, currentWear) {
  return sheet(close => html`<${AttachmentPicker} kind=${kind} title=${title} currentId=${currentId} currentWear=${currentWear} close=${close} />`, { wide: true });
}

function AttachmentPicker({ kind, title, currentId, currentWear, close }) {
  const isSticker = kind === "sticker";
  const [search, setSearch] = useState("");
  const [effect, setEffect] = useState(0);
  const [limit, setLimit] = useState(PAGE);
  const [res, setRes] = useState(null);
  const [sel, setSel] = useState(currentId);
  const [scrape, setScrape] = useState(Math.round(Math.min(1, Math.max(0, currentWear)) * 100));
  const seq = useRef(0);
  const grid = useRef();

  useEffect(() => {
    const mine = ++seq.current;
    const t = setTimeout(() => call("skins.stickers", { kind, search, effect: EFFECTS[effect], limit })
      .then(r => { if (mine === seq.current) setRes(r); }).catch(() => {}), search ? 140 : 0);
    return () => clearTimeout(t);
  }, [search, effect, limit]);

  const filter = fn => { setLimit(PAGE); grid.current && (grid.current.scrollTop = 0); fn(); };
  const use = id => {
    if (!id) return;
    const item = res?.items.find(i => i.id === id);
    if (item) meta.set(kind + ":" + id, item);
    close({ id, wear: scrape / 100 });
  };
  const more = e => {
    const el = e.currentTarget;
    if (res && res.items.length < res.total && el.scrollTop + el.clientHeight > el.scrollHeight - 400) setLimit(l => Math.max(l, res.items.length + PAGE));
  };

  return html`<div class="picker">
    <div class="picker-head">
      <h2>${title}</h2>
      <span class="faint small">${res ? `${res.total.toLocaleString()} found` : ""}</span>
    </div>
    <div class="hstack picker-filters">
      <div class="grow"><${TextField} icon="search" autofocus placeholder=${isSticker ? "Search team, player, tournament or name" : "Search charms"}
        value=${search} onInput=${v => filter(() => setSearch(v))} onEnter=${() => use(sel)} /></div>
      ${isSticker && html`<${Segmented} options=${EFFECTS} value=${effect} onChange=${v => filter(() => setEffect(v))} />`}
    </div>
    <div class="picker-grid scroll" ref=${grid} onScroll=${more}>
      ${res?.items.map(s => html`<button key=${s.id} class=${"finish small-card" + (s.id === sel ? " selected" : "")} style=${`--c:${s.color || "#8e8e93"}`}
          onClick=${() => setSel(s.id)} onDblClick=${() => use(s.id)} title=${s.name}>
        <div class="pic"><${Pic} src=${s.image} size=${128} icon=${isSticker ? "sticker" : "charm"} /></div>
        <div class="name">${s.name}</div>
        <div class="rarity">${s.rarity}</div>
      </button>`)}
      ${res && res.total === 0 && html`<div class="empty grid-wide"><${Icon} name="search" size=${30} /><div>Nothing found.</div></div>`}
    </div>
    <div class="picker-foot">
      ${isSticker && html`<div class="hstack nowrap scrape"><span>Scrape</span>
        <${Slider} min=${0} max=${100} value=${scrape} onChange=${setScrape} width=${240} format=${v => v === 0 ? "New" : `${v}%`} /></div>`}
      <div class="grow"></div>
      <${Button} onClick=${() => close(undefined)}>Cancel<//>
      <${Button} kind="prominent" disabled=${!sel} onClick=${() => use(sel)}>Use<//>
    </div>
  </div>`;
}

// ------------------------------------------------------------------------------------------------
// Import a real item
// ------------------------------------------------------------------------------------------------

function ImportSheet({ close }) {
  const [text, setText] = useState("");
  const [p, setP] = useState(null);
  const seq = useRef(0);

  // Pre-fill from the clipboard when it looks like an item.
  useEffect(() => {
    call("skins.clipboard").then(clip => { clip = (clip ?? "").trim(); if (looksLikeItem(clip)) setText(clip); }).catch(() => {});
  }, []);
  useEffect(() => {
    const mine = ++seq.current;
    const t = setTimeout(() => call("skins.parse", text).then(r => { if (mine === seq.current) setP(r); }).catch(() => {}), 150);
    return () => clearTimeout(t);
  }, [text]);

  const ok = p?.ok && text.trim() !== "";
  return html`<div class="import">
    <h2>Import a Real Item</h2>
    <p>Paste a !gen code (CSFloat → Copy !gen), an inspect link with item data (csgo_econ_action_preview 00…), or a Steam
      Market listing name or link. Old market "Inspect in Game" links (…M123A456D789) don't contain the item - open those
      on CSFloat and copy the !gen code.</p>
    <${TextField} multiline rows=${3} autofocus placeholder="Paste a !gen code or inspect link…" value=${text} onInput=${setText} />
    <div class=${"import-preview" + (ok ? "" : " empty-state")} style=${ok && p.color ? `--c:${p.color}` : ""}>
      ${ok ? html`
        <div class="pic"><${Pic} src=${p.image} size=${512} /></div>
        <div class="text">
          <div class="name">${p.name}</div>
          ${p.lines.map(l => html`<div class="muted small">${l}</div>`)}
          ${p.note && html`<div class="note small"><${Icon} name="info" size=${14} /> ${p.note}</div>`}
        </div>`
      : p?.error && text.trim() ? html`<div class="error"><${Icon} name="warning" size=${18} /> ${p.error}</div>`
      : html`<div class="faint">The item appears here</div>`}
    </div>
    <div class="buttons">
      <${Button} icon="copy" onClick=${() => call("skins.clipboard").then(c => setText(c ?? "")).catch(() => {})}>Paste<//>
      <div class="grow"></div>
      <${Button} onClick=${() => close(undefined)}>Cancel<//>
      <${Button} kind="prominent" icon="import" disabled=${!ok} onClick=${() => close(p)}>Import and Equip<//>
    </div>
  </div>`;
}

// ------------------------------------------------------------------------------------------------
// Saved loadouts, randomize, reset
// ------------------------------------------------------------------------------------------------

function LoadoutsSheet({ info, close }) {
  const [names, setNames] = useState(info.names);
  const [sel, setSel] = useState(info.names[0] ?? null);
  const [asking, setAsking] = useState(false);

  const del = async () => {
    if (!asking) return setAsking(true);
    setAsking(false);
    if (await call("skins.loadoutDelete", sel).then(() => true, () => false)) {
      const left = names.filter(n => n !== sel);
      setNames(left);
      setSel(left[0] ?? null);
    }
  };

  return html`<div class="loadouts">
    <h2>Loadouts</h2>
    <p>Keep whole loadouts under a name, or shuffle everything.</p>
    <div class="cols" style="--n:2">
      <${Section} title=${names.length ? "Saved Loadouts" : "No saved loadouts yet"}>
        <div class="loadout-list scroll">
          ${names.length === 0 && html`<div class="empty small">Press "Save…" to keep the current one.</div>`}
          ${names.map(n => html`<${Row} key=${n} title=${n} class=${n === sel ? "picked" : ""}
              onClick=${() => { setSel(n); setAsking(false); }}>
            ${n === sel ? html`<${Icon} name="check" size=${16} stroke=${2.4} class="check" />` : html`<span></span>`}
          <//>`)}
        </div>
        <div class="row">
          <div class="hstack grow">
            <${Button} kind="prominent" size="sm" disabled=${!sel} onClick=${() => close({ choice: "load", name: sel })}>Load<//>
            <${Button} kind=${asking ? "prominent destructive" : "destructive"} size="sm" disabled=${!sel} onClick=${del}>
              ${asking ? `Delete "${sel}"?` : "Delete"}<//>
          </div>
        </div>
      <//>
      <${Section} title="Everything">
        <${Row} icon=${{ name: "save", color: "blue" }} title="Save Current" subtitle="Keep it under a name">
          <${Button} size="sm" onClick=${() => close({ choice: "save" })}>Save…<//>
        <//>
        <${Row} icon=${{ name: "shuffle", color: "purple" }} title="Randomize" subtitle="Random skins, knife and gloves">
          <${Button} size="sm" disabled=${!info.canRandomize} onClick=${() => close({ choice: "random" })}>Shuffle<//>
        <//>
        <${Row} icon=${{ name: "trash", color: "red" }} title="Reset All" subtitle="Back to default weapons">
          <${Button} size="sm" kind="destructive" onClick=${() => close({ choice: "reset" })}>Reset<//>
        <//>
      <//>
    </div>
    <div class="buttons"><${Button} kind="prominent" onClick=${() => close(undefined)}>Done<//></div>
  </div>`;
}
