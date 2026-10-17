// Inventory: what you unboxed, laid out like CS2's inventory. Click an item to inspect it, Equip puts it on.
import { html, useState, useEffect, useRef } from "../vendor/preact-htm.js";
import { call, tryCall, useApi, navigate } from "../api.js";
import { Page, Button, Segmented, Select, TextField, Icon, ItemPic, Slider, Stepper, Toggle, confirm, sheet } from "../ui.js";

export const wearName = w => w < 0.07 ? "Factory New" : w < 0.15 ? "Minimal Wear" : w < 0.38 ? "Field-Tested" : w < 0.45 ? "Well-Worn" : "Battle-Scarred";
const SYMBOL = { USD: "$", EUR: "€", GBP: "£" };
export const money = (v, cur) => v == null ? "—" : cur === "EUR" ? `${v.toFixed(2).replace(".", ",")}€` : `${SYMBOL[cur] ?? "$"}${v.toFixed(2)}`;
const GRADE_RANK = ["Consumer Grade", "Base Grade", "Industrial Grade", "Mil-Spec Grade", "High Grade", "Restricted", "Remarkable", "Classified", "Exotic", "Covert", "Extraordinary", "Contraband"];
const equippable = v => (v.item.kind === "skin" && v.item.def) || ((v.item.kind === "pin" || v.item.kind === "music") && v.item.collectibleId);

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
    ${i.kind === "skin" && v.category === "weapons" && html`
      <div class="inspect-row"><span>Stickers</span><b>${(i.stickers ?? []).length}/5</b></div>
      ${(i.stickers ?? []).length > 0 && html`<div class="inspect-stickers">${i.stickers.map(st => html`<span title=${st.name}><${ItemPic} src=${st.image} size=${128} /></span>`)}</div>`}
      <${Button} block icon="layers" onClick=${() => sheet(close => html`<${StickerSheet} uid=${i.uid} close=${close} />`, { wide: true })}>Stickers…<//>`}
    <div class="inspect-row"><span>Market price</span><b>${money(v.price, cur)}</b></div>
    <div class="inspect-row"><span>Unboxed from</span><b>${i.caseName}</b></div>
    <div class="inspect-row"><span>Unboxed</span><b>${new Date(i.opened).toLocaleString()}</b></div>
    <div class="inspect-actions">
      ${equippable(v) && (v.equipped
        ? html`<${Button} block onClick=${() => tryCall("cases.unequip", i.uid)}>Unequip<//>`
        : html`<${Button} kind="prominent" block icon="check" onClick=${() => tryCall("cases.equip", i.uid)}>Equip<//>`)}
      ${v.price != null && html`<${Button} block icon="tag" onClick=${async () => {
        const gets = Math.max(0.01, Math.floor(v.price / 1.15 * 100) / 100);
        if (await confirm({ title: "Sell item?", message: `Sell ${i.name} on the market for ${money(v.price, cur)}?\nYou receive ${money(gets, cur)} (after Steam's fee) in your wallet.`, ok: "Sell" })) {
          await tryCall("cases.sell", i.uid); close();
        }
      }}>Sell · ${money(v.price, cur)}<//>`}
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
  const toOffset = (e, slot) => {
    const r = box.current.getBoundingClientRect();
    return { x: ((e.clientX - r.left) / r.width - 0.5 - slotX(gun.def, slot)) / 0.45, y: -((e.clientY - r.top) / r.height - 0.5) / 0.4 };
  };
  const clamp = n => Math.max(-1, Math.min(1, n));
  // The sticker keeps the spot you grabbed it at (dx, dy) instead of jumping under the cursor.
  const down = (e, slot) => {
    e.preventDefault(); setSel(slot);
    const st = stickers.find(x => x.slot === slot), p = toOffset(e, slot);
    setDrag({ slot, x: st.offsetX, y: st.offsetY, dx: st.offsetX - p.x, dy: st.offsetY - p.y });
    e.currentTarget.setPointerCapture?.(e.pointerId);
  };
  const move = e => { if (drag) { const p = toOffset(e, drag.slot); setDrag({ ...drag, x: clamp(p.x + drag.dx), y: clamp(p.y + drag.dy) }); } };
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
        <${ItemPic} src=${gun.image} size=${1024} />
        ${stickers.map(st => {
          const x = drag?.slot === st.slot ? drag.x : st.offsetX, y = drag?.slot === st.slot ? drag.y : st.offsetY;
          const at = spot(gun.def, st.slot, x, y);
          return html`<div key=${st.slot} class=${"placed" + (sel === st.slot ? " selected" : "")} onPointerDown=${e => down(e, st.slot)}
            style=${`left:${at.left}%;top:${at.top}%;width:${64 * st.scale}px;transform:translate(-50%,-50%) rotate(${st.rotation}deg);opacity:${1 - st.wear * 0.75}`}>
            <${ItemPic} src=${st.image} size=${128} /></div>`;
        })}
      </div>
      <div class="sticker-side">
        <div class="field-label">On this gun (${stickers.length}/5)</div>
        <div class="sticker-slots">
          ${stickers.map(st => html`<button key=${st.slot} class=${"slot" + (sel === st.slot ? " selected" : "")} onClick=${() => setSel(st.slot)} title=${st.name}>
            <${ItemPic} src=${st.image} size=${128} /></button>`)}
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
                title=${`Apply ${c.name}`} onClick=${() => apply(c.uid)}><${ItemPic} src=${c.image} size=${128} /></button>`)}
        </div>
      </div>
    </div>
    <div class="picker-foot"><div class="grow"></div><${Button} kind="prominent" onClick=${() => close(undefined)}>Done<//></div>
  </div>`;
}

/** CS2's Trade Up Contract: pick 10 same-grade guns (or 5 Coverts), see the odds, sign. */
function TradeUpSheet({ close }) {
  const [inv] = useApi("cases.inventory");
  const [picked, setPicked] = useState([]);
  const [check, setCheck] = useState(null);
  const [phase, setPhase] = useState("pick"); // pick | signing | done
  const [result, setResult] = useState(null);
  useEffect(() => { call("tradeup.check", { uids: picked }).then(setCheck, () => {}); }, [picked.join()]);
  if (!inv) return html`<div class="picker"><div class="empty small">Loading…</div></div>`;
  const cur = inv.currency;
  const first = inv.items.find(v => v.item.uid === picked[0]);
  const needed = first?.item.grade === "Covert" ? 5 : 10;
  const eligible = inv.items.filter(v => v.category === "weapons" && v.item.paint > 0 && !v.item.souvenir && v.item.grade !== "Contraband"
    && (!first || (v.item.grade === first.item.grade && v.item.statTrak === first.item.statTrak)));
  const toggle = uid => setPicked(p => p.includes(uid) ? p.filter(x => x !== uid) : p.length < needed ? [...p, uid] : p);
  const sign = async () => {
    setPhase("signing");
    const [r] = await Promise.all([call("tradeup.sign", { uids: picked }).catch(() => null), new Promise(ok => setTimeout(ok, 1600))]);
    if (!r) { setPhase("pick"); return; }
    setResult(r); setPicked([]); setPhase("done");
  };

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
  return html`<div class="picker tradeup">
    <div class="picker-head"><h2>Trade Up Contract</h2>
      <span class="faint small">10 guns of one grade for one of the next grade (or 5 Covert for a knife or gloves). All StatTrak™ or none, no souvenirs. The items you put in are gone.</span></div>
    <div class="tradeup-layout">
      <div class="tradeup-left">
        <div class="field-label">Contract (${picked.length}/${needed})</div>
        <div class="tradeup-slots">
          ${Array.from({ length: needed }, (_, n) => {
            const v = inv.items.find(x => x.item.uid === picked[n]);
            return v ? html`<button class="cs-item" style=${`--c:${v.item.color}`} onClick=${() => toggle(v.item.uid)} title="Take out">
                <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
                <div class="text"><div class="weapon">${v.weapon}</div><div class="fin">${v.finish}</div></div>
                <div class="price">${v.item.wear.toFixed(4)}</div></button>`
              : html`<div class="tradeup-empty">${n + 1}</div>`;
          })}
        </div>
        <div class="field-label">Your eligible items ${first ? `· ${first.item.grade}${first.item.statTrak ? " · StatTrak™" : ""}` : ""}</div>
        <div class="tradeup-pool scroll">
          ${eligible.length === 0 ? html`<div class="faint small">No items that can go in. Open cases for more.</div>`
            : eligible.map(v => html`<button key=${v.item.uid} class=${"cs-item" + (picked.includes(v.item.uid) ? " selected" : "")} style=${`--c:${v.item.color}`}
                onClick=${() => toggle(v.item.uid)}>
                <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
                <div class="text"><div class="weapon">${v.weapon}</div><div class="fin">${v.finish}</div></div>
                <div class="price">${v.item.wear.toFixed(4)}</div></button>`)}
        </div>
      </div>
      <div class="tradeup-right">
        <div class="field-label">Possible results</div>
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
          <div class="inspect-row"><span>Average result</span><b>${money(check?.expectedValue, cur)}</b></div>
          ${check && !check.ok && picked.length > 0 && html`<div class="small note-warn">${check.message}</div>`}
          <${Button} kind="prominent" size="lg" block icon="check" disabled=${!check?.ok} onClick=${sign}>Sign Contract<//>
        </div>
      </div>
    </div>
    <div class="picker-foot"><${Button} kind="plain" onClick=${() => setPicked([])}>Clear<//><div class="grow"></div><${Button} onClick=${() => close(undefined)}>Close<//></div>
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
            <${Button} kind="prominent" icon="plus" onClick=${add}>Add to Inventory<//>
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

function LoadoutView({ items, cur }) {
  const [team, setTeam] = useState(2);
  const onSide = v => team === 2 ? v.equippedT : v.equippedCT;
  const pick = async (title, mine) => {
    const choice = await sheet(close => html`<div class="picker">
      <div class="picker-head"><h2>${title}</h2><span class="faint small">Your skins for this slot. Pick one to put it on.</span></div>
      <div class="picker-grid scroll">
        ${mine.length === 0 ? html`<div class="empty small">None in your inventory. Open cases to get some.</div>`
          : mine.map(v => html`<button key=${v.item.uid} class=${"cs-item" + (onSide(v) ? " selected" : "")} style=${`--c:${v.item.color}`} onClick=${() => close(v)}>
              <div class="pic"><${ItemPic} src=${v.item.image} size=${256} /></div>
              <div class="text"><div class="fin">${v.finish}</div><div class="weapon">${v.item.statTrak ? "StatTrak™ · " : ""}${wearName(v.item.wear)}</div></div>
              <div class="price">${money(v.price, cur)}</div></button>`)}
      </div>
      <div class="picker-foot">${mine.some(onSide) && html`<${Button} onClick=${() => close("default")}>Use Default<//>`}<div class="grow"></div>
        <${Button} onClick=${() => close(undefined)}>Cancel<//></div></div>`, { wide: true });
    if (choice === "default") tryCall("cases.unequip", mine.find(onSide).item.uid);
    else if (choice && !onSide(choice)) tryCall("cases.equipSide", { uid: choice.item.uid, team });
  };
  const slot = (key, name, mine) => {
    const on = mine.find(onSide);
    return html`<button key=${key} class=${"cs-item loadout-slot" + (on ? " selected" : "")} style=${`--c:${on?.item.color ?? "transparent"}`}
        onClick=${() => pick(name, mine)}>
      <div class="pic">${on ? html`<${ItemPic} src=${on.item.image} size=${256} />` : html`<${Icon} name="crosshair" size=${30} />`}</div>
      <div class="text"><div class="weapon">${name}</div><div class="fin">${on ? on.finish : "Default"}</div></div>
      <div class="price">${mine.length ? `${mine.length} owned` : ""}</div>
    </button>`;
  };
  const skins = items.filter(v => v.item.kind === "skin");
  const of = def => skins.filter(v => v.item.def === def);
  const special = skins.filter(v => v.category === "special");
  const knives = special.filter(v => !isGlove(v)), gloves = special.filter(isGlove);
  return html`<div class="loadout">
    <${Segmented} options=${[{ value: 2, label: "Terrorists" }, { value: 3, label: "Counter-Terrorists" }]} value=${team} onChange=${setTeam} />
    <div class="loadout-group"><h3>Melee</h3><div class="cs-grid">
      ${slot("knife", "Knife", knives)}
    </div></div>
    ${LOADOUT.map(g => html`<div class="loadout-group" key=${g.name}><h3>${g.name}</h3><div class="cs-grid">
      ${g.guns.filter(([, , t]) => t === 0 || t === team).map(([def, name]) => slot(def, name, of(def)))}
    </div></div>`)}
    <div class="loadout-group"><h3>Gloves</h3><div class="cs-grid">${slot("gloves", "Gloves", gloves)}</div></div>
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
  const [sort, setSort] = useState("newest");
  const [q, setQ] = useState("");
  const [open, setOpen] = useState(null);
  if (!state) return html`<${Page} title="Inventory" />`;

  const cur = state.currency;
  const words = q.toLowerCase().split(/\s+/).filter(Boolean);
  const toggleFav = uid => setFavs(f => {
    const n = new Set(f); n.has(uid) ? n.delete(uid) : n.add(uid);
    try { localStorage.setItem("inv.favs", JSON.stringify([...n])); } catch { /* no storage */ }
    return n;
  });
  const lo = parseFloat(minP), hi = parseFloat(maxP);
  let items = state.items.filter(v => (tab === "all" || tab === "loadout" || v.category === tab)
    && (!onlyST || v.item.statTrak) && (!onlyFav || favs.has(v.item.uid)) && (!fromCase || v.item.caseName === fromCase)
    && (isNaN(lo) || (v.price ?? 0) >= lo) && (isNaN(hi) || (v.price ?? 0) <= hi)
    && words.every(w => `${v.item.name} ${v.item.caseName} ${v.item.grade}`.toLowerCase().includes(w)));
  const by = {
    newest: (a, b) => new Date(b.item.opened) - new Date(a.item.opened),
    rarity: (a, b) => GRADE_RANK.indexOf(b.item.grade) - GRADE_RANK.indexOf(a.item.grade) || (b.price ?? 0) - (a.price ?? 0),
    price: (a, b) => (b.price ?? -1) - (a.price ?? -1),
    name: (a, b) => a.item.name.localeCompare(b.item.name),
  };
  items = [...items].sort((a, b) => favs.has(b.item.uid) - favs.has(a.item.uid) || by[sort](a, b)); // favorites first
  const cases = [...new Set(state.items.map(v => v.item.caseName).filter(Boolean))].sort();
  // Keeps the best-priced copy of each skin (and anything equipped or favorite); sells the rest.
  const duplicates = Object.values(Object.groupBy(state.items.filter(v => v.item.kind === "skin"), v => v.item.name))
    .flatMap(g => g.length < 2 ? [] : [...g].sort((a, b) => (b.price ?? 0) - (a.price ?? 0)).slice(1))
    .filter(v => !v.equipped && !favs.has(v.item.uid) && v.price != null);
  const sellDuplicates = async () => {
    const total = duplicates.reduce((n, v) => n + v.price, 0);
    if (await confirm({ title: "Sell duplicates?", message: `Sell ${duplicates.length} extra copies for about ${money(total, cur)}?\nThe best copy of each skin, equipped and favorite items stay.`, ok: "Sell All" }))
      for (const v of duplicates) await call("cases.sell", v.item.uid).catch(() => null);
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
      subtitle=${`${state.items.length} items · worth ${money(state.total, cur)}${state.useWallet ? ` · wallet ${money(state.wallet, cur)}` : ""}`}
      actions=${html`
        <${Button} icon="key" onClick=${() => navigate("cases")}>Open Cases<//>
        <${Button} icon="layers" onClick=${tradeUp}>Trade Up<//>
        <${Button} icon="tag" disabled=${!duplicates.length} onClick=${sellDuplicates} title="Sell extra copies of the same skin">Sell Duplicates (${duplicates.length})<//>
        ${state.admin && html`<${Button} kind="prominent" icon="plus" onClick=${addItem} title="Admin: put any item in your inventory">Add Item<//>`}`}>
    <div class="hstack inv-bar">
      <${Segmented} options=${[{ value: "all", label: "All Items" }, { value: "loadout", label: "Loadout" }, { value: "weapons", label: "Weapons" }, { value: "special", label: "Knives & Gloves" },
        { value: "graphics", label: "Stickers & Graffiti" }, { value: "other", label: "Collectibles" }]} value=${tab} onChange=${setTab} />
      <div class="grow"><${TextField} icon="search" placeholder="Search inventory" value=${q} onInput=${setQ} /></div>
      <${Select} width=${150} value=${sort} onChange=${setSort}
        options=${[{ value: "newest", label: "Newest" }, { value: "rarity", label: "Rarity" }, { value: "price", label: "Price" }, { value: "name", label: "Name" }]} />
    </div>
    ${tab !== "loadout" && html`<div class="hstack inv-bar">
      <${Select} width=${200} value=${fromCase} onChange=${setFromCase} options=${[{ value: "", label: "Any case" }, ...cases.map(c => ({ value: c, label: c }))]} />
      <${Toggle} value=${onlyST} onChange=${setOnlyST} /><span class="small">StatTrak™</span>
      <${Toggle} value=${onlyFav} onChange=${setOnlyFav} /><span class="small">Favorites</span>
      <${TextField} width=${90} placeholder="Min price" value=${minP} onInput=${setMinP} />
      <${TextField} width=${90} placeholder="Max price" value=${maxP} onInput=${setMaxP} />
    </div>`}
    ${tab === "loadout" ? html`<${LoadoutView} items=${state.items} cur=${cur} />` : html`
    <div class=${"inv-layout" + (selected ? " with-inspect" : "")}>
      ${items.length === 0
        ? html`<div class="empty"><${Icon} name="key" size=${32} /><div>${state.items.length ? "Nothing matches." : "Your inventory is empty. Open cases to get skins."}</div>
            ${!state.items.length && html`<div style="margin-top:12px"><${Button} kind="prominent" onClick=${() => navigate("cases")}>Open Cases<//></div>`}</div>`
        : html`<div class="cs-grid">${items.map(v => html`<button key=${v.item.uid} class=${"cs-item" + (open === v.item.uid ? " selected" : "")}
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
        </button>`)}</div>`}
      ${selected && html`<${Inspect} v=${selected} cur=${cur} close=${() => setOpen(null)} />`}
    </div>`}
  <//>`;
}
