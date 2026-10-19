// Inventory: what you unboxed, laid out like CS2's inventory. Click an item to inspect it, Equip puts it on.
import { html, useState, useEffect, useRef, useMemo } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, navigate, img } from "../api.js";
import { Page, Button, Segmented, Select, TextField, Icon, ItemPic, Slider, Stepper, Toggle, confirm, sheet } from "../ui.js";

export const wearName = w => w < 0.07 ? "Factory New" : w < 0.15 ? "Minimal Wear" : w < 0.38 ? "Field-Tested" : w < 0.45 ? "Well-Worn" : "Battle-Scarred";
const SYMBOL = { USD: "$", EUR: "€", GBP: "£" };
export const money = (v, cur) => v == null ? "—" : cur === "EUR" ? `${v.toFixed(2).replace(".", ",")}€` : `${SYMBOL[cur] ?? "$"}${v.toFixed(2)}`;
const GRADE_RANK = ["Consumer Grade", "Base Grade", "Industrial Grade", "Mil-Spec Grade", "High Grade", "Restricted", "Remarkable", "Classified", "Exotic", "Covert", "Extraordinary", "Contraband"];
/** Everything a search can hit: name, wear, grade, case, StatTrak / Souvenir. Each typed word must match. */
// Worked out once per item (the page re-filters tens of thousands of items on every keystroke).
const cached = fn => { const memo = new WeakMap(); return v => { let x = memo.get(v); if (x === undefined) memo.set(v, x = fn(v)); return x; }; };
const haystack = cached(v => `${v.item.statTrak ? "stattrak st " : ""}${v.item.souvenir ? "souvenir " : ""}${v.item.name} ${v.item.kind === "skin" && v.item.paint > 0 ? wearName(v.item.wear) : ""} ${v.item.grade} ${v.item.caseName} ${v.item.phase ?? ""}`.toLowerCase());
const openedAt = cached(v => Date.parse(v.item.opened));
const searchWords = q => q.toLowerCase().split(/\s+/).filter(Boolean);
const WEARS = ["Factory New", "Minimal Wear", "Field-Tested", "Well-Worn", "Battle-Scarred"];
// Big inventories (free cases add up fast): draw cards a page at a time, drawing thousands at once takes seconds.
const PAGE = 120;
const More = ({ shown, total, onMore }) => total > shown
  && html`<div class="show-more"><${Button} onClick=${onMore}>Show more · ${shown} of ${total}<//></div>`;
const equippable = v => (v.item.kind === "skin" && v.item.def) || ((v.item.kind === "pin" || v.item.kind === "music") && v.item.collectibleId);

/** CS2's wear bar: the five wear bands with a marker at the float. */
export function WearBar({ wear }) {
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
    <div class="inspect-pic"><${ItemPic} src=${i.image} size=${512} /></div>
    <div class="inspect-grade">${i.grade}${i.phase ? ` · ${i.phase}` : ""}</div>
    <div class="inspect-weapon">${st ? html`<span class="st-tag">StatTrak™</span> ` : ""}${i.souvenir ? html`<span class="sv-tag">Souvenir</span> ` : ""}${v.weapon}</div>
    <h2 class="inspect-finish">${v.finish}</h2>
    ${i.kind === "skin" && i.paint > 0 && html`
      <div class="inspect-row"><span>Exterior</span><b>${wearName(i.wear)}</b></div>
      <div class="inspect-row"><span>Float</span><b class="mono">${i.wear.toFixed(14)}</b></div>
      <${WearBar} wear=${i.wear} />
      <div class="inspect-row"><span>Pattern</span><b class="mono">${i.seed}</b></div>`}
    ${st && html`<div class="inspect-row"><span>StatTrak™ kills</span><b class="mono">0</b></div>`}
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
// Each slot has its own spot on the gun (offset 0 = that spot), so stickers don't pile up in the middle.
// Short guns have their slots close together, long ones (snipers) far apart.
const PISTOLS = [1, 2, 3, 4, 30, 32, 36, 61, 63, 64], LONG = [9, 11, 38, 40];
const slotX = (def, slot) => [-2, -1, 0, 1, 2][slot] * (PISTOLS.includes(def) ? 0.07 : LONG.includes(def) ? 0.19 : 0.15);
const spot = (def, slot, x, y) => ({ left: 50 + (slotX(def, slot) + x * 0.45) * 100, top: 50 - y * 40 });

function StickerSheet({ uid, close }) {
  const [inv] = useApi("cases.inventory");
  const [choices, reloadChoices] = useApi("inventory.stickers");
  const [sel, setSel] = useState(null);
  const [drag, setDrag] = useState(null); // { slot, x, y } while dragging
  const [local, setLocal] = useState({}); // slot -> edits not saved yet
  const [zoom, setZoom] = useState(1);
  const [crop, setCrop] = useState(null); // the gun's visible part of its picture, as fractions
  const box = useRef(), stage = useRef(), live = useRef();
  const v = inv?.items.find(x => x.item.uid === uid);
  const gun = v?.item;
  const stickers = (gun?.stickers ?? []).map(st => ({ ...st, ...(local[st.slot] ?? {}) }));
  const current = stickers.find(st => st.slot === sel);

  const save = (slot, change) => {
    const st = stickers.find(x => x.slot === slot);
    if (!st) return;
    const next = { ...st, ...change };
    setLocal(l => ({ ...l, [slot]: { ...(l[slot] ?? {}), ...change } }));
    tryCall("inventory.placeSticker", { gun: uid, slot, offsetX: next.offsetX, offsetY: next.offsetY, rotation: next.rotation, scale: next.scale, wear: next.wear })
      .then(() => setLocal(l => { const c = { ...l }; delete c[slot]; return c; }));
  };
  const clamp = n => Math.max(-1, Math.min(1, n));
  live.current = { current, drag, save };
  // Arrow keys nudge the selected sticker (Shift: bigger steps).
  useEffect(() => {
    const key = e => {
      const { current, drag, save } = live.current;
      if (!current || drag || e.target.closest?.("input, textarea")) return;
      const step = e.shiftKey ? 0.05 : 0.01;
      const d = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] }[e.key];
      if (!d) return;
      e.preventDefault();
      save(current.slot, { offsetX: clamp(current.offsetX + d[0]), offsetY: clamp(current.offsetY + d[1]) });
    };
    window.addEventListener("keydown", key);
    return () => window.removeEventListener("keydown", key);
  }, []);
  // Zooming keeps the selected sticker (or the middle of the gun) in view.
  useEffect(() => {
    const el = stage.current, st = live.current.current;
    if (!el || !gun) return;
    const at = st ? spot(gun.def, st.slot, st.offsetX, st.offsetY) : { left: 50, top: 50 };
    el.scrollLeft = el.scrollWidth * at.left / 100 - el.clientWidth / 2;
    el.scrollTop = el.scrollHeight * at.top / 100 - el.clientHeight / 2;
  }, [zoom]);
  if (!v) return html`<div class="picker"><div class="empty small">Loading…</div></div>`;

  // Steam pictures have empty space around the gun: find the non-transparent box so the gun fills the stage.
  const fit = e => {
    const im = e.target, w = im.naturalWidth, h = im.naturalHeight, c = document.createElement("canvas");
    c.width = w; c.height = h;
    const g = c.getContext("2d");
    g.drawImage(im, 0, 0);
    const a = g.getImageData(0, 0, w, h).data;
    let x0 = w, y0 = h, x1 = -1, y1 = -1;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (a[(y * w + x) * 4 + 3] > 16) {
      if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
    }
    if (x1 > x0 && y1 > y0) setCrop({ x: x0 / w, y: y0 / h, w: (x1 - x0 + 1) / w, h: (y1 - y0 + 1) / h, ratio: (x1 - x0 + 1) / (y1 - y0 + 1) });
  };
  const freeSlot = [0, 1, 2, 3, 4].find(n => stickers.every(st => st.slot !== n));
  const toOffset = (e, slot) => {
    const r = box.current.getBoundingClientRect();
    return { x: ((e.clientX - r.left) / r.width - 0.5 - slotX(gun.def, slot)) / 0.45, y: -((e.clientY - r.top) / r.height - 0.5) / 0.4 };
  };
  // The sticker keeps the spot you grabbed it at (dx, dy) instead of jumping under the cursor.
  const down = (e, slot) => {
    e.preventDefault(); e.stopPropagation(); setSel(slot);
    const st = stickers.find(x => x.slot === slot), p = toOffset(e, slot);
    setDrag({ slot, x: st.offsetX, y: st.offsetY, dx: st.offsetX - p.x, dy: st.offsetY - p.y });
    e.currentTarget.setPointerCapture?.(e.pointerId);
  };
  const move = e => {
    if (drag) { const p = toOffset(e, drag.slot); setDrag({ ...drag, x: clamp(p.x + drag.dx), y: clamp(p.y + drag.dy) }); }
  };
  const up = () => { if (drag) { save(drag.slot, { offsetX: drag.x, offsetY: drag.y }); setDrag(null); } };
  // A sticker from the inventory goes on at once (its slot's spot, selected); then drag it, or click the gun to move it there.
  const apply = async c => {
    const slot = freeSlot;
    if (slot == null) return;
    await call("inventory.applySticker", { gun: uid, sticker: c.uid, slot, offsetX: 0, offsetY: 0, rotation: 0, scale: 1 }).then(() => setSel(slot), () => {});
    reloadChoices();
  };
  const moveHere = e => {
    if (!current) return;
    const p = toOffset(e, current.slot);
    save(current.slot, { offsetX: clamp(p.x), offsetY: clamp(p.y) });
  };
  const scrape = async () => {
    if (await confirm({ title: "Scrape off?", message: `${current.name} comes off the gun for good (like in CS2).`, ok: "Scrape Off", danger: true })) {
      save(current.slot, { wear: 1 });
      setSel(null);
    }
  };
  const nudge = (dx, dy) => save(current.slot, { offsetX: clamp(current.offsetX + dx), offsetY: clamp(current.offsetY + dy) });

  return html`<div class="picker sticker-editor">
    <div class="picker-head"><h2><span class="faint">${v.weapon} |</span> ${v.finish}</h2>
      <span class="faint small">Click a sticker below to put it on. Then drag it, or click on the gun to move the selected one there; arrow keys nudge. Check it in game with F (inspect): equipped guns change within a second.</span></div>
    <div class="sticker-layout">
      <div class="sticker-left">
        <div class="sticker-view">
        <div class=${"sticker-stage" + (current ? " placing" : "") + (zoom > 1 ? " zoomed" : "")} ref=${stage}>
          <div class="sticker-canvas" ref=${box} style=${`width:${zoom * Math.min(100, (crop?.ratio ?? 2.6) * 41)}%;aspect-ratio:${crop?.ratio ?? 2.6}`} onPointerMove=${move} onPointerUp=${up} onPointerLeave=${up} onClick=${moveHere}>
            <img src=${img(gun.image, 1024)} alt="" onLoad=${fit}
              style=${crop ? `width:${100 / crop.w}%;left:${-crop.x / crop.w * 100}%;top:${-crop.y / crop.h * 100}%` : "width:100%;top:50%;transform:translateY(-50%)"} />
            ${stickers.map(st => {
              const x = drag?.slot === st.slot ? drag.x : st.offsetX, y = drag?.slot === st.slot ? drag.y : st.offsetY;
              const at = spot(gun.def, st.slot, x, y);
              return html`<div key=${st.slot} class=${"placed" + (sel === st.slot ? " selected" : "")} onPointerDown=${e => down(e, st.slot)} onClick=${e => e.stopPropagation()}
                style=${`left:${at.left}%;top:${at.top}%;width:${11 * st.scale}%;transform:translate(-50%,-50%) rotate(${st.rotation}deg);opacity:${1 - st.wear * 0.75}`}>
                <${ItemPic} src=${st.image} size=${128} /></div>`;
            })}
          </div>
        </div>
        <div class="sticker-zoom">
          ${[1, 1.6, 2.4].map(z => html`<button key=${z} class=${zoom === z ? "on" : ""} onClick=${() => setZoom(z)} title="Zoom">${z}×</button>`)}
        </div>
        <div class="sticker-rail">
          ${[0, 1, 2, 3, 4].map(n => {
            const st = stickers.find(x => x.slot === n);
            return html`<button key=${n} class=${"slot" + (st ? "" : " empty") + (st && sel === n ? " selected" : "")} disabled=${!st} onClick=${() => setSel(n)} title=${st?.name ?? `Slot ${n + 1}`}>
              ${st ? html`<${ItemPic} src=${st.image} size=${128} />` : html`<span class="slot-no">${n + 1}</span>`}</button>`;
          })}
        </div>
        </div>
      </div>
      <div class="sticker-side">
        ${!current && html`<div class="faint small">${stickers.length ? "Pick a sticker on the right of the gun to edit it." : "No stickers yet. Pick one below."}</div>`}
        ${current && html`<div class="sticker-controls">
          <div class="inspect-row"><span>${current.name}</span><b class="mono">x ${current.offsetX.toFixed(2)} · y ${current.offsetY.toFixed(2)}</b></div>
          <div class="hstack nudge">
            <${Button} size="sm" onClick=${() => nudge(-0.02, 0)} title="Left (←)">←<//><${Button} size="sm" onClick=${() => nudge(0, 0.02)} title="Up (↑)">↑<//>
            <${Button} size="sm" onClick=${() => nudge(0, -0.02)} title="Down (↓)">↓<//><${Button} size="sm" onClick=${() => nudge(0.02, 0)} title="Right (→)">→<//>
          </div>
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
        <div class="field-label">Your stickers ${stickers.length >= 5 ? "· the gun is full" : "· click one to put it on"}</div>
        <div class="sticker-choices scroll">
          ${!choices ? html`<div class="faint small">Loading…</div>`
            : choices.length === 0 ? html`<div class="faint small">No stickers in your inventory. Open sticker capsules on the Cases page.</div>`
            : choices.map(c => html`<button key=${c.uid} class="slot" style=${`--c:${c.color}`} disabled=${stickers.length >= 5}
                title=${`Put on ${c.name}`} onClick=${() => apply(c)}><${ItemPic} src=${c.image} size=${128} /></button>`)}
        </div>
      </div>
    </div>
    <div class="picker-foot"><div class="grow"></div><${Button} kind="prominent" onClick=${() => close(undefined)}>Done<//></div>
  </div>`;
}

/** The contract paper's signature line: draw with the mouse, like CS2. onChange(true) once something is drawn. */
function Signature({ onChange }) {
  const ref = useRef();
  const drawing = useRef(false);
  const point = e => { const r = ref.current.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
  const down = e => {
    drawing.current = true;
    ref.current.setPointerCapture(e.pointerId);
    const g = ref.current.getContext("2d");
    g.lineWidth = 2.4; g.lineCap = g.lineJoin = "round"; g.strokeStyle = "#1d2a5a";
    g.beginPath(); g.moveTo(...point(e));
  };
  const move = e => {
    if (!drawing.current) return;
    const g = ref.current.getContext("2d");
    g.lineTo(...point(e)); g.stroke();
    onChange(true);
  };
  const clear = () => { ref.current.getContext("2d").clearRect(0, 0, 420, 90); onChange(false); };
  return html`<div class="signature">
    <canvas ref=${ref} width="420" height="90" onPointerDown=${down} onPointerMove=${move} onPointerUp=${() => { drawing.current = false; }}></canvas>
    <div class="sign-line"><span>Sign here</span><button class="link" onClick=${clear}>Clear</button></div>
  </div>`;
}

const TU_SORTS = [{ value: "float", label: "Lowest float" }, { value: "floatDesc", label: "Highest float" }, { value: "price", label: "Cheapest" },
  { value: "new", label: "Newest" }, { value: "name", label: "Name" }];
const TU_GRADES = ["Consumer Grade", "Industrial Grade", "Mil-Spec Grade", "Restricted", "Classified", "Covert"];
const TU_COLORS = ["#b0c3d9", "#5e98d9", "#4b69ff", "#8847ff", "#d32ce6", "#eb4b4b"];

/** CS2's Trade Up Contract: pick 10 guns of one grade (or 5 Coverts), submit, sign the contract, get the result. */
function TradeUpSheet({ close }) {
  const [inv] = useApi("cases.inventory");
  const [picked, setPicked] = useState([]);
  const [check, setCheck] = useState(null);
  const [phase, setPhase] = useState("pick"); // pick | paper | signing | done
  const [signed, setSigned] = useState(false);
  const [result, setResult] = useState(null);
  const [q, setQ] = useState("");
  const [sort, setSort] = useState("float");
  const [grade, setGrade] = useState("");
  const [stFilter, setStFilter] = useState("");
  const [wear, setWear] = useState("");
  const [limit, setLimit] = useState(PAGE);
  useEffect(() => setLimit(PAGE), [q, sort, grade, stFilter, wear]);
  // Everything that can go in a contract at all (same for every keystroke and click until the inventory changes).
  const [tradeable, index] = useMemo(() => !inv ? [[], new Map()] : [
    inv.items.filter(v => v.category === "weapons" && v.item.paint > 0 && !v.item.souvenir && TU_GRADES.includes(v.item.grade)),
    new Map(inv.items.map(v => [v.item.uid, v]))], [inv]);
  useEffect(() => {
    let live = true; // an older, slower answer mustn't replace the newer one
    call("tradeup.check", { uids: picked }).then(c => live && setCheck(c), () => {});
    return () => { live = false; };
  }, [picked.join()]);
  if (!inv) return html`<div class="picker"><div class="empty small">Loading…</div></div>`;
  const cur = inv.currency;
  const byUid = uid => index.get(uid);
  const first = byUid(picked[0]);
  const needed = first?.item.grade === "Covert" ? 5 : 10;
  // What fits the first item: same grade and StatTrak™ kind, like CS2.
  const words = searchWords(q);
  const eligible = tradeable.filter(v => !picked.includes(v.item.uid)
      && (!first || (v.item.grade === first.item.grade && v.item.statTrak === first.item.statTrak))
      && (!grade || v.item.grade === grade) && (!stFilter || (stFilter === "st") === v.item.statTrak)
      && (!wear || wearName(v.item.wear) === wear) && words.every(w => haystack(v).includes(w)))
    .sort({
      float: (a, b) => a.item.wear - b.item.wear, floatDesc: (a, b) => b.item.wear - a.item.wear,
      price: (a, b) => (a.price ?? 1e9) - (b.price ?? 1e9), name: (a, b) => a.item.name.localeCompare(b.item.name), new: () => 0,
    }[sort]);
  // Grade / StatTrak™ chips: locked to the first item's once one is in, like CS2.
  const gradeNow = first ? first.item.grade : grade, stNow = first ? (first.item.statTrak ? "st" : "normal") : stFilter;
  const count = (g, st = stNow) => tradeable.filter(v => v.item.grade === g && (!st || (st === "st") === v.item.statTrak) && !picked.includes(v.item.uid)).length;
  const chip = (active, color, label, n, onClick, disabled) => html`<button class=${"tu-chip" + (active ? " on" : "")} style=${`--c:${color}`}
      disabled=${disabled} onClick=${onClick}><span class="dot"></span>${label}${n != null ? html`<b>${n}</b>` : ""}</button>`;
  const add = uid => setPicked(p => p.length < needed ? [...p, uid] : p);
  const remove = uid => setPicked(p => p.filter(x => x !== uid));
  // Fill the free slots from the top of the list as sorted. With nothing in yet: the first grade (and StatTrak™ kind) that has enough.
  const fillGroup = (() => {
    if (first) return eligible.slice(0, needed - picked.length);
    const groups = Object.values(Object.groupBy(eligible, v => `${v.item.grade}|${v.item.statTrak}`));
    const enough = groups.find(g => g.length >= (g[0].item.grade === "Covert" ? 5 : 10));
    return enough ? enough.slice(0, enough[0].item.grade === "Covert" ? 5 : 10) : [];
  })();
  const autoFill = () => setPicked(p => [...p, ...fillGroup.map(v => v.item.uid)]);
  const sign = async () => {
    setPhase("signing");
    const [r] = await Promise.all([call("tradeup.sign", { uids: picked }).catch(() => null), new Promise(ok => setTimeout(ok, 1600))]);
    if (!r) { setSigned(false); setPhase("pick"); return; }
    setResult(r); setPicked([]); setSigned(false); setPhase("done");
  };
  const Card = (v, onClick, title) => html`<button key=${v.item.uid} class="cs-item" style=${`--c:${v.item.color}`} onClick=${onClick} title=${title}>
    <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
    ${v.item.statTrak && html`<span class="tu-st">ST™</span>`}
    <div class="text"><div class="weapon">${v.weapon}</div><div class="fin">${v.finish}</div></div>
    <div class="price">${v.item.wear.toFixed(4)}${v.price != null ? html` · <span class="tu-price">${money(v.price, cur)}</span>` : ""}</div></button>`;
  const gain = check?.expectedValue != null && check?.inputValue != null ? check.expectedValue - check.inputValue : null;

  if (phase === "signing") return html`<div class="picker tradeup"><div class="tradeup-signing"><div class="pen">✍</div><h2>Signing the contract…</h2></div></div>`;
  if (phase === "done") {
    const v = result.item, i = v.item;
    return html`<div class="picker tradeup"><div class="reveal" style=${`--c:${i.color}`}>
      <div class="reveal-glow"></div>
      <div class="reveal-pic"><${ItemPic} src=${i.image} size=${512} /></div>
      <div class="reveal-grade">${i.grade}${i.phase ? ` · ${i.phase}` : ""}</div>
      <h2 class="reveal-name">${i.statTrak ? "StatTrak™ " : ""}${i.name}</h2>
      ${i.paint > 0 && html`<div class="reveal-stats mono"><span>${wearName(i.wear)}</span><span>Float ${i.wear.toFixed(9)}</span><span>Pattern ${i.seed}</span></div>`}
      <div class="reveal-price">${money(v.price, cur)}</div>
      <div class="hstack reveal-actions">
        <${Button} kind="prominent" icon="check" onClick=${() => tryCall("cases.equip", i.uid)}>Equip<//>
        <${Button} icon="refresh" onClick=${() => setPhase("pick")}>Another Contract<//>
        <${Button} kind="plain" onClick=${() => close(i.uid)}>Done<//>
      </div>
    </div></div>`;
  }
  if (phase === "paper") {
    const items = picked.map(byUid).filter(Boolean);
    return html`<div class="picker tradeup"><div class="contract-paper">
      <h2>Trade Up Contract</h2>
      <p>I, the undersigned, hereby exchange the ${items.length} ${first?.item.grade ?? ""}${first?.item.statTrak ? " StatTrak™" : ""} items below for one item
        of the next grade from their collections. This contract cannot be undone: the items listed are gone once it is signed.</p>
      <ol class="contract-items">${items.map(v => html`<li>${v.item.statTrak ? "StatTrak™ " : ""}${v.item.name}<span>${wearName(v.item.wear)} · ${v.item.wear.toFixed(6)}</span></li>`)}</ol>
      <${Signature} onChange=${setSigned} />
      <div class="hstack contract-buttons">
        <${Button} onClick=${() => { setSigned(false); setPhase("pick"); }}>Back<//>
        <div class="grow"></div>
        <${Button} kind="prominent" size="lg" icon="check" disabled=${!signed} onClick=${sign}>Sign Contract<//>
      </div>
    </div></div>`;
  }
  return html`<div class="picker tradeup">
    <div class="picker-head"><h2>Trade Up Contract</h2>
      <span class="faint small">Exchange 10 items of the same grade for one item of the next grade (5 Covert items for a knife or gloves).
        All StatTrak™ or none, no souvenirs. The result comes from the collections of the items you put in.</span></div>
    <div class="tradeup-layout">
      <div class="tradeup-left">
        <div class="hstack tradeup-tools">
          <div class="grow"><${TextField} icon="search" placeholder="Search: ak, redline, field-tested, stattrak…" value=${q} onInput=${setQ} /></div>
          <${Select} width=${150} value=${sort} onChange=${setSort} options=${TU_SORTS} />
        </div>
        <div class="tu-chips">
          ${chip(!gradeNow, "var(--text-3)", "All grades", null, () => setGrade(""), !!first)}
          ${TU_GRADES.map((g, n) => chip(gradeNow === g, TU_COLORS[n], g.replace(" Grade", ""), count(g), () => setGrade(gradeNow === g ? "" : g), !!first || (!count(g) && gradeNow !== g)))}
        </div>
        <div class="tu-chips">
          ${chip(!stNow, "var(--text-3)", "Normal + StatTrak™", null, () => setStFilter(""), !!first)}
          ${chip(stNow === "normal", "var(--text-2)", "Normal", null, () => setStFilter("normal"), !!first)}
          ${chip(stNow === "st", "#cf6a32", "StatTrak™", null, () => setStFilter("st"), !!first)}
          <div class="grow"></div>
          <${Select} width=${150} value=${wear} onChange=${setWear} options=${[{ value: "", label: "Any exterior" }, ...WEARS.map(w => ({ value: w, label: w }))]} />
        </div>
        <div class="field-label">Your items · ${eligible.length}${first ? ` · ${first.item.grade}${first.item.statTrak ? " · StatTrak™" : ""}` : " · click to add"}</div>
        <div class="tradeup-pool scroll">
          ${eligible.length === 0 ? html`<div class="faint small">No items that can go in${q || grade || wear || stFilter ? " with these filters" : ""}. Open cases for more.</div>`
            : html`${eligible.slice(0, limit).map(v => Card(v, () => add(v.item.uid), "Add to the contract"))}
              <${More} shown=${Math.min(limit, eligible.length)} total=${eligible.length} onMore=${() => setLimit(l => l + PAGE)} />`}
        </div>
      </div>
      <div class="tradeup-right">
        <div class="hstack"><div class="field-label grow">Contract · ${picked.length}/${needed}</div>
          <${Button} size="sm" icon="sparkles" disabled=${picked.length >= needed || !fillGroup.length} onClick=${autoFill}
            title="Fill the free slots from the top of your list (as sorted and filtered)">Auto-fill<//></div>
        <div class="tradeup-slots">
          ${Array.from({ length: needed }, (_, n) => {
            const v = byUid(picked[n]);
            return v ? Card(v, () => remove(v.item.uid), "Take out") : html`<div class="tradeup-empty">${n + 1}</div>`;
          })}
        </div>
        <div class="field-label">Possible outcomes</div>
        <div class="tradeup-outcomes scroll">
          ${(check?.outcomes ?? []).length === 0 ? html`<div class="faint small">${check?.message ?? ""}</div>`
            : check.outcomes.map(o => html`<div class="outcome" style=${`--c:${o.color}`}>
                <div class="pic"><${ItemPic} src=${o.image} size=${128} /></div>
                <div class="grow"><div class="fin">${o.name}</div>
                  <div class="faint small">${o.wear ? `${o.wear} · ${o.float.toFixed(6)} · ` : ""}${o.collection}</div></div>
                <div class="odds"><b>${(o.chance * 100).toFixed(o.chance < 0.1 ? 2 : 1)}%</b><span>${money(o.price, cur)}</span></div>
              </div>`)}
        </div>
        <div class="tradeup-sum">
          <div class="inspect-row"><span>Put in</span><b>${money(check?.inputValue, cur)}</b></div>
          <div class="inspect-row"><span>Average result</span><b>${money(check?.expectedValue, cur)}${gain != null
            ? html` <span class=${gain >= 0 ? "gain" : "drop"}>(${gain >= 0 ? "+" : "−"}${money(Math.abs(gain), cur)})</span>` : ""}</b></div>
          ${check && !check.ok && picked.length > 0 && html`<div class="small note-warn">${check.message}</div>`}
          <div class="hstack">
            <${Button} kind="plain" disabled=${!picked.length} onClick=${() => setPicked([])}>Clear<//>
            <div class="grow"></div>
            <${Button} kind="prominent" size="lg" icon="check" disabled=${!check?.ok} onClick=${() => setPhase("paper")}>Submit Contract<//>
          </div>
        </div>
      </div>
    </div>
    <div class="picker-foot"><div class="grow"></div><${Button} onClick=${() => close(undefined)}>Close<//></div>
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
    let live = true; // an older search answering late mustn't replace the list
    const t = setTimeout(() => call("inventory.catalog", { kind, search: q }).then(l => live && setList(l), () => live && setList([])), 180);
    return () => { live = false; clearTimeout(t); };
  }, [kind, q]);
  const choose = c => { setPick(c); setWear(c.paint ? Math.max(c.min, Math.min(c.max, 0.01)) : 0); setSeed(0); setSt(false); setSv(false); };
  const [adding, setAdding] = useState(false);
  const add = async () => {
    if (adding) return; // a double click adds once
    setAdding(true);
    const v = await call("inventory.add", { kind: pick.kind, key: pick.key, wear, seed, statTrak: st, souvenir: sv }).catch(() => null);
    setAdding(false);
    if (v) close(v.item.uid);
  };
  return html`<div class="picker add-item">
    <div class="picker-head"><h2>Add Item</h2><span class="faint small">Admin: any item, straight into your inventory.</span></div>
    <div class="hstack picker-filters">
      <${Segmented} options=${KINDS} value=${kind} onChange=${v => { setKind(v); setPick(null); }} />
      <div class="grow"><${TextField} icon="search" placeholder="Search: ak redline, karambit fade, sticker…" value=${q} onInput=${setQ} autofocus /></div>
    </div>
    ${pick ? html`<div class="add-detail" style=${`--c:${pick.color}`}>
        <div class="inspect-pic"><${ItemPic} src=${pick.image} size=${512} /></div>
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
            <${Button} kind="prominent" icon="plus" busy=${adding} onClick=${add}>Add to Inventory<//>
          </div>
        </div>
      </div>`
      : html`<div class="picker-grid scroll">
        ${list == null ? html`<div class="empty small">Loading…</div>` : list.length === 0 ? html`<div class="empty small">Nothing found.</div>`
          : list.map(c => html`<button key=${c.key} class="cs-item" style=${`--c:${c.color}`} onClick=${() => choose(c)} title=${c.name}>
              <div class="pic"><${ItemPic} src=${c.image} size=${256} /></div>
              <div class="text"><div class="fin">${c.name}</div><div class="weapon">${c.grade}${c.team ? ` · ${c.team}` : ""}</div></div>
            </button>`)}
      </div>`}
    <div class="picker-foot"><div class="grow"></div><${Button} onClick=${() => close(undefined)}>Close<//></div>
  </div>`;
}

// CS2's loadout screen: the weapons of each side by group, with the skin you have on. Team: 0 both, 2 T, 3 CT.
const LOADOUT = [
  { name: "Pistols", guns: [[4, "Glock-18", 2], [61, "USP-S", 3], [32, "P2000", 3], [2, "Dual Berettas", 0], [36, "P250", 0], [3, "Five-SeveN", 3], [30, "Tec-9", 2], [63, "CZ75-Auto", 0], [1, "Desert Eagle", 0], [64, "R8 Revolver", 0]] },
  { name: "SMGs", guns: [[17, "MAC-10", 2], [34, "MP9", 3], [33, "MP7", 0], [23, "MP5-SD", 0], [24, "UMP-45", 0], [19, "P90", 0], [26, "PP-Bizon", 0]] },
  { name: "Heavy", guns: [[35, "Nova", 0], [25, "XM1014", 0], [29, "Sawed-Off", 2], [27, "MAG-7", 3], [14, "M249", 0], [28, "Negev", 0]] },
  { name: "Rifles", guns: [[7, "AK-47", 2], [16, "M4A4", 3], [60, "M4A1-S", 3], [13, "Galil AR", 2], [10, "FAMAS", 3], [39, "SG 553", 2], [8, "AUG", 3], [40, "SSG 08", 0], [9, "AWP", 0], [11, "G3SG1", 2], [38, "SCAR-20", 3]] },
];
const isGlove = v => /gloves|wraps/i.test(v.weapon);

// CS2's loadout screen: pick the side, then a slot (character column on the left, weapons in the middle), then one
// of your items below for it. Guns, knife and gloves can differ per side; agent, music kit and pin are per item.
const COLUMNS = [
  { name: "Pistols", groups: ["Pistols"] },
  { name: "Mid-Tier", groups: ["SMGs", "Heavy"] },
  { name: "Rifles", groups: ["Rifles"] },
];
// CS:GO's skin plugins (kgns) do knives and gloves; no agents, music kits or pins.
const GEAR = [{ key: "gloves", name: "Gloves", icon: "skins" }, { key: "knife", name: "Knife", icon: "crosshair" }];

function LoadoutView({ items, cur }) {
  const [team, setTeam] = useState(3);
  const [sel, setSel] = useState("knife");
  const [pics] = useApi("inventory.weaponPics");
  const side = team === 2 ? "T" : "CT";
  const skins = items.filter(v => v.item.kind === "skin");
  const special = skins.filter(v => v.category === "special");
  // Every slot: what goes in it and what this side wears now.
  const mineFor = key => key === "knife" ? special.filter(v => !isGlove(v)) : key === "gloves" ? special.filter(isGlove)
    : key === "agent" ? items.filter(v => v.item.kind === "agent" && v.item.team === side)
    : key === "music" || key === "pin" ? items.filter(v => v.item.kind === key)
    : skins.filter(v => v.item.def === key);
  // kgns: one skin per gun and one knife for both sides; gloves per side.
  const perSide = key => key === "gloves";
  const worn = (key, v) => perSide(key) ? (team === 2 ? v.equippedT : v.equippedCT) : v.equipped;
  const onNow = key => mineFor(key).find(v => worn(key, v));
  const equip = v => perSide(sel) ? tryCall("cases.equipSide", { uid: v.item.uid, team }) : tryCall("cases.equip", v.item.uid);
  const useDefault = on => perSide(sel) ? tryCall("cases.unequipSide", { uid: on.item.uid, team }) : tryCall("cases.unequip", on.item.uid);

  const slotTile = (key, name, defaultPic, icon) => {
    const on = onNow(key);
    return html`<button key=${key} class=${"lo-slot" + (sel === key ? " active" : "") + (on ? " worn" : "")} style=${`--c:${on?.item.color ?? "transparent"}`}
        onClick=${() => { setSel(key); setTimeout(() => document.querySelector(".lo-pick")?.scrollIntoView({ behavior: "smooth", block: "nearest" }), 50); }} title=${on ? `${name} | ${on.finish}` : name}>
      <div class="pic">${on ? html`<${ItemPic} src=${on.item.image} size=${256} />` : defaultPic ? html`<span class="default"><${ItemPic} src=${defaultPic} size=${256} /></span>` : html`<${Icon} name=${icon ?? "crosshair"} size=${26} />`}</div>
      <div class="lo-name">${name}</div>
      <div class="lo-fin">${on ? on.finish : "Default"}</div>
    </button>`;
  };
  // A gun the other side can't buy (AK on CT...) isn't a slot there: back to the knife.
  const switchTo = t => {
    const gun = typeof sel === "number" && LOADOUT.flatMap(g => g.guns).find(([d]) => d === sel);
    if (gun && gun[2] !== 0 && gun[2] !== t) setSel("knife");
    setTeam(t);
  };
  const guns = g => LOADOUT.find(x => x.name === g).guns.filter(([, , t]) => t === 0 || t === team);
  const mine = mineFor(sel);
  const on = onNow(sel);
  const selName = typeof sel === "number" ? LOADOUT.flatMap(g => g.guns).find(([d]) => d === sel)?.[1] : GEAR.find(g => g.key === sel)?.name;
  const sorted = [...mine].sort((a, b) => worn(sel, b) - worn(sel, a) || GRADE_RANK.indexOf(b.item.grade) - GRADE_RANK.indexOf(a.item.grade) || a.item.wear - b.item.wear);

  return html`<div class=${"lo " + (team === 2 ? "lo-t" : "lo-ct")}>
    <div class="lo-top">
      <button class=${"lo-side" + (team === 3 ? " active ct" : "")} onClick=${() => switchTo(3)}>EQUIP CT</button>
      <button class=${"lo-side" + (team === 2 ? " active t" : "")} onClick=${() => switchTo(2)}>EQUIP T</button>
      <span class="faint small">Pick a slot, then one of your items below. In CS:GO guns and knife are the same on both sides; gloves can differ.</span>
    </div>
    <div class="lo-board">
      <div class="lo-gear">${GEAR.map(g => slotTile(g.key, g.name, null, g.icon))}</div>
      ${COLUMNS.map(c => html`<div class="lo-col" key=${c.name}>
        <div class="lo-head">${c.name}</div>
        ${c.groups.flatMap(guns).map(([def, name]) => slotTile(def, name, pics?.[def]))}
      </div>`)}
    </div>
    <div class="lo-pick">
      <div class="hstack lo-pick-head"><h3 class="grow">${selName ?? ""} · ${side}</h3>
        ${on && html`<${Button} size="sm" onClick=${() => useDefault(on)}>Use Default<//>`}</div>
      ${sorted.length === 0 ? html`<div class="empty small">None in your inventory${typeof sel === "number" ? ". Open cases to get some." : "."}</div>`
        : html`<div class="cs-grid">${sorted.slice(0, 240).map(v => html`<button key=${v.item.uid} class=${"cs-item" + (worn(sel, v) ? " selected" : "")}
            style=${`--c:${v.item.color}`} onClick=${() => !worn(sel, v) && equip(v)} title=${worn(sel, v) ? "Equipped" : `Equip for ${side}`}>
            <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
            ${worn(sel, v) && html`<span class="equipped"><${Icon} name="check" size=${12} stroke=${3} /></span>`}
            ${v.item.statTrak && v.item.kind === "skin" && html`<span class="st">ST™</span>`}
            <div class="text"><div class="weapon">${v.weapon}</div><div class="fin">${v.finish}</div></div>
            <div class="price">${v.item.kind === "skin" && v.item.paint > 0 ? wearName(v.item.wear) : money(v.price, cur)}</div>
          </button>`)}</div>`}
    </div>
  </div>`;
}

// Favorites live in this browser only: a per-PC convenience, not game data.
const readFavs = () => { try { return new Set(JSON.parse(localStorage.getItem("inv.favs") ?? "[]")); } catch { return new Set(); } };

export function InventoryPage() {
  const [state] = useApi("cases.inventory");
  const [favs, setFavs] = useState(readFavs);
  const [onlyST, setOnlyST] = useState(false);
  const [onlyFav, setOnlyFav] = useState(false);
  const [fromCase, setFromCase] = useState("");
  const [minP, setMinP] = useState("");
  const [maxP, setMaxP] = useState("");
  const [tab, setTab] = useState("all");
  const [grade, setGrade] = useState("");
  const [wear, setWear] = useState("");
  const [sort, setSort] = useState("newest");
  const [q, setQ] = useState("");
  const [open, setOpen] = useState(null);
  const [limit, setLimit] = useState(PAGE);
  useEffect(() => setLimit(PAGE), [q, tab, sort, grade, wear, fromCase, onlyST, onlyFav, minP, maxP]);
  // Copies of the same skin in the same exterior (and StatTrak™ / Souvenir): the lowest float stays, and anything
  // equipped, favorite or with stickers. Other exteriors are different items, so they all stay.
  const duplicates = useMemo(() => !state ? [] : Object.values(Object.groupBy(state.items.filter(v => v.item.kind === "skin"),
      v => `${v.item.name}|${v.item.statTrak}|${v.item.souvenir}|${v.item.paint > 0 ? wearName(v.item.wear) : ""}|${v.item.phase ?? ""}`))
    .flatMap(g => g.length < 2 ? [] : [...g].sort((a, b) => a.item.wear - b.item.wear).slice(1))
    .filter(v => !v.equipped && !favs.has(v.item.uid) && !(v.item.stickers ?? []).length), [state, favs]);
  if (!state) return html`<${Page} title="Inventory" />`;

  const cur = state.currency;
  const words = searchWords(q);
  const toggleFav = uid => setFavs(f => {
    const n = new Set(f); n.has(uid) ? n.delete(uid) : n.add(uid);
    try { localStorage.setItem("inv.favs", JSON.stringify([...n])); } catch { /* no storage */ }
    return n;
  });
  const lo = parseFloat(minP), hi = parseFloat(maxP);
  let items = state.items.filter(v => (tab === "all" || tab === "loadout" || v.category === tab)
    && (!onlyST || v.item.statTrak) && (!onlyFav || favs.has(v.item.uid)) && (!fromCase || v.item.caseName === fromCase)
    && (isNaN(lo) || (v.price ?? 0) >= lo) && (isNaN(hi) || (v.price ?? 0) <= hi)
    && (!grade || v.item.grade === grade) && (!wear || (v.item.kind === "skin" && v.item.paint > 0 && wearName(v.item.wear) === wear))
    && words.every(w => haystack(v).includes(w)));
  const filtered = q || grade || wear || fromCase || onlyST || onlyFav || minP || maxP;
  const clearFilters = () => { setQ(""); setGrade(""); setWear(""); setFromCase(""); setOnlyST(false); setOnlyFav(false); setMinP(""); setMaxP(""); };
  const grades = GRADE_RANK.filter(g => state.items.some(v => v.item.grade === g)).reverse();
  const by = {
    newest: (a, b) => openedAt(b) - openedAt(a),
    rarity: (a, b) => GRADE_RANK.indexOf(b.item.grade) - GRADE_RANK.indexOf(a.item.grade) || (b.price ?? 0) - (a.price ?? 0),
    price: (a, b) => (b.price ?? -1) - (a.price ?? -1),
    name: (a, b) => a.item.name.localeCompare(b.item.name),
  };
  items = [...items].sort((a, b) => favs.has(b.item.uid) - favs.has(a.item.uid) || by[sort](a, b)); // favorites first
  const cases = [...new Set(state.items.map(v => v.item.caseName).filter(Boolean))].sort();
  const removeDuplicates = async () => {
    if (await confirm({ title: "Remove duplicates?", message: `Remove ${duplicates.length} extra copies?\nOne copy of each skin per exterior stays (the lowest float), and equipped and favorite items and guns with stickers. Undo Remove brings them back.`, ok: "Remove", danger: true }))
      tryCall("cases.removeMany", duplicates.map(v => v.item.uid));
  };
  const selected = state.items.find(v => v.item.uid === open);
  const tradeUp = async () => {
    const got = await sheet(close => html`<${TradeUpSheet} close=${close} />`, { wide: true });
    if (got) setOpen(got);
  };
  const addItem = async () => {
    const added = await sheet(close => html`<${AddItemSheet} close=${close} />`, { wide: true });
    if (added) setOpen(added);
  };

  return html`<${Page} title="Inventory" class="inventory-page"
      subtitle=${`${state.items.length} items · worth ${money(state.total, cur)}`}
      actions=${html`
        <${Button} icon="key" onClick=${() => navigate("cases")}>Open Cases<//>
        <${Button} icon="layers" onClick=${tradeUp}>Trade Up<//>
        ${state.undo > 0 && html`<${Button} icon="refresh" onClick=${() => tryCall("cases.undoRemove")} title="Bring back what Remove Duplicates removed last">Undo Remove (${state.undo})<//>`}
        <${Button} icon="trash" disabled=${!duplicates.length} onClick=${removeDuplicates} title="Remove extra copies of the same skin in the same exterior">Remove Duplicates (${duplicates.length})<//>
        ${state.admin && html`<${Button} kind="prominent" icon="plus" onClick=${addItem} title="Admin: put any item in your inventory">Add Item<//>`}`}>
    <div class="hstack inv-bar">
      <${Segmented} options=${[{ value: "all", label: "All Items" }, { value: "loadout", label: "Loadout" }, { value: "weapons", label: "Weapons" }, { value: "special", label: "Knives & Gloves" },
        { value: "graphics", label: "Stickers & Graffiti" }, { value: "other", label: "Collectibles" }]} value=${tab} onChange=${setTab} />
      <div class="grow"><${TextField} icon="search" placeholder="Search: ak redline, karambit, factory new, stattrak…" value=${q} onInput=${setQ} /></div>
      <${Select} width=${150} value=${sort} onChange=${setSort}
        options=${[{ value: "newest", label: "Newest" }, { value: "rarity", label: "Rarity" }, { value: "price", label: "Price" }, { value: "name", label: "Name" }]} />
    </div>
    ${tab !== "loadout" && html`<div class="hstack inv-bar">
      <${Select} width=${170} value=${grade} onChange=${setGrade} options=${[{ value: "", label: "Any grade" }, ...grades.map(g => ({ value: g, label: g }))]} />
      <${Select} width=${150} value=${wear} onChange=${setWear} options=${[{ value: "", label: "Any exterior" }, ...WEARS.map(w => ({ value: w, label: w }))]} />
      <${Select} width=${190} value=${fromCase} onChange=${setFromCase} options=${[{ value: "", label: "Any case" }, ...cases.map(c => ({ value: c, label: c }))]} />
      <${Toggle} value=${onlyST} onChange=${setOnlyST} /><span class="small">StatTrak™</span>
      <${Toggle} value=${onlyFav} onChange=${setOnlyFav} /><span class="small">Favorites</span>
      <${TextField} width=${90} placeholder="Min price" value=${minP} onInput=${setMinP} />
      <${TextField} width=${90} placeholder="Max price" value=${maxP} onInput=${setMaxP} />
      ${filtered && html`<${Button} size="sm" kind="plain" icon="x" onClick=${clearFilters}>Clear filters · ${items.length} shown<//>`}
    </div>`}
    ${tab === "loadout" ? html`<${LoadoutView} items=${state.items} cur=${cur} />` : html`
    <div class=${"inv-layout" + (selected ? " with-inspect" : "")}>
      ${items.length === 0
        ? html`<div class="empty"><${Icon} name="key" size=${32} /><div>${state.items.length ? "Nothing matches." : "Your inventory is empty. Open cases to get skins."}</div>
            ${!state.items.length && html`<div style="margin-top:12px"><${Button} kind="prominent" onClick=${() => navigate("cases")}>Open Cases<//></div>`}</div>`
        : html`<div class="cs-grid">${items.slice(0, limit).map(v => html`<button key=${v.item.uid} class=${"cs-item" + (open === v.item.uid ? " selected" : "")}
            style=${`--c:${v.item.color}`} onClick=${() => setOpen(open === v.item.uid ? null : v.item.uid)}>
          <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
          <span class=${"fav" + (favs.has(v.item.uid) ? " on" : "")} role="button" title="Favorite" onClick=${e => { e.stopPropagation(); toggleFav(v.item.uid); }}>★</span>
          ${v.equipped && html`<span class="equipped" title="Equipped"><${Icon} name="check" size=${12} stroke=${3} /></span>`}
          ${v.item.statTrak && v.item.kind === "skin" && html`<span class="st">ST™</span>`}
          ${v.item.souvenir && html`<span class="st sv">SV</span>`}
          <div class="text">
            <div class="weapon">${v.weapon}</div>
            <div class="fin">${v.finish}</div>
          </div>
          <div class="price">${money(v.price, cur)}</div>
        </button>`)}<${More} shown=${Math.min(limit, items.length)} total=${items.length} onMore=${() => setLimit(l => l + PAGE)} /></div>`}
      ${selected && html`<${Inspect} v=${selected} cur=${cur} close=${() => setOpen(null)} />`}
    </div>`}
  <//>`;
}
