// Hacker Mode: who hacks, quick profiles and every power, grouped. Each change is saved and sent to the
// running server at once (Api.Hacker.cs).
import { html, useState, useEffect, useRef } from "../vendor/preact-htm.js";
import { tryCall, useApi, toast } from "../api.js";
import { Page, Section, Row, Toggle, Segmented, Slider, TextField, Select, Icon } from "../ui.js";

// Icons icons.js doesn't have.
const localIcons = {
  cube: `<path d="m12 3 8 4.5v9L12 21l-8-4.5v-9z"/><path d="m4 7.5 8 4.5 8-4.5M12 12v9"/>`,
  skeleton: `<circle cx="12" cy="4.8" r="2"/><path d="M12 7v7M7 9.5h10M12 14l-3.5 6.5M12 14l3.5 6.5M7 9.5 5 13M17 9.5l2 3.5"/>`,
  radar: `<circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="4.5"/><path d="M12 12 18 6"/><circle cx="15.5" cy="14.5" r="1.1" fill="currentColor"/>`,
  bullet: `<path d="M10 21V10c0-3 1-5.5 2-7 1 1.5 2 4 2 7v11z"/><path d="M10 17h4"/>`,
  spread: `<circle cx="12" cy="12" r="1.6" fill="currentColor"/><path d="M12 3v4M12 17v4M3 12h4M17 12h4"/>`,
  recoil: `<path d="M12 20V6M7 11l5-5 5 5"/><path d="M5 20h14"/>`,
  infinity: `<path d="M12 12c-2-2.8-3.6-4-5.5-4a4 4 0 0 0 0 8c1.9 0 3.5-1.2 5.5-4s3.6-4 5.5-4a4 4 0 0 1 0 8c-1.9 0-3.5-1.2-5.5-4z"/>`,
  bunny: `<path d="M9 9.5C8 6 8 3.5 9.3 3.2s2 2.3 2.2 6M15 9.5c1-3.5 1-6-.3-6.3s-2 2.3-2.2 6"/><ellipse cx="12" cy="14.5" rx="5.5" ry="5"/><circle cx="10" cy="14" r=".8" fill="currentColor"/><circle cx="14" cy="14" r=".8" fill="currentColor"/>`,
  flash: `<circle cx="12" cy="12" r="4"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/><path d="M4 20 20 4"/>`,
  wall: `<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 9.7h18M3 14.3h18M9 5v4.7M15 5v4.7M12 9.7v4.6M7 14.3V19M17 14.3V19"/>`,
};

function Glyph({ name, size = 18, stroke = 2 }) {
  if (!localIcons[name]) return html`<${Icon} name=${name} size=${size} stroke=${stroke} />`;
  return html`<svg class="icon" width=${size} height=${size} viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width=${stroke}
    stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" dangerouslySetInnerHTML=${{ __html: localIcons[name] }} />`;
}

/** A settings row with a coloured icon square that may use a local icon. */
function PowerRow({ icon, color, title, subtitle, children, sub = false, subtitleClass = "" }) {
  return html`<div class=${"row with-icon" + (sub ? " hk-subrow" : "")}>
    <div class="row-icon" style=${`--c: var(--${color})`}><${Glyph} name=${icon} /></div>
    <div class="text">
      <div class="title">${title}</div>
      ${subtitle && html`<div class=${"subtitle " + subtitleClass}>${subtitle}</div>`}
    </div>
    <div class="accessory">${children}</div>
  </div>`;
}

/** Sub-options that fold away while their main toggle is off. */
function Fold({ open, children }) {
  return html`<div class=${"hk-fold" + (open ? " open" : "")} inert=${!open}><div>${children}</div></div>`;
}

const modeCards = [
  { icon: "x", color: "gray", title: "Off", text: "Nobody hacks. Pick who hacks to turn it on." },
  { icon: "friends", color: "green", title: "Everyone", text: "Every player hacks. Great vs bots." },
  { icon: "dice", color: "purple", title: "One random", text: "One hacker per round glows green. Catch them!" },
];

const profileLooks = {
  legit: { icon: "target", color: "blue" },
  closet: { icon: "eye", color: "indigo" },
  rage: { icon: "bolt", color: "red" },
  movement: { icon: "run", color: "teal" },
  off: { icon: "x", color: "gray" },
};

const styleNotes = [
  "Eases the crosshair onto the target, like a good player",
  "Aims only the shot and puts your view back",
  "Snaps onto the target and shoots",
];

const aggressionText = v => v <= 3 ? `${v} · legit` : v <= 7 ? `${v}` : `${v} · rage`;

const commands = [
  { cmd: "!cheats", text: "Cheat menu in game", copy: "bind F3 css_cheats", hint: "bind F3 css_cheats" },
  { cmd: "!aimkey", text: "Pick your own aim key in game", copy: "!aimkey mouse2", hint: "!aimkey mouse2" },
  { cmd: "!aimtoggle", text: "Aimbot always on / off", copy: "bind x css_aimtoggle", hint: "bind x css_aimtoggle" },
  { cmd: "!hacks", text: "Shows who hacks and which powers are on", copy: "!hacks" },
];

function copy(text) {
  navigator.clipboard?.writeText(text).then(() => toast(`Copied: ${text}`, "good"), () => toast(text));
}

export function HackerPage() {
  const [state] = useApi("hacker.state");
  const [aggression, setAggression] = useState(null);
  const [bindKey, setBindKey] = useState(null);
  const bindTimer = useRef();
  useEffect(() => () => clearTimeout(bindTimer.current), []);

  if (!state) return html`<${Page} title="Hacker Mode" />`;
  const s = state.settings;
  const set = patch => tryCall("hacker.set", patch);
  const off = s.mode === 0;

  // Typing a key sends one update after a pause, not one per letter.
  const typeKey = text => {
    setBindKey(text);
    clearTimeout(bindTimer.current);
    bindTimer.current = setTimeout(() => set({ bindKey: text }).then(() => setBindKey(null)), 450);
  };
  const keyText = bindKey ?? s.bindKey;
  const keyValid = keyText.trim() === "" || /^[a-z0-9_]{1,16}$/i.test(keyText.trim());

  const live = !state.running
    ? { color: "gray", title: "Server not running", text: "Start on the Play page · changes here apply right away" }
    : { color: "green", title: state.sent ? "Sent to the server" : "Server running",
        text: "Every change here applies in game right away" + (s.bindKey.trim() && state.bindKeyValid ? " · own aim key works after CS2 restarts (or console: exec lanskintester_client)" : "") };

  const power = (key, icon, color, title, subtitle) => html`<${PowerRow} icon=${icon} color=${color} title=${title} subtitle=${subtitle}>
    <${Toggle} value=${s[key]} onChange=${v => set({ [key]: v })} />
  <//>`;

  return html`<${Page} class="hk" title="Hacker Mode"
      subtitle="Fun cheats on your own server. Nobody's game is changed and nothing gets you banned."
      actions=${html`<div class="hk-live" style=${`--c: var(--${live.color})`} title=${live.text}><span class="dot"></span>${live.title}</div>`}>

    <section class="hk-hero glass liquid">
      <div class="hk-hero-head">
        <div class="hk-hero-icon"><${Icon} name="hacker" size=${30} stroke=${1.8} /></div>
        <div>
          <h2>Who hacks</h2>
          <p>${off ? "Hacker mode is off. Your picks below are kept for when you turn it on." : s.mode === 1 ? "Everyone on the server has the powers below." : "Each round one random player gets the powers below."}</p>
        </div>
      </div>
      <div class="hk-modes" role="radiogroup">
        ${modeCards.map((m, i) => html`<button role="radio" aria-checked=${s.mode === i} class=${"hk-mode tile glass sheen" + (s.mode === i ? " selected" : "")}
            style=${`--c: var(--${m.color}, #8e8e93)`} onClick=${() => s.mode !== i && set({ mode: i })}>
          <span class="hk-mode-icon"><${Icon} name=${m.icon} size=${22} stroke=${2} /></span>
          <span class="title">${state.modes[i] === "One random" ? "One Random" : state.modes[i]}</span>
          <span class="subtitle">${m.text}</span>
          ${s.mode === i && html`<span class="hk-check"><${Icon} name="check" size=${14} stroke=${3} /></span>`}
        </button>`)}
      </div>

      <div class="hk-caption">Quick profiles <span>one click sets every power below</span></div>
      <div class="hk-profiles">
        ${state.profiles.map(p => {
          const look = profileLooks[p.id] ?? profileLooks.legit;
          return html`<button class=${"hk-profile tile glass sheen" + (p.active ? " selected" : "") + (p.id === "off" ? " hk-danger" : "")}
              style=${`--c: var(--${look.color}, #8e8e93)`} onClick=${() => tryCall("hacker.profile", p.id)} title=${p.subtitle}>
            <span class="hk-profile-icon"><${Glyph} name=${look.icon} size=${18} /></span>
            <span class="title">${p.title}</span>
            <span class="subtitle">${p.subtitle}</span>
          </button>`;
        })}
      </div>
    </section>

    <div class=${"cols wrap hk-powers" + (off ? " hk-dim" : "")} style="--n:2">
      <div class="stack">
        <${Section} title="Aimbot" note="moves your aim onto an enemy">
          ${power("aimbot", "crosshair", "red", "Aimbot", "Locks onto enemies for you")}
          <${Fold} open=${s.aimbot}>
            <${PowerRow} sub icon="sliders" color="orange" title="Style" subtitle=${styleNotes[s.aimStyle]}>
              <${Segmented} options=${state.aimStyles} value=${s.aimStyle} onChange=${v => set({ aimStyle: v })} />
            <//>
            <${PowerRow} sub icon="game" color="blue" title="Aim Key" subtitle="When the aimbot works">
              <${Select} options=${state.aimKeys.map((label, value) => ({ value, label }))} value=${s.aimKey} onChange=${v => set({ aimKey: v })} width=${200} />
            <//>
            <${PowerRow} sub icon="key" color="yellow" title="Own Aim Key"
                subtitle=${keyValid ? "Any key: mouse4, mouse5, x, alt, f… · bound in CS2 on this PC" : "Letters, numbers and _ only (e.g. mouse4)"}
                subtitleClass=${keyValid ? "" : "warn"}>
              <${TextField} value=${keyText} onInput=${typeKey} placeholder="e.g. mouse4" width=${130} maxLength=${16} />
            <//>
          <//>
        <//>

        <${Section} title="Triggerbot">
          ${power("trigger", "bolt", "orange", "Triggerbot", "Shoots when an enemy crosses your crosshair, after a short random delay")}
        <//>

        <${Section} title="Targeting" note=${s.aimbot || s.trigger ? "for the aimbot and triggerbot" : "turn on the aimbot or triggerbot"}>
          <div class=${"hk-targeting" + (s.aimbot || s.trigger ? "" : " hk-dim")} inert=${!(s.aimbot || s.trigger)}>
            <${PowerRow} icon="person" color="pink" title="Body Part" subtitle="Where the aimbot and triggerbot go">
              <${Segmented} options=${state.aimBones} value=${s.aimBone} onChange=${v => set({ aimBone: v })} />
            <//>
            <${PowerRow} icon="target" color="purple" title="Aim At" subtitle="Who gets picked first">
              <${Select} options=${state.aimTargets.map((label, value) => ({ value, label }))} value=${s.aimTarget} onChange=${v => set({ aimTarget: v })} width=${190} />
            <//>
            <div class="row with-icon hk-slider-row">
              <div class="row-icon" style="--c: var(--red)"><${Glyph} name="sliders" /></div>
              <div class="text">
                <div class="title">Aggression</div>
                <div class="subtitle">1 gentle and human · 10 instant lock. Also shortens the trigger delay</div>
                <${Slider} min=${1} max=${10} value=${aggression ?? s.aggression} format=${aggressionText}
                  tint=${`color-mix(in srgb, var(--green), var(--red) ${((aggression ?? s.aggression) - 1) * 11}%)`}
                  onChange=${setAggression} onCommit=${v => set({ aggression: v }).then(() => setAggression(null))} />
              </div>
            </div>
            ${power("aimWalls", "wall", "indigo", "Aim Through Walls", "Aimbot and triggerbot also lock through walls")}
          </div>
        <//>
      </div>

      <div class="stack">
        <${Section} title="Visuals">
          ${power("chams", "eye", "green", "Chams", "Solid coloured player models, visible through walls")}
          ${power("boxEsp", "cube", "teal", "Box ESP", "3D box around enemies and teammates")}
          ${power("boneEsp", "skeleton", "blue", "Skeleton ESP", "Pose stick-figure through walls")}
          ${power("tags", "tag", "purple", "Name Tags", "Name, HP, armor, gun and grenades above each player")}
          ${power("radar", "radar", "green", "Radar Hack", "Every enemy shows on your radar, even when they are quiet")}
          ${power("enemyInfo", "info", "indigo", "Enemy Info", "Three nearest enemies: name, HP, armor, gun, utility, distance")}
          ${power("noFlash", "flash", "yellow", "No Flash", "Flashbangs don't blind you, and smokes are not drawn for you")}
        <//>

        <${Section} title="Weapons">
          ${power("noRecoil", "recoil", "orange", "Recoil Control", "Pulls the spray back onto the crosshair for you")}
          ${power("noSpread", "spread", "pink", "Zero Spread", "Every bullet goes where you aim · whole server")}
          ${power("ammo", "infinity", "blue", "Infinite Ammo", "Never reload")}
          ${power("damage", "bullet", "red", "Damage Boost", "2× damage")}
        <//>

        <${Section} title="Movement">
          ${power("speed", "run", "teal", "Speedhack", "Run 35% faster")}
          ${power("bhop", "bunny", "green", "Bunnyhop", "Hold jump to bhop perfectly")}
        <//>
      </div>
    </div>

    <${Section} class="hk-ingame" title="In Game" footer="Type the commands in chat. Click a bind to copy it, then paste it in the CS2 console.">
      <${Row} icon=${{ name: "server", color: state.running ? "green" : "indigo" }} title=${live.title} subtitle=${live.text}
        subtitleClass=${state.running ? "hk-good" : ""} />
      ${commands.map(c => html`<div class="row hk-cmd">
        <code class="hk-chip">${c.cmd}</code>
        <div class="text"><div class="subtitle">${c.text}</div></div>
        ${c.hint && html`<button class="hk-bind" onClick=${() => copy(c.copy)} title="Copy">
          <span class="mono">${c.hint}</span><${Icon} name="copy" size=${14} />
        </button>`}
      </div>`)}
    <//>
  <//>`;
}
