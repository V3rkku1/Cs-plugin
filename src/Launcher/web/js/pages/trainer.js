// Trainer: site drills against bots, a reaction drill, movement stats and saved lineups (Trainer.cs in the
// plugin), plus a reaction test right here in the launcher.
import { html, useState, useRef, useEffect } from "../vendor/preact-htm.js";
import { tryCall, useApi } from "../api.js";
import { Page, Section, Row, Button, Segmented, Stepper, Icon } from "../ui.js";
import { usePlay } from "./play.js";

const send = command => tryCall("live.command", command);

const DRILLS = [
  { id: "clear", icon: "crosshair", color: "orange", title: "Clear Site", text: "Bots hold angles on the site. Clear it and plant." },
  { id: "retake", icon: "bolt", color: "red", title: "Plant & Hold", text: "You plant, bots wait at CT spawn and retake once the bomb is down." },
  { id: "hold", icon: "shield", color: "blue", title: "Hold Site", text: "You start on the site as CT, bots push it." },
  { id: "reflex", icon: "target", color: "green", title: "Reaction Drill", text: "One bot pops up in front of you. Chat shows how fast you hit it." },
];

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

export function TrainerPage() {
  const [play] = usePlay();
  const [site, setSite] = useState(0);
  const [bots, setBots] = useState(3);
  const map = play?.status?.map ?? "";
  const [lineups] = useApi("trainer.lineups", map, ["play.tick"]);
  if (!play) return html`<${Page} title="Trainer" />`;
  const up = !!play.status;
  const drill = id => send(`lst_drill ${id} ${site ? "b" : "a"} ${bots}`);

  return html`<${Page} title="Trainer"
      subtitle="Learn lineups, train your reactions and movement, and practise sites against bots.">
    <div class="cols wrap" style="--n:2">
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

        <${Section} title="Movement" footer="Bunnyhop practice: turn on Auto Bunnyhop in Live Server › Server Plugins to learn strafing first.">
          <${Row} icon=${{ name: "run", color: "teal" }} title="Speedometer" subtitle="Speed, last jump distance, pre-speed and bhop chain on screen · !speed in chat">
            <div class="action-grid" style="padding:0">
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_speedo 1")}>On<//>
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_speedo 0")}>Off<//>
            </div>
          <//>
          <${Row} icon=${{ name: "sparkles", color: "purple" }} title="Noclip" subtitle="Fly around to learn the map">
            <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_noclip")}>Toggle<//>
          <//>
        <//>
      </div>

      <div class="stack">
        <${Section} title="Lineups" note=${map || "no map running"}
            footer="Save one in game: throw it, then !savenade name. Go puts everyone at the throw spot with that grenade.">
          ${(lineups ?? []).length === 0
            ? html`<${Row} icon=${{ name: "info", color: "indigo" }} title="No lineups on this map yet" subtitle="Throw a grenade, then type !savenade window in chat" />`
            : html`<${Row} icon=${{ name: "refresh", color: "blue" }} title="Practise All" subtitle="Each click goes to the next lineup">
                <${Button} size="sm" kind="prominent" disabled=${!up} onClick=${() => send("lst_lineup next")}>Next<//>
              <//>
              ${lineups.map(l => html`<${Row} icon=${{ name: "target", color: "orange" }} title=${l.name} subtitle=${l.weapon}>
                <${Button} size="sm" disabled=${!up} onClick=${() => send(`lst_lineup ${l.name}`)}>Go<//>
                <${Button} size="sm" kind="plain" disabled=${!up} onClick=${() => send(`lst_delnade ${l.name}`)}><${Icon} name="trash" size=${14} /><//>
              <//>`)}`}
          <${Row} icon=${{ name: "trash", color: "red" }} title="Clear Smokes and Fires">
            <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_clear")}>Clear<//>
          <//>
        <//>

        <${Section} title="Reaction Test" note="in the launcher, no server needed">
          <${ReactionTest} />
        <//>
      </div>
    </div>
  <//>`;
}
