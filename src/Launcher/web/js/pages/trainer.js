// Trainer: built-in and saved grenade lineups, site drills against bots, a reaction drill, movement and
// aim practice (Trainer.cs in the plugin), plus a reaction test right here in the launcher.
import { html, useState, useRef, useEffect } from "../vendor/preact-htm.js";
import { tryCall, useApi } from "../api.js";
import { Page, Section, Row, Button, Segmented, Stepper, TextField, Select, Icon } from "../ui.js";
import { usePlay } from "./play.js";
import { LINEUPS } from "../lineups-data.js";

const send = command => tryCall("live.command", command);

const DRILLS = [
  { id: "clear", icon: "crosshair", color: "orange", title: "Clear Site", text: "Bots hold angles on the site. Clear it and plant." },
  { id: "retake", icon: "bolt", color: "red", title: "Plant & Hold", text: "You plant, bots wait at CT spawn and retake once the bomb is down." },
  { id: "hold", icon: "shield", color: "blue", title: "Hold Site", text: "You start on the site as CT, bots push it." },
  { id: "reflex", icon: "target", color: "green", title: "Reaction Drill", text: "One bot pops up in front of you. Chat shows how fast you hit it." },
];

// Cheats, endless round, free buys, infinite grenades, the trajectory preview while you hold a grenade,
// and the air time in chat when a grenade pops.
const PRACTICE_ON = "sv_cheats 1; mp_warmup_end; bot_kick; mp_roundtime 60; mp_roundtime_defuse 60; mp_freezetime 0; " +
  "mp_ignore_round_win_conditions 1; mp_buy_anywhere 1; mp_buytime 99999; mp_maxmoney 65535; mp_startmoney 65535; " +
  "sv_infinite_ammo 1; ammo_grenade_limit_total 5; sv_grenade_trajectory_prac_pipreview 1; " +
  "sv_grenade_trajectory_prac_trailtime 6; lst_nadeinfo 1; mp_restartgame 1";
const PRACTICE_OFF = "sv_infinite_ammo 0; sv_grenade_trajectory_prac_pipreview 0; mp_ignore_round_win_conditions 0; " +
  "mp_buy_anywhere 0; lst_nadeinfo 0; mp_restartgame 1";

const NADES = [["all", "All"], ["smoke", "Smoke"], ["flash", "Flash"], ["molotov", "Molotov"]];
const SIDES = [["all", "Both"], ["t", "T"], ["ct", "CT"]];
const nadeLook = { smoke: ["eye", "indigo"], flash: ["sun", "yellow"], molotov: ["bolt", "red"], he: ["target", "orange"] };
const mapLabel = m => m.replace(/^de_/, "").replace(/^./, c => c.toUpperCase());

/** On / Off buttons for a setting the page doesn't track. */
const OnOff = ({ up, on, off }) => html`<div class="action-grid" style="padding:0;grid-template-columns:repeat(2,64px)">
  <${Button} size="sm" disabled=${!up} onClick=${() => send(on)}>On<//>
  <${Button} size="sm" disabled=${!up} onClick=${() => send(off)}>Off<//>
</div>`;

/** Click when it turns green. Times are from the browser, so they include your mouse and screen. */
function ReactionTest() {
  const [phase, setPhase] = useState("idle"); // idle | wait | go | early
  const [times, setTimes] = useState([]);
  const shownAt = useRef(0);
  const timer = useRef();
  useEffect(() => () => clearTimeout(timer.current), []);

  const press = () => {
    if (phase === "wait") { clearTimeout(timer.current); setPhase("early"); return; }
    if (phase === "go") { setTimes(t => [...t.slice(-9), Math.round(performance.now() - shownAt.current)]); setPhase("idle"); return; }
    setPhase("wait");
    timer.current = setTimeout(() => { shownAt.current = performance.now(); setPhase("go"); }, 1200 + Math.random() * 2800);
  };

  const last = times.at(-1);
  const avg = times.length ? Math.round(times.reduce((a, b) => a + b, 0) / times.length) : null;
  const look = {
    idle: ["var(--blue)", last != null ? `${last} ms · click to go again` : "Click to start"],
    wait: ["var(--red)", "Wait for green…"],
    go: ["var(--green)", "CLICK!"],
    early: ["var(--orange)", "Too soon. Click to try again"],
  }[phase];
  return html`<div style="padding: 14px">
    <button onPointerDown=${press} style=${`width:100%;height:150px;border:0;border-radius:14px;cursor:pointer;font-size:22px;font-weight:700;color:#fff;background:${look[0]}`}>
      ${look[1]}
    </button>
    <div class="subtitle" style="margin-top:10px">
      ${times.length ? `Average ${avg} ms · best ${Math.min(...times)} ms · last ${times.length}` : "Pros are around 150–200 ms here."}
    </div>
  </div>`;
}

function Lineups({ up, running }) {
  const [pick, setPick] = useState(null);
  const [nade, setNade] = useState(0);
  const [side, setSide] = useState(0);
  const [query, setQuery] = useState("");
  const map = pick ?? (LINEUPS[running] ? running : "de_mirage");
  const [saved] = useApi("trainer.lineups", map, ["play.tick"]);
  const onMap = running === map;
  const q = query.trim().toLowerCase();

  const list = LINEUPS[map].filter(([name, type, s]) =>
    (nade === 0 || type === NADES[nade][0]) && (side === 0 || s === SIDES[side][0]) && (!q || name.toLowerCase().includes(q)));
  const go = ([name, type, , x, y, z, pitch, yaw, how]) =>
    send(`lst_goto ${x} ${y} ${z} ${pitch} ${yaw} ${type} ${name} - ${how.replaceAll("·", ",")}`);

  return html`<${Section} title="Lineups" note=${onMap ? "click Go: you stand on the spot, aimed, grenade in hand" : running ? `server is on ${running}` : "start the server to practise"}
      footer="Built-in lineups: positions and angles from cs2util.com (? opens the lineup with pictures). Your own: throw, then !savenade name in chat.">
    <div class="row" style="gap:8px;flex-wrap:wrap">
      <${Select} options=${Object.keys(LINEUPS).map(m => ({ value: m, label: mapLabel(m) }))} value=${map} onChange=${setPick} width=${130} />
      <${Segmented} options=${NADES.map(n => n[1])} value=${nade} onChange=${setNade} />
      <${Segmented} options=${SIDES.map(s => s[1])} value=${side} onChange=${setSide} />
      <div class="grow"><${TextField} value=${query} onInput=${setQuery} placeholder="Search: window, jungle, ct…" icon="search" /></div>
    </div>
    ${!onMap && running && html`<${Row} icon=${{ name: "warning", color: "orange" }} title=${`These are ${mapLabel(map)} lineups`}
        subtitle=${`Host ${map} on the Play or Live Server page to use them.`} />`}
    ${(saved ?? []).map(l => html`<${Row} icon=${{ name: "save", color: "green" }} title=${l.name} subtitle=${`Saved by you · ${l.weapon}`}>
      <${Button} size="sm" kind="prominent" disabled=${!up || !onMap} onClick=${() => send(`lst_lineup ${l.name}`)}>Go<//>
      <${Button} size="sm" kind="plain" disabled=${!up || !onMap} onClick=${() => send(`lst_delnade ${l.name}`)}><${Icon} name="trash" size=${14} /><//>
    <//>`)}
    <div style="max-height:460px;overflow:auto">
      ${list.map(l => html`<${Row} icon=${{ name: nadeLook[l[1]][0], color: nadeLook[l[1]][1] }} title=${l[0]} subtitle=${`${l[2].toUpperCase() || "Any"} · ${l[8]}`}>
        <${Button} size="sm" kind="plain" title="Pictures of the lineup" onClick=${() => tryCall("app.openUrl", `https://www.cs2util.com${l[9]}`)}>?<//>
        <${Button} size="sm" kind="prominent" disabled=${!up || !onMap} onClick=${() => go(l)}>Go<//>
      <//>`)}
      ${list.length === 0 && html`<${Row} icon=${{ name: "info", color: "indigo" }} title="No lineups match" />`}
    </div>
  <//>`;
}

export function TrainerPage() {
  const [play] = usePlay();
  const [site, setSite] = useState(0);
  const [bots, setBots] = useState(3);
  if (!play) return html`<${Page} title="Trainer" />`;
  const up = !!play.status;
  const drill = id => send(`lst_drill ${id} ${site ? "b" : "a"} ${bots}`);

  return html`<${Page} title="Trainer"
      subtitle="Learn lineups, train your reactions, aim and movement, and practise sites against bots.">
    <div class="cols wrap" style="--n:2">
      <div class="stack">
        <${Lineups} up=${up} running=${play.status?.map ?? ""} />

        <${Section} title="Grenade Practice" footer="Practice Mode: endless round, free buys anywhere, infinite grenades, no bots, air time in chat when a grenade pops.">
          <${Row} icon=${{ name: "sparkles", color: "purple" }} title="Practice Mode" subtitle="Everything below in one click">
            <${OnOff} up=${up} on=${PRACTICE_ON} off=${PRACTICE_OFF} />
          <//>
          <${Row} icon=${{ name: "eye", color: "teal" }} title="Trajectory Preview" subtitle="Shows where the grenade goes while you hold it (cheats on)">
            <${OnOff} up=${up} on="sv_cheats 1; sv_grenade_trajectory_prac_pipreview 1" off="sv_grenade_trajectory_prac_pipreview 0" />
          <//>
          <${Row} icon=${{ name: "clock", color: "blue" }} title="Air Time in Chat" subtitle="How long each grenade flew before it popped">
            <${OnOff} up=${up} on="lst_nadeinfo 1" off="lst_nadeinfo 0" />
          <//>
          <${Row} icon=${{ name: "refresh", color: "orange" }} title="Rethrow / Clear" subtitle="Back to your last throw · remove smokes and fires">
            <div class="action-grid" style="padding:0;grid-template-columns:repeat(2,80px)">
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_rethrow")}>Rethrow<//>
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_clear")}>Clear<//>
            </div>
          <//>
        <//>

        <${Section} title="Reaction Test" note="in the launcher, no server needed">
          <${ReactionTest} />
        <//>
      </div>

      <div class="stack">
        <${Section} title="Site Drills" note=${up ? "Competitive or Casual on a de_ map" : "start the server on the Play page"}
            footer="A drill changes bots and teams until you stop it. Apply Changes on Live Server brings your setup back.">
          <${Row} icon=${{ name: "map", color: "indigo" }} title="Site">
            <${Segmented} options=${["A", "B"]} value=${site} onChange=${setSite} />
          <//>
          <${Row} icon=${{ name: "bot", color: "teal" }} title="Bots" subtitle="for the site drills">
            <${Stepper} value=${bots} min=${1} max=${5} onChange=${setBots} />
          <//>
          ${DRILLS.map(d => html`<${Row} icon=${{ name: d.icon, color: d.color }} title=${d.title} subtitle=${d.text}>
            <${Button} size="sm" kind="prominent" disabled=${!up} onClick=${() => drill(d.id)}>Start<//>
          <//>`)}
          <${Row} icon=${{ name: "stop", color: "indigo" }} title="Stop Drill">
            <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_drill off")}>Stop<//>
          <//>
        <//>

        <${Section} title="Movement" footer="Sync: how much of your turning matched the strafe key (A turning left, D turning right). Aim for 80%+.">
          <${Row} icon=${{ name: "run", color: "teal" }} title="Jump Stats" subtitle="Speed, jump distance, pre-speed, gain, strafe sync and bhop chain on screen · !speed">
            <${OnOff} up=${up} on="lst_speedo 1" off="lst_speedo 0" />
          <//>
          <${Row} icon=${{ name: "shuffle", color: "green" }} title="Easy Bhop" subtitle="Hold jump to hop, no speed loss on landing">
            <${OnOff} up=${up} on="sv_autobunnyhopping 1; sv_enablebunnyhopping 1; sv_staminajumpcost 0; sv_staminalandcost 0"
              off="sv_autobunnyhopping 0; sv_enablebunnyhopping 0; sv_staminajumpcost 0.08; sv_staminalandcost 0.05" />
          <//>
          <${Row} icon=${{ name: "save", color: "blue" }} title="Checkpoint" subtitle="Save where you stand, teleport back for another try (!save, !load)">
            <div class="action-grid" style="padding:0;grid-template-columns:repeat(2,80px)">
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_spot save")}>Save<//>
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_spot load")}>Teleport<//>
            </div>
          <//>
          <${Row} icon=${{ name: "sparkles", color: "purple" }} title="Noclip" subtitle="Fly around to learn the map">
            <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_noclip")}>Toggle<//>
          <//>
        <//>

        <${Section} title="Aim">
          <${Row} icon=${{ name: "crosshair", color: "red" }} title="Counter-Strafe Check" subtitle="Each shot says if you were standing still and how fast you stopped · !cs">
            <${OnOff} up=${up} on="lst_cstrafe 1" off="lst_cstrafe 0" />
          <//>
          <${Row} icon=${{ name: "target", color: "orange" }} title="Show Impacts" subtitle="Where every bullet hit, to learn spray patterns (cheats on)">
            <${OnOff} up=${up} on="sv_cheats 1; sv_showimpacts 1; sv_showimpacts_time 4" off="sv_showimpacts 0" />
          <//>
          <${Row} icon=${{ name: "stop", color: "blue" }} title="Freeze Bots" subtitle="Bots stand still: practise headshots and crosshair placement">
            <${OnOff} up=${up} on="sv_cheats 1; bot_stop 1" off="bot_stop 0" />
          <//>
          <${Row} icon=${{ name: "person", color: "pink" }} title="Bots Copy You" subtitle="Bots mirror your movement: practise tracking strafing targets (cheats on)">
            <${OnOff} up=${up} on="sv_cheats 1; bot_mimic 1" off="bot_mimic 0" />
          <//>
        <//>
      </div>
    </div>
  <//>`;
}
