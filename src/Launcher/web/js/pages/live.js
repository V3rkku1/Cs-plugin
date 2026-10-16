// Live Server: the server settings (most used first) and the controls for the running match.
import { html, useState, useEffect } from "../vendor/preact-htm.js";
import { tryCall } from "../api.js";
import { Page, Section, Row, Button, Toggle, Segmented, Slider, Stepper, TextField, Select, Icon, Badge, confirm, prompt } from "../ui.js";
import { usePlay, go } from "./play.js";
import { LogView } from "./setup.js";

const set = (key, value) => tryCall("live.set", { [key]: value });
const send = command => tryCall("live.command", command);

/** A text field that saves when you press Enter or leave it. */
function CommitField({ value, onCommit, ...props }) {
  const [text, setText] = useState(null);
  useEffect(() => setText(null), [value]);
  const commit = () => { if (text != null && text !== value) onCommit(text); };
  return html`<div onFocusOut=${commit}><${TextField} ...${props} value=${text ?? value} onInput=${setText} onEnter=${commit} /></div>`;
}

/** Slider that sends its value when released, and shows the dragged value meanwhile. */
function LiveSlider({ value, onCommit, ...props }) {
  const [v, setV] = useState(null);
  useEffect(() => setV(null), [value]);
  return html`<${Slider} ...${props} value=${v ?? value} onChange=${setV} onCommit=${onCommit} />`;
}


function StatusCard({ state }) {
  const color = { good: "var(--green)", warning: "var(--orange)", busy: "var(--blue)", off: "var(--text-3)" }[state.liveKind];
  return html`<div class="live-status glass" style=${`--c:${color}`}>
    <span class=${"live-dot" + (state.liveKind === "off" ? "" : " on")}></span>
    <div class="text">
      <div class="title">${state.liveTitle}</div>
      <div class="subtitle">${state.liveDetail}</div>
    </div>
    ${state.startError && html`<div class="live-error"><${Icon} name="warning" size=${15} stroke=${2} />${state.startError}</div>`}
    ${state.status?.players?.length > 0 && html`<div class="players">${state.status.players.map(p => html`<${Badge} color="green">${p}<//>`)}</div>`}
  </div>`;
}

function ActionGrid({ actions, disabled }) {
  return html`<div class="action-grid">
    ${actions.map(([label, command, icon]) => html`<${Button} size="sm" icon=${icon} disabled=${disabled}
      onClick=${() => typeof command === "function" ? command() : send(command)}>${label}<//>`)}
  </div>`;
}

export function LivePage() {
  const [state] = usePlay();
  const [give, setGive] = useState(null);
  const [command, setCommand] = useState("");
  if (!state) return html`<${Page} title="Live Server" />`;

  const s = state.settings;
  const on = state.testMode;
  const up = !!state.status;
  const retakes = s.mode === "Retakes";
  const plugin = retakes && s.retakeStyle === 1;
  const buy = s.retakeWeapons !== 1;
  const owns = retakes || s.mode === "Arms Race" || s.mode === "Demolition";
  const mode = state.modes.find(m => m.name === s.mode) ?? state.modes[0];
  const maps = mode.maps.includes(s.map) ? mode.maps : [...mode.maps, s.map];
  const giveItem = give ?? state.give[0]?.classname;

  const pickMap = async v => {
    if (v !== "__other") return set("map", v);
    const name = await prompt({ title: "Other Map", message: "Type a map name from your CS2 files, e.g. de_cache.", value: s.map, placeholder: "de_…", ok: "Use Map" });
    if (name?.trim()) set("map", name.trim());
  };
  const reset = async () => {
    if (await confirm({ title: "Reset settings?", message: "Put all game, bot and rule settings back to the defaults?\nFriends and launch options are kept.", ok: "Reset", danger: true }))
      tryCall("live.reset");
  };
  const run = () => {
    if (!command.trim()) return;
    send(command);
    setCommand("");
  };

  const headerActions = on
    ? html`<${Button} kind="prominent" icon="refresh" onClick=${() => tryCall("play.apply")} title="Send these settings to the running server (F5)">Apply Changes<//>
        <${Button} kind="destructive" icon="stop" onClick=${() => tryCall("play.stop")} title="Shift+F5">Stop<//>`
    : html`<${Button} kind="prominent" icon="play" disabled=${!state.ready} busy=${state.busy} onClick=${() => tryCall("play.start")} title="F5">Start<//>`;

  return html`<${Page} title="Live Server" subtitle="Set the mode, bots and rules, then run the match while it is up." actions=${headerActions}>
    <${StatusCard} state=${state} />

    <div class="cols wrap live-cols" style="--n:2">
      <div class="stack">
        <${Section} title="Game" footer=${on ? "Changes reach the server when you press Apply Changes." : "Used the next time you press Start."}>
          <${Row} icon=${{ name: "game", color: "purple" }} title="Game Mode">
            <${Select} width=${220} value=${s.mode} options=${state.modes.map(m => m.name)} onChange=${v => set("mode", v)} />
          <//>
          <${Row} icon=${{ name: "map", color: "teal" }} title="Map" subtitle=${s.workshopMap ? "The Workshop map below is used instead" : ""}>
            <${Select} width=${220} value=${s.map} onChange=${pickMap}
              options=${[...maps.map(m => ({ value: m, label: m })), { value: "__other", label: "Other map…" }]} />
          <//>
          <${Row} icon=${{ name: "globe", color: "blue" }} title="Workshop Map" subtitle=${state.workshop.note}
              subtitleClass=${"note-" + state.workshop.kind}>
            <${CommitField} width=${170} value=${s.workshopMap} placeholder="ID or link (optional)" onCommit=${v => set("workshopMap", v)} />
            <${Button} size="sm" disabled=${!state.workshop.id} onClick=${() => tryCall("live.subscribe")} title="Open it in Steam to subscribe (download)">Subscribe<//>
          <//>
        <//>

        ${retakes && html`<${Section} title="Retakes" footer=${plugin ? "Plugin settings are read when the server starts: Apply restarts it when they change." : ""}>
          <${Row} icon=${{ name: "clock", color: "red" }} title="Style" subtitle="Valve's cards, or the cs2-retakes plugin">
            <${Segmented} options=${state.retakeStyles} value=${s.retakeStyle} onChange=${v => set("retakeStyle", v)} />
          <//>
          ${!plugin && html`<${Row} title="Valve Retakes" subtitle="3 terrorists vs 4 counter-terrorists. Loadout cards each round." />`}
          ${plugin && html`
            <${Row} title="Max Players" subtitle="Seats in the match, including you (2-10)">
              <${Stepper} value=${s.retakeMaxPlayers} min=${2} max=${10} onChange=${v => set("retakeMaxPlayers", v)} /><//>
            <${Row} title="Terrorists" subtitle="Percent of the players on T">
              <${Stepper} value=${s.retakeTerroristPercent} min=${10} max=${80} step=${5} format=${v => v + "%"} parse=${t => parseInt(t)} onChange=${v => set("retakeTerroristPercent", v)} /><//>
            <${Row} title="Guns" subtitle="Buy your own, or pistol / half-buy / rifle rounds">
              <${Segmented} options=${["Buy", "Allocator"]} value=${s.retakeWeapons === 1 ? 1 : 0} onChange=${v => set("retakeWeapons", v)} /><//>
            ${buy
              ? html`<${Row} title="Buy Time" subtitle="Seconds to buy. Your guns come back next round">
                  <${Stepper} value=${s.retakeBuySeconds} min=${4} max=${20} onChange=${v => set("retakeBuySeconds", v)} /><//>`
              : html`<${Row} title="Allocator" subtitle="Type !guns to choose. One AWP per team." />`}
            <${Row} title="Bomb Timer" subtitle="Seconds until the bomb explodes">
              <${Stepper} value=${s.retakeBombTimer} min=${15} max=${60} onChange=${v => set("retakeBombTimer", v)} /><//>
            <${Row} title="Instadefuse" subtitle="The bomb defuses instantly when nothing can stop it">
              <${Toggle} value=${s.retakeInstadefuse} onChange=${v => set("retakeInstadefuse", v)} /><//>
            <${Row} title="Auto Plant" subtitle="The bomb is planted as the round starts">
              <${Toggle} value=${s.retakeAutoPlant} onChange=${v => set("retakeAutoPlant", v)} /><//>
            <${Row} title="Announce Site" subtitle="Show which site the bomb is on">
              <${Toggle} value=${s.retakeAnnounce} onChange=${v => set("retakeAnnounce", v)} /><//>
            <${Row} title="Join Straight In" subtitle="Skip the team menu">
              <${Toggle} value=${s.retakeAutoJoin} onChange=${v => set("retakeAutoJoin", v)} /><//>
            <${Row} title="Balance Teams" subtitle="Keep the T and CT counts even">
              <${Toggle} value=${s.retakeBalance} onChange=${v => set("retakeBalance", v)} /><//>
            <${Row} title="Scramble" subtitle="Mix the teams after a win streak">
              <${Toggle} value=${s.retakeScramble} onChange=${v => set("retakeScramble", v)} /><//>
            <${Row} title="Scramble After" subtitle="Rounds won in a row">
              <${Stepper} value=${s.retakeScrambleRounds} min=${1} max=${30} disabled=${!s.retakeScramble} onChange=${v => set("retakeScrambleRounds", v)} /><//>`}
        <//>`}

        <${Section} title="Bots">
          <${Row} icon=${{ name: "bot", color: "orange" }} title="Bots">
            <${LiveSlider} min=${0} max=${20} value=${s.bots} width=${230} tint="var(--orange)" format=${v => v === 0 ? "None" : v}
              onCommit=${v => set("bots", v)} />
          <//>
          <${Row} icon=${{ name: "star", color: "yellow" }} title="Skill">
            <${Segmented} options=${state.difficulties} value=${s.difficulty} onChange=${v => set("difficulty", v)} />
          <//>
          <${Row} icon=${{ name: "friends", color: "blue" }} title="Team"
              subtitle=${plugin ? "The plugin splits the teams. This picker is ignored." : retakes ? "Valve fills both sides (3 T vs 4 CT)." : "0 bots = friends only"}>
            <${Select} width=${200} disabled=${retakes} value=${s.botTeam} options=${state.botTeams.map((t, i) => ({ value: i, label: t }))} onChange=${v => set("botTeam", v)} />
          <//>
          <${Row} icon=${{ name: "crosshair", color: "red" }} title="Weapons" subtitle=${owns ? "This mode hands out its own guns" : ""}>
            <${Select} width=${200} disabled=${owns} value=${s.botWeapons} options=${state.botWeapons.map((t, i) => ({ value: i, label: t }))} onChange=${v => set("botWeapons", v)} />
          <//>
          <${Row} icon=${{ name: "sun", color: "green" }} title="Peaceful Bots" subtitle="Bots don't shoot - look at your skins in peace">
            <${Toggle} value=${s.botsDontShoot} onChange=${v => set("botsDontShoot", v)} />
          <//>
        <//>

        <${Section} title="Rules">
          <${Row} icon=${{ name: "shuffle", color: "purple" }} title="Free-for-All" subtitle=${s.mode === "Deathmatch" ? "Deathmatch: everyone is an enemy" : "Only in Deathmatch"}>
            <${Toggle} value=${s.freeForAll} disabled=${s.mode !== "Deathmatch"} onChange=${v => set("freeForAll", v)} /><//>
          <${Row} icon=${{ name: "tag", color: "green" }} title="Max Money" subtitle=${owns ? "This mode handles money itself" : "And buy anywhere"}>
            <${Toggle} value=${s.maxMoney} disabled=${owns} onChange=${v => set("maxMoney", v)} /><//>
          <${Row} icon=${{ name: "bolt", color: "orange" }} title="Infinite Ammo"><${Toggle} value=${s.infiniteAmmo} onChange=${v => set("infiniteAmmo", v)} /><//>
          <${Row} icon=${{ name: "refresh", color: "teal" }} title="Endless Rounds" subtitle=${owns ? "This mode ends the round on its own" : "Rounds never end, respawn on death"}>
            <${Toggle} value=${s.noRoundEnd} disabled=${owns} onChange=${v => set("noRoundEnd", v)} /><//>
          <${Row} icon=${{ name: "key", color: "yellow" }} title="Allow Cheats" subtitle="Needed for third person and noclip">
            <${Toggle} value=${s.allowCheats} onChange=${v => set("allowCheats", v)} /><//>
          <${Row} icon=${{ name: "friends", color: "red" }} title="Friendly Fire"><${Toggle} value=${s.friendlyFire} onChange=${v => set("friendlyFire", v)} /><//>
          <${Row} icon=${{ name: "clock", color: "blue" }} title="Round Time" subtitle=${owns ? "This mode keeps its own round length" : "Minutes · 0 = game mode default"}>
            <${Stepper} value=${s.roundTime} min=${0} max=${60} disabled=${owns} onChange=${v => set("roundTime", v)} /><//>
          <${Row} icon=${{ name: "target", color: "pink" }} title="Damage Report" subtitle="Chat shows the damage you deal">
            <${Toggle} value=${s.showDamage} onChange=${v => set("showDamage", v)} /><//>
        <//>

        <${Section} title="Hacker Mode">
          <${Row} icon=${{ name: "hacker", color: "green" }} title="Hacker Mode" subtitle=${state.hackerDetail}
              subtitleClass=${s.hackerMode !== 0 ? "note-good" : ""} onClick=${() => go("hacker")}>
            <${Button} size="sm" onClick=${e => { e.stopPropagation(); go("hacker"); }}>Open<//>
          <//>
        <//>

        <${Section} title="Launch">
          <${Row} icon=${{ name: "play", color: "blue" }} title="Open CS2 Automatically" subtitle="Off = start only the server">
            <${Toggle} value=${s.autoStartGame} onChange=${v => set("autoStartGame", v)} />
          <//>
          <${Row} icon=${{ name: "terminal", color: "indigo" }} title="Launch Options" subtitle="Extra options for the game window">
            <${CommitField} width=${230} value=${s.gameArgs} placeholder="-windowed -w 1920 -h 1080" onCommit=${v => set("gameArgs", v)} />
          <//>
          <${Row} icon=${{ name: "refresh", color: "red" }} title="Reset Settings" subtitle="Back to the defaults (keeps friends and launch options)">
            <${Button} size="sm" kind="destructive" onClick=${reset}>Reset<//>
          <//>
        <//>
      </div>

      <div class="stack">
        <${Section} title="Round" note=${up ? "" : "unlocks when the server is up"}>
          <${ActionGrid} disabled=${!up} actions=${[
            ["Restart", "mp_restartgame 1", "refresh"], ["End Warmup", "mp_warmup_end", "clock"],
            ["Pause", "lst_pause on", "stop"], ["Resume", "lst_pause off", "play"], ["Swap Teams", "lst_swap", "shuffle"],
            ["CT Win", "lst_endround ct", "shield"], ["T Win", "lst_endround t", "target"]]} />
        <//>
        <${Section} title="Players">
          <${ActionGrid} disabled=${!up} actions=${[
            ["Add CT", "bot_add_ct", "plus"], ["Add T", "bot_add_t", "plus"], ["Kick Bots", "bot_kick", "minus"],
            ["Respawn", "lst_respawn", "run"], ["God Mode", "lst_god", "shield"]]} />
        <//>
        <${Section} title="Practice">
          <${ActionGrid} disabled=${!up} actions=${[
            ["Third Person", "lst_view third", "eye"], ["First Person", "lst_view first", "person"], ["Noclip", "lst_noclip", "sparkles"],
            ["Armor", "lst_armor", "shield"], ["Nades", "lst_nades", "target"], ["Clear", "lst_clear", "trash"],
            ["Rethrow", "lst_rethrow", "refresh"], ["Save Spot", "lst_spot save", "save"], ["Load Spot", "lst_spot load", "import"],
            ["Refresh", "lst_refresh", "refresh"], [s.showDamage ? "Damage: On" : "Damage: Off", () => tryCall("live.damage"), "bolt"]]} />
        <//>
        <${Section} title="Give and Command" footer="Commands go to the server console. Enter sends.">
          <${Row} icon=${{ name: "crosshair", color: "orange" }} title="Give Weapon">
            <${Select} width=${190} disabled=${!up} value=${giveItem} options=${state.give.map(g => ({ value: g.classname, label: g.name }))} onChange=${setGive} />
            <${Button} size="sm" disabled=${!up} onClick=${() => tryCall("live.give", giveItem)}>Give<//>
          <//>
          <div class="row">
            <div class="row-icon" style="--c: var(--indigo)"><${Icon} name="terminal" size=${18} stroke=${2} /></div>
            <div class="grow"><${TextField} value=${command} onInput=${setCommand} onEnter=${run} placeholder=${up ? "Any server command, e.g. mp_roundtime 5" : "Server not running"} /></div>
            <${Button} size="sm" kind="prominent" disabled=${!up || !command.trim()} onClick=${run}>Run<//>
          </div>
        <//>
        <${Section} title="Activity">
          <${LogView} height=${300} />
        <//>
      </div>
    </div>
  <//>`;
}
