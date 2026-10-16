// Inventory: what you unboxed, laid out like CS2's inventory. Click an item to inspect it, Equip puts it on.
import { html, useState, useEffect, useRef } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, img } from "../api.js";
import { Page, Button, Segmented, Select, TextField, Icon, Slider, Stepper, Toggle, confirm, sheet } from "../ui.js";
import { navigate } from "../app.js";

const wearName = w => w < 0.07 ? "Factory New" : w < 0.15 ? "Minimal Wear" : w < 0.38 ? "Field-Tested" : w < 0.45 ? "Well-Worn" : "Battle-Scarred";
const SYMBOL = { USD: "$", EUR: "€", GBP: "£" };
export const money = (v, cur) => v == null ? "—" : cur === "EUR" ? `${v.toFixed(2).replace(".", ",")}€` : `${SYMBOL[cur] ?? "$"}${v.toFixed(2)}`;
const GRADE_RANK = ["Consumer Grade", "Base Grade", "Industrial Grade", "Mil-Spec Grade", "High Grade", "Restricted", "Remarkable", "Classified", "Exotic", "Covert", "Extraordinary", "Contraband"];
const equippable = v => (v.item.kind === "skin" && v.item.def) || ((v.item.kind === "pin" || v.item.kind === "music") && v.item.collectibleId);

function Pic({ src, size = 256 }) {
  const [bad, setBad] = useState(false);
  return src && !bad ? html`<img src=${img(src, size)} alt="" loading="lazy" onError=${() => setBad(true)} />` : html`<${Icon} name="skins" size=${30} />`;
}

/** CS2's wear bar: the five wear bands with a marker at the float. */
function WearBar({ wear }) {
  return html`<div class="wear-bar">
    <div class="bands"><span class="fn"></span><span class="mw"></span><span class="ft"></span><span class="ww"></span><span class="bs"></span></div>
    <div class="marker" style=${`left:${(wear * 100).toFixed(3)}%`}></div>
  </div>`;
}

function Inspect({ v, cur, close }) {
  const i = v.item;
  const st = i.statTrak && i.kind === "skin";
  return html`<aside class="inv-inspect glass" style=${`--c:${i.color}`}>
    <div class="hstack"><div class="grow"></div><${Button} kind="plain" icon="x" onClick=${close} title="Close" /></div>
    <div class="inspect-pic"><${Pic} src=${i.image} size=${512} /></div>
    <div class="inspect-grade">${i.grade}${i.phase ? ` · ${i.phase}` : ""}</div>
    <div class="inspect-weapon">${st ? html`<span class="st-tag">StatTrak™</span> ` : ""}${i.souvenir ? html`<span class="sv-tag">Souvenir</span> ` : ""}${v.weapon}</div>
    <h2 class="inspect-finish">${v.finish}</h2>
    ${i.kind === "skin" && i.paint > 0 && html`
      <div class="inspect-row"><span>Exterior</span><b>${wearName(i.wear)}</b></div>
      <div class="inspect-row"><span>Float</span><b class="mono">${i.wear.toFixed(14)}</b></div>
      <${WearBar} wear=${i.wear} />
      <div class="inspect-row"><span>Pattern</span><b class="mono">${i.seed}</b></div>`}
    ${st && html`<div class="inspect-row"><span>StatTrak™ kills</span><b class="mono">0</b></div>`}
    ${i.kind === "skin" && v.category === "weapons" && html`
      <div class="inspect-row"><span>Stickers</span><b>${(i.stickers ?? []).length}/5</b></div>
      ${(i.stickers ?? []).length > 0 && html`<div class="inspect-stickers">${i.stickers.map(st => html`<span title=${st.name}><${Pic} src=${st.image} size=${128} /></span>`)}</div>`}
      <${Button} block icon="layers" onClick=${() => sheet(close => html`<${StickerSheet} uid=${i.uid} close=${close} />`, { wide: true })}>Stickers…<//>`}
    <div class="inspect-row"><span>Market price</span><b>${money(v.price, cur)}</b></div>
    <div class="inspect-row"><span>Unboxed from</span><b>${i.caseName}</b></div>
    <div class="inspect-row"><span>Unboxed</span><b>${new Date(i.opened).toLocaleString()}</b></div>
    <div class="inspect-actions">
      ${equippable(v) && (v.equipped
        ? html`<${Button} block onClick=${() => tryCall("cases.unequip", i.uid)}>Unequip<//>`
        : html`<${Button} kind="prominent" block icon="check" onClick=${() => tryCall("cases.equip", i.uid)}>Equip<//>`)}
      <${Button} kind="destructive" block icon="trash" onClick=${async () => {
        if (await confirm({ title: "Delete item?", message: `Remove ${i.name} from your inventory?`, ok: "Delete", danger: true })) { await tryCall("cases.remove", i.uid); close(); }
      }}>Delete<//>
    </div>
  </aside>`;
}

/** CS2-style sticker editor for one gun: apply from your inventory, drag to place, turn, size, scrape. */
function StickerSheet({ uid, close }) {
  const [inv] = useApi("cases.inventory");
  const [choices, reloadChoices] = useApi("inventory.stickers");
  const [sel, setSel] = useState(null);
  const [drag, setDrag] = useState(null); // { slot, x, y } while dragging
  const [local, setLocal] = useState({}); // slot -> edits not saved yet
  const box = useRef();
  const v = inv?.items.find(x => x.item.uid === uid);
  if (!v) return html`<div class="picker"><div class="empty small">Loading…</div></div>`;
  const gun = v.item;
  const stickers = (gun.stickers ?? []).map(st => ({ ...st, ...(local[st.slot] ?? {}) }));
  const current = stickers.find(st => st.slot === sel);

  const save = (slot, change) => {
    const st = stickers.find(x => x.slot === slot);
    if (!st) return;
    const next = { ...st, ...change };
    setLocal(l => ({ ...l, [slot]: change }));
    tryCall("inventory.placeSticker", { gun: uid, slot, offsetX: next.offsetX, offsetY: next.offsetY, rotation: next.rotation, scale: next.scale, wear: next.wear })
      .then(() => setLocal(l => { const c = { ...l }; delete c[slot]; return c; }));
  };
  const toOffset = e => {
    const r = box.current.getBoundingClientRect();
    return { x: Math.max(-1, Math.min(1, ((e.clientX - r.left) / r.width - 0.5) / 0.45)), y: Math.max(-1, Math.min(1, -((e.clientY - r.top) / r.height - 0.5) / 0.4)) };
  };
  const down = (e, slot) => { e.preventDefault(); setSel(slot); setDrag({ slot, ...toOffset(e) }); e.currentTarget.setPointerCapture?.(e.pointerId); };
  const move = e => { if (drag) setDrag({ ...drag, ...toOffset(e) }); };
  const up = () => { if (drag) { save(drag.slot, { offsetX: drag.x, offsetY: drag.y }); setDrag(null); } };
  const apply = async stUid => {
    await tryCall("inventory.applySticker", { gun: uid, sticker: stUid, slot: 0, offsetX: 0, offsetY: 0, rotation: 0, scale: 1 });
    reloadChoices();
  };
  const scrape = async () => {
    if (await confirm({ title: "Scrape off?", message: `${current.name} comes off the gun for good (like in CS2).`, ok: "Scrape Off", danger: true })) {
      save(current.slot, { wear: 1 });
      setSel(null);
    }
  };

  return html`<div class="picker sticker-editor">
    <div class="picker-head"><h2>Stickers · ${v.weapon} | ${v.finish}</h2>
      <span class="faint small">Drag a sticker to move it. The preview is a guide: check it in game (inspect with F) - equipped guns change within a second.</span></div>
    <div class="sticker-layout">
      <div class="sticker-stage" ref=${box} onPointerMove=${move} onPointerUp=${up} onPointerLeave=${up}>
        <${Pic} src=${gun.image} size=${1024} />
        ${stickers.map(st => {
          const x = drag?.slot === st.slot ? drag.x : st.offsetX, y = drag?.slot === st.slot ? drag.y : st.offsetY;
          return html`<div key=${st.slot} class=${"placed" + (sel === st.slot ? " selected" : "")} onPointerDown=${e => down(e, st.slot)}
            style=${`left:${50 + x * 45}%;top:${50 - y * 40}%;width:${64 * st.scale}px;transform:translate(-50%,-50%) rotate(${st.rotation}deg);opacity:${1 - st.wear * 0.75}`}>
            <${Pic} src=${st.image} size=${128} /></div>`;
        })}
      </div>
      <div class="sticker-side">
        <div class="field-label">On this gun (${stickers.length}/5)</div>
        <div class="sticker-slots">
          ${stickers.map(st => html`<button key=${st.slot} class=${"slot" + (sel === st.slot ? " selected" : "")} onClick=${() => setSel(st.slot)} title=${st.name}>
            <${Pic} src=${st.image} size=${128} /></button>`)}
          ${stickers.length === 0 && html`<div class="faint small">No stickers yet. Pick one below.</div>`}
        </div>
        ${current && html`<div class="sticker-controls">
          <div class="inspect-row"><span>${current.name}</span></div>
          <div class="inspect-row"><span>Rotation</span><b class="mono">${Math.round(current.rotation)}°</b></div>
          <${Slider} min=${-180} max=${180} step=${1} value=${current.rotation} onChange=${r => setLocal(l => ({ ...l, [current.slot]: { ...(l[current.slot] ?? {}), rotation: r } }))}
            onCommit=${r => save(current.slot, { rotation: r })} format=${r => `${Math.round(r)}°`} />
          <div class="inspect-row"><span>Size</span><b class="mono">${current.scale.toFixed(2)}×</b></div>
          <${Slider} min=${0.5} max=${2} step=${0.01} value=${current.scale} onChange=${r => setLocal(l => ({ ...l, [current.slot]: { ...(l[current.slot] ?? {}), scale: r } }))}
            onCommit=${r => save(current.slot, { scale: r })} format=${r => `${r.toFixed(2)}×`} />
          <div class="inspect-row"><span>Scrape</span><b class="mono">${Math.round(current.wear * 100)}%</b></div>
          <${Slider} min=${0} max=${0.95} step=${0.01} value=${current.wear} onChange=${r => setLocal(l => ({ ...l, [current.slot]: { ...(l[current.slot] ?? {}), wear: r } }))}
            onCommit=${r => save(current.slot, { wear: r })} format=${r => `${Math.round(r * 100)}%`} />
          <div class="hstack">
            <${Button} size="sm" onClick=${() => save(current.slot, { offsetX: 0, offsetY: 0, rotation: 0, scale: 1 })}>Reset Position<//>
            <${Button} size="sm" kind="destructive" onClick=${scrape}>Scrape Off<//>
          </div>
        </div>`}
        <div class="field-label">Your stickers</div>
        <div class="sticker-choices scroll">
          ${!choices ? html`<div class="faint small">Loading…</div>`
            : choices.length === 0 ? html`<div class="faint small">No stickers in your inventory. Open sticker capsules on the Cases page.</div>`
            : choices.map(c => html`<button key=${c.uid} class="slot" style=${`--c:${c.color}`} disabled=${stickers.length >= 5}
                title=${`Apply ${c.name}`} onClick=${() => apply(c.uid)}><${Pic} src=${c.image} size=${128} /></button>`)}
        </div>
      </div>
    </div>
    <div class="picker-foot"><div class="grow"></div><${Button} kind="prominent" onClick=${() => close(undefined)}>Done<//></div>
  </div>`;
}

const KINDS = [{ value: "skin", label: "Skins" }, { value: "sticker", label: "Stickers" }, { value: "agent", label: "Agents" },
  { value: "pin", label: "Pins" }, { value: "music", label: "Music Kits" }];

/** Admin: search any item and put it in the inventory, with the float, pattern and StatTrak you pick. */
function AddItemSheet({ close }) {
  const [kind, setKind] = useState("skin");
  const [q, setQ] = useState("");
  const [list, setList] = useState(null);
  const [pick, setPick] = useState(null);
  const [wear, setWear] = useState(0.01);
  const [seed, setSeed] = useState(0);
  const [st, setSt] = useState(false);
  const [sv, setSv] = useState(false);
  useEffect(() => {
    const t = setTimeout(() => call("inventory.catalog", { kind, search: q }).then(setList, () => setList([])), 180);
    return () => clearTimeout(t);
  }, [kind, q]);
  const choose = c => { setPick(c); setWear(c.paint ? Math.max(c.min, Math.min(c.max, 0.01)) : 0); setSeed(0); setSt(false); setSv(false); };
  const add = async () => {
    const v = await call("inventory.add", { kind: pick.kind, key: pick.key, wear, seed, statTrak: st, souvenir: sv }).catch(() => null);
    if (v) close(v.item.uid);
  };
  return html`<div class="picker add-item">
    <div class="picker-head"><h2>Add Item</h2><span class="faint small">Admin: any item, straight into your inventory.</span></div>
    <div class="hstack picker-filters">
      <${Segmented} options=${KINDS} value=${kind} onChange=${v => { setKind(v); setPick(null); }} />
      <div class="grow"><${TextField} icon="search" placeholder="Search: ak redline, karambit fade, sticker…" value=${q} onInput=${setQ} autofocus /></div>
    </div>
    ${pick ? html`<div class="add-detail" style=${`--c:${pick.color}`}>
        <div class="inspect-pic"><${Pic} src=${pick.image} size=${512} /></div>
        <div class="add-form">
          <div class="inspect-grade">${pick.grade}</div>
          <h2 class="inspect-finish">${pick.name}</h2>
          ${pick.kind === "skin" && pick.paint > 0 && html`
            <div class="inspect-row"><span>Float · ${wearName(wear)}</span><b class="mono">${wear.toFixed(6)}</b></div>
            <${Slider} min=${pick.min} max=${pick.max} step=${0.0001} value=${wear} onChange=${setWear} format=${v => v.toFixed(4)} />
            <${WearBar} wear=${wear} />
            <div class="inspect-row"><span>Pattern</span><${Stepper} value=${seed} min=${0} max=${1000} onChange=${setSeed} /></div>`}
          ${pick.kind === "skin" && html`
            <div class="inspect-row"><span>StatTrak™</span><${Toggle} value=${st} onChange=${v => { setSt(v); if (v) setSv(false); }} /></div>
            ${!pick.name.startsWith("★") && html`<div class="inspect-row"><span>Souvenir</span><${Toggle} value=${sv} onChange=${v => { setSv(v); if (v) setSt(false); }} /></div>`}`}
          <div class="hstack" style="margin-top:12px">
            <${Button} onClick=${() => setPick(null)}>Back<//>
            <${Button} kind="prominent" icon="plus" onClick=${add}>Add to Inventory<//>
          </div>
        </div>
      </div>`
      : html`<div class="picker-grid scroll">
        ${list == null ? html`<div class="empty small">Loading…</div>` : list.length === 0 ? html`<div class="empty small">Nothing found.</div>`
          : list.map(c => html`<button key=${c.key} class="cs-item" style=${`--c:${c.color}`} onClick=${() => choose(c)} title=${c.name}>
              <div class="pic"><${Pic} src=${c.image} size=${256} /></div>
              <div class="text"><div class="fin">${c.name}</div><div class="weapon">${c.grade}${c.team ? ` · ${c.team}` : ""}</div></div>
            </button>`)}
      </div>`}
    <div class="picker-foot"><div class="grow"></div><${Button} onClick=${() => close(undefined)}>Close<//></div>
  </div>`;
}

export function InventoryPage() {
  const [state] = useApi("cases.inventory");
  const [tab, setTab] = useState("all");
  const [sort, setSort] = useState("newest");
  const [q, setQ] = useState("");
  const [open, setOpen] = useState(null);
  if (!state) return html`<${Page} title="Inventory" />`;

  const cur = state.currency;
  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  let items = state.items.filter(v => (tab === "all" || v.category === tab)
    && words.every(w => `${v.item.name} ${v.item.caseName} ${v.item.grade}`.toLowerCase().includes(w)));
  const by = {
    newest: (a, b) => new Date(b.item.opened) - new Date(a.item.opened),
    rarity: (a, b) => GRADE_RANK.indexOf(b.item.grade) - GRADE_RANK.indexOf(a.item.grade) || (b.price ?? 0) - (a.price ?? 0),
    price: (a, b) => (b.price ?? -1) - (a.price ?? -1),
    name: (a, b) => a.item.name.localeCompare(b.item.name),
  };
  items = [...items].sort(by[sort]);
  const selected = state.items.find(v => v.item.uid === open);
  const addItem = async () => {
    const added = await sheet(close => html`<${AddItemSheet} close=${close} />`, { wide: true });
    if (added) setOpen(added);
  };

  return html`<${Page} title="Inventory" class="inventory-page"
      subtitle=${`${state.items.length} items · worth ${money(state.total, cur)}`}
      actions=${html`
        <${Button} icon="key" onClick=${() => navigate("cases")}>Open Cases<//>
        ${state.admin && html`<${Button} kind="prominent" icon="plus" onClick=${addItem} title="Admin: put any item in your inventory">Add Item<//>`}`}>
    <div class="hstack inv-bar">
      <${Segmented} options=${[{ value: "all", label: "All Items" }, { value: "weapons", label: "Weapons" }, { value: "special", label: "Knives & Gloves" },
        { value: "graphics", label: "Stickers & Graffiti" }, { value: "other", label: "Collectibles" }]} value=${tab} onChange=${setTab} />
      <div class="grow"><${TextField} icon="search" placeholder="Search inventory" value=${q} onInput=${setQ} /></div>
      <${Select} width=${150} value=${sort} onChange=${setSort}
        options=${[{ value: "newest", label: "Newest" }, { value: "rarity", label: "Rarity" }, { value: "price", label: "Price" }, { value: "name", label: "Name" }]} />
    </div>
    <div class=${"inv-layout" + (selected ? " with-inspect" : "")}>
      ${items.length === 0
        ? html`<div class="empty"><${Icon} name="key" size=${32} /><div>${state.items.length ? "Nothing matches." : "Your inventory is empty. Open cases to get skins."}</div>
            ${!state.items.length && html`<div style="margin-top:12px"><${Button} kind="prominent" onClick=${() => navigate("cases")}>Open Cases<//></div>`}</div>`
        : html`<div class="cs-grid">${items.map(v => html`<button key=${v.item.uid} class=${"cs-item" + (open === v.item.uid ? " selected" : "")}
            style=${`--c:${v.item.color}`} onClick=${() => setOpen(open === v.item.uid ? null : v.item.uid)}>
          <div class="pic"><${Pic} src=${v.item.image} size=${256} /></div>
          ${v.equipped && html`<span class="equipped" title="Equipped"><${Icon} name="check" size=${12} stroke=${3} /></span>`}
          ${v.item.statTrak && v.item.kind === "skin" && html`<span class="st">ST™</span>`}
          ${v.item.souvenir && html`<span class="st sv">SV</span>`}
          <div class="text">
            <div class="weapon">${v.weapon}</div>
            <div class="fin">${v.finish}</div>
          </div>
          <div class="price">${money(v.price, cur)}</div>
        </button>`)}</div>`}
      ${selected && html`<${Inspect} v=${selected} cur=${cur} close=${() => setOpen(null)} />`}
    </div>
  <//>`;
}
