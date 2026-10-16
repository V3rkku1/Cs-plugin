// Inventory: what you unboxed, laid out like CS2's inventory. Click an item to inspect it, Equip puts it on.
import { html, useState } from "../vendor/preact-htm.js";
import { tryCall, useApi, img } from "../api.js";
import { Page, Button, Segmented, Select, TextField, Icon, confirm } from "../ui.js";
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

  return html`<${Page} title="Inventory" class="inventory-page"
      subtitle=${`${state.items.length} items · worth ${money(state.total, cur)}`}
      actions=${html`
        <${Button} icon="key" onClick=${() => navigate("cases")}>Open Cases<//>
        ${state.admin && html`<${Button} icon="skins" onClick=${() => navigate("skins")} title="Admin: pick any skin">All Skins<//>`}`}>
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
