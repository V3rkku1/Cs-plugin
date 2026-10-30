// Liquid Glass components shared by every page. Keep pages built from these so the look stays one system.
import { html, render, useState, useEffect, useRef, useLayoutEffect } from "./vendor/preact-htm.js";
import { Icon } from "./icons.js";
import { call, on, img } from "./api.js";

export { Icon };

/** Moves the glass sheen highlight with the pointer. */
export function sheen(e) {
  const r = e.currentTarget.getBoundingClientRect();
  e.currentTarget.style.setProperty("--mx", `${e.clientX - r.left}px`);
  e.currentTarget.style.setProperty("--my", `${e.clientY - r.top}px`);
}

/** Page with a large title. actions = buttons on the right of the title. */
export function Page({ title, subtitle, actions, children, class: cls = "" }) {
  return html`<div class=${"page " + cls}>
    <header class="page-header">
      <div>
        <h1>${title}</h1>
        ${subtitle && html`<p>${subtitle}</p>`}
      </div>
      ${actions && html`<div class="actions">${actions}</div>`}
    </header>
    ${children}
  </div>`;
}

/** Inset grouped glass card with a caption above (title) and an optional footer note. */
export function Section({ title, note, footer, children, pad = false, class: cls = "", style }) {
  return html`<section class=${"section " + cls} style=${style}>
    ${title && html`<div class="section-title">${title}${note && html`<span class="note">${note}</span>`}</div>`}
    <div class=${"card glass" + (pad ? " pad" : "")}>${children}</div>
    ${footer && html`<div class="section-footer">${footer}</div>`}
  </section>`;
}

/**
 * A settings row: title + subtitle on the left, accessory (children) on the right.
 * icon = { name, color } for a coloured iOS-style icon square. onClick makes the row a button.
 */
export function Row({ title, subtitle, icon, children, onClick, class: cls = "", subtitleClass = "" }) {
  return html`<div class=${`row ${icon ? "with-icon" : ""} ${onClick ? "clickable" : ""} ${cls}`} onClick=${onClick}>
    ${icon && html`<div class="row-icon" style=${`--c: var(--${icon.color ?? "blue"})`}><${Icon} name=${icon.name} size=${18} stroke=${2} /></div>`}
    <div class="text">
      <div class="title">${title}</div>
      ${subtitle && html`<div class=${"subtitle " + subtitleClass}>${subtitle}</div>`}
    </div>
    ${children != null && html`<div class="accessory">${children}</div>`}
    ${onClick && !children && html`<${Icon} name="chevronRight" size=${16} class="faint" />`}
  </div>`;
}

/**
 * kind: "default" | "prominent" | "plain" | "destructive" (combine: "prominent destructive").
 * size: "sm" | "lg". icon: icon name. tint: CSS colour for prominent buttons.
 */
export function Button({ kind = "default", size = "", icon, tint, onClick, disabled, busy, children, block, title, class: cls = "" }) {
  return html`<button class=${`btn ${kind} ${size} ${block ? "block" : ""} ${children ? "" : "icon-only"} ${cls}`}
      style=${tint ? `--tint: ${tint}` : undefined} disabled=${disabled || busy} onClick=${onClick} title=${title}>
    ${busy ? html`<span class="state-icon busy" style="width:16px;height:16px;border-width:2px"></span>`
      : icon && html`<${Icon} name=${icon} size=${17} stroke=${2} />`}
    ${children}
  </button>`;
}

export function Toggle({ value, onChange, disabled }) {
  return html`<button role="switch" aria-checked=${!!value} class=${"switch" + (value ? " on" : "")} disabled=${disabled}
    onClick=${e => { e.stopPropagation(); onChange?.(!value); }}><span class="knob"></span></button>`;
}

/**
 * options: ["A", "B"] or [{ value, label, disabled }]. value = the selected option's value (or index for strings).
 * cols > 0 wraps into a grid of that many columns.
 */
export function Segmented({ options, value, onChange, disabled, cols = 0, class: cls = "" }) {
  const items = options.map((o, i) => typeof o === "string" ? { value: i, label: o } : o);
  const root = useRef();
  const [thumb, setThumb] = useState(null);
  // The sliding thumb sits under the active button: measured after each change and on resize.
  const measure = (clear = true) => {
    const active = root.current?.querySelector("button.active");
    if (active) setThumb({ x: active.offsetLeft - 3, y: active.offsetTop - 3, w: active.offsetWidth, h: active.offsetHeight });
    else if (clear) setThumb(null);
  };
  useLayoutEffect(measure, [value, options.length, items.map(i => i.label).join()]);
  useEffect(() => {
    const ro = new ResizeObserver(() => measure(false));
    ro.observe(root.current);
    return () => ro.disconnect();
  }, []);
  return html`<div ref=${root} class=${`segmented ${cols ? "rows" : ""} ${disabled ? "disabled" : ""} ${cls}`} style=${cols ? `--cols:${cols}` : undefined}>
    ${thumb && html`<div class="thumb" style=${`width:${thumb.w}px;height:${thumb.h}px;transform:translate(${thumb.x}px,${thumb.y}px)`}></div>`}
    ${items.map(o => html`<button class=${o.value === value ? "active" : ""} disabled=${o.disabled}
      onClick=${() => o.value !== value && onChange?.(o.value)}>${o.label}</button>`)}
  </div>`;
}

/** Range slider with the value shown on the right (format(value) for custom text). */
export function Slider({ min = 0, max = 100, step = 1, value, onChange, onCommit, format, tint, width }) {
  const p = ((value - min) / (max - min)) * 100;
  return html`<div class="slider" style=${`--p:${p}%;${tint ? `--tint:${tint};` : ""}${width ? `width:${width}px` : ""}`}>
    <input type="range" min=${min} max=${max} step=${step} value=${value}
      onInput=${e => onChange?.(Number(e.currentTarget.value))}
      onChange=${e => onCommit?.(Number(e.currentTarget.value))} />
    <span class="value">${format ? format(value) : value}</span>
  </div>`;
}

/** − value + with typing allowed. Calls onChange with a clamped number. */
export function Stepper({ value, min = -Infinity, max = Infinity, step = 1, onChange, format, parse, width = 64, disabled }) {
  const [text, setText] = useState(null);
  const shown = text ?? (format ? format(value) : String(value));
  const commit = raw => {
    setText(null);
    const n = parse ? parse(raw) : Number(raw);
    if (Number.isFinite(n)) onChange?.(Math.min(max, Math.max(min, n)));
  };
  const round = n => Math.round(n * 1e6) / 1e6;
  return html`<div class="stepper">
    <button disabled=${disabled || value <= min} onClick=${() => onChange?.(Math.max(min, round(value - step)))}>−</button>
    <input style=${`width:${width}px`} value=${shown} disabled=${disabled}
      onInput=${e => setText(e.currentTarget.value)}
      onBlur=${e => commit(e.currentTarget.value)}
      onKeyDown=${e => e.key === "Enter" && e.currentTarget.blur()} />
    <button disabled=${disabled || value >= max} onClick=${() => onChange?.(Math.min(max, round(value + step)))}>+</button>
  </div>`;
}

/** Text input in a glass well. onEnter fires on Enter. icon = leading icon name. */
export function TextField({ value, onInput, onEnter, placeholder, icon, width, multiline, rows = 4, type = "text", autofocus, maxLength }) {
  const ref = useRef();
  useEffect(() => { if (autofocus) ref.current?.focus(); }, []);
  const props = {
    ref, value: value ?? "", placeholder, maxLength,
    onInput: e => onInput?.(e.currentTarget.value),
    onKeyDown: e => { if (e.key === "Enter" && !multiline) onEnter?.(e.currentTarget.value); },
  };
  return html`<label class=${"field" + (multiline ? " area" : "")} style=${width ? `width:${width}px` : undefined}>
    ${icon && html`<${Icon} name=${icon} size=${16} />`}
    ${multiline ? html`<textarea rows=${rows} ...${props}></textarea>` : html`<input type=${type} ...${props} />`}
  </label>`;
}

/**
 * Pop-up menu button. options: [{ value, label, note }] or strings. Opens a glass menu under the button.
 */
export function Select({ options, value, onChange, placeholder = "Choose…", disabled, width }) {
  const items = options.map((o, i) => typeof o === "string" ? { value: o, label: o } : o);
  const [open, setOpen] = useState(null);
  const button = useRef();
  const current = items.find(o => o.value === value);
  const show = () => {
    const r = button.current.getBoundingClientRect();
    // Opens on the side with more room and never runs off the window (cut-off items couldn't be picked).
    const below = window.innerHeight - r.bottom, above = r.top, down = below >= 260 || below >= above;
    setOpen({ left: Math.min(r.left, window.innerWidth - Math.max(r.width, 200) - 8), top: down ? r.bottom + 6 : undefined, bottom: down ? undefined : window.innerHeight - r.top + 6,
      width: Math.max(r.width, 200), max: Math.min(360, (down ? below : above) - 18) });
  };
  useEffect(() => {
    if (!open) return;
    // Rendered at the body: a glass card's backdrop-filter would otherwise offset and clip a fixed menu.
    const host = document.body.appendChild(document.createElement("div"));
    render(html`<div class="menu glass" style=${`left:${open.left}px;${open.top != null ? `top:${open.top}px;` : `bottom:${open.bottom}px;`}min-width:${open.width}px;max-height:${open.max}px`}
        ref=${el => el?.querySelector(".active")?.scrollIntoView({ block: "nearest" })}>
      ${items.map(o => html`<button class=${o.value === value ? "active" : ""} onClick=${() => { setOpen(null); onChange?.(o.value); }}>
        ${o.label}${o.note && html`<small>${o.note}</small>`}
        ${o.value === value && html`<${Icon} name="check" size=${15} stroke=${2.2} class="check" />`}
      </button>`)}
    </div>`, host);
    const close = e => { if (!host.contains(e.target) && !button.current?.contains(e.target)) setOpen(null); };
    const resize = () => setOpen(null);
    // Scrolling the page would leave the menu floating away from its button: close it (its own scrolling is fine).
    const scroll = e => { if (!host.contains(e.target)) setOpen(null); };
    setTimeout(() => window.addEventListener("pointerdown", close), 0);
    window.addEventListener("resize", resize);
    window.addEventListener("scroll", scroll, true);
    return () => {
      window.removeEventListener("pointerdown", close);
      window.removeEventListener("resize", resize);
      window.removeEventListener("scroll", scroll, true);
      render(null, host);
      host.remove();
    };
  }, [open, value]);
  return html`<div class="select">
    <button ref=${button} disabled=${disabled} onClick=${() => open ? setOpen(null) : show()} style=${width ? `width:${width}px` : undefined}>
      <span>${current?.label ?? placeholder}</span><${Icon} name="chevronUpDown" size=${15} />
    </button>
  </div>`;
}

/** An item picture through the launcher's cache; a placeholder icon when it's missing or offline. */
export function ItemPic({ src, size = 256 }) {
  const [bad, setBad] = useState(false);
  return src && !bad ? html`<img src=${img(src, size)} alt="" loading="lazy" onError=${() => setBad(true)} />` : html`<${Icon} name="skins" size=${30} />`;
}

/** Install-step style status: ok / warning / failed / missing / busy. */
export function StateIcon({ state }) {
  const glyph = { ok: "check", warning: "warning", failed: "x" }[state];
  return html`<div class=${"state-icon " + state}>${glyph && html`<${Icon} name=${glyph} size=${15} stroke=${2.6} />`}</div>`;
}

export function Badge({ color = "blue", children }) {
  return html`<span class="badge" style=${`--c: var(--${color})`}>${children}</span>`;
}

/** value 0..1, or null/-1 for indeterminate. */
export function Progress({ value }) {
  const indeterminate = value == null || value < 0;
  return html`<div class=${"progress" + (indeterminate ? " indeterminate" : "")}><div style=${`width:${indeterminate ? 35 : Math.round(value * 100)}%`}></div></div>`;
}

// ------------------------------------------------------------------------------------------------
// Toasts and modal sheets
// ------------------------------------------------------------------------------------------------

const toastColors = { info: "var(--blue)", good: "var(--green)", warning: "var(--orange)", bad: "var(--red)" };

export function Toasts() {
  const [items, setItems] = useState([]);
  useEffect(() => on("toast", ({ text, kind }) => {
    const id = Math.random();
    setItems(list => [...list.filter(t => t.text !== text).slice(-2), { id, text, kind }]);
    setTimeout(() => setItems(list => list.map(t => t.id === id ? { ...t, leaving: true } : t)), 3800);
    setTimeout(() => setItems(list => list.filter(t => t.id !== id)), 4150);
  }), []);
  return html`<div class="toasts">${items.map(t => html`<div key=${t.id} class=${"toast glass" + (t.leaving ? " leaving" : "")}>
    <span class="dot" style=${`--c:${toastColors[t.kind] ?? toastColors.info}`}></span>${t.text}</div>`)}</div>`;
}

let sheetRoot;
/** Shows a modal sheet. render(close) returns its content. Resolves with the value passed to close(). */
export function sheet(content, { wide = false } = {}) {
  sheetRoot ??= document.body.appendChild(document.createElement("div"));
  return new Promise(resolve => {
    // Every way out drops the Escape listener (a scrim click used to leave one behind per sheet).
    const close = value => { window.removeEventListener("keydown", onKey); render(null, sheetRoot); resolve(value); };
    const onKey = e => { if (e.key === "Escape") close(undefined); };
    window.addEventListener("keydown", onKey);
    render(html`<div class="scrim" onPointerDown=${e => e.target === e.currentTarget && close(undefined)}>
      <div class=${"sheet glass" + (wide ? " wide" : "")}>${content(close)}</div>
    </div>`, sheetRoot);
  });
}

/** "Are you sure?" sheet. Resolves true / false. */
export function confirm({ title, message, ok = "OK", cancel = "Cancel", danger = false }) {
  return sheet(close => html`
    <h2>${title}</h2>
    <p style="white-space:pre-line">${message}</p>
    <div class="buttons">
      <${Button} onClick=${() => close(false)}>${cancel}<//>
      <${Button} kind=${"prominent" + (danger ? " destructive" : "")} onClick=${() => close(true)}>${ok}<//>
    </div>`).then(v => v === true);
}

/** Asks for a line of text. Resolves the text, or null when cancelled. */
export function prompt({ title, message, value = "", placeholder, ok = "OK" }) {
  return sheet(close => {
    const Body = () => {
      const [text, setText] = useState(value);
      return html`
        <h2>${title}</h2>
        ${message && html`<p>${message}</p>`}
        <${TextField} value=${text} onInput=${setText} onEnter=${() => close(text)} placeholder=${placeholder} autofocus />
        <div class="buttons">
          <${Button} onClick=${() => close(null)}>Cancel<//>
          <${Button} kind="prominent" onClick=${() => close(text)}>${ok}<//>
        </div>`;
    };
    return html`<${Body} />`;
  }).then(v => v ?? null);
}

// ------------------------------------------------------------------------------------------------
// Activity log
// ------------------------------------------------------------------------------------------------

/** The activity log is kept for the whole session, whichever page is open. */
const logLines = [];
const logListeners = new Set();
call("app.log").then(lines => {
  logLines.unshift(...lines.filter(l => !logLines.includes(l)));
  logListeners.forEach(fn => fn());
}).catch(() => {});
on("log", line => {
  logLines.push(line);
  if (logLines.length > 600) logLines.splice(0, logLines.length - 600);
  logListeners.forEach(fn => fn());
});

export function LogView({ height = 420 }) {
  const [, setTick] = useState(0);
  const box = useRef();
  useEffect(() => {
    const fn = () => setTick(t => t + 1);
    logListeners.add(fn);
    return () => logListeners.delete(fn);
  }, []);
  useEffect(() => { box.current && (box.current.scrollTop = box.current.scrollHeight); });
  return html`<div class="log" ref=${box} style=${`height:${height}px`}>
    ${logLines.length === 0 ? html`<span class="faint">Nothing yet.</span>`
      : logLines.map(l => html`<div class=${/FAILED/.test(l) ? "fail" : ""}>${l}</div>`)}
  </div>`;
}
