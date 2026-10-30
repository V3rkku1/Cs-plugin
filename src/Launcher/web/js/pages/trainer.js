// Trainer: built-in and saved grenade lineups, site drills against bots, a reaction drill, movement and
// aim practice (Trainer.cs in the plugin), plus a reaction test right here in the launcher.
import { html, useState, useRef, useEffect } from "../vendor/preact-htm.js";
import { call, tryCall, useApi } from "../api.js";
import { Page, Section, Row, Button, Segmented, Stepper, TextField, Select, Icon } from "../ui.js";
import { usePlay, Health } from "./play.js";
import { LINEUPS } from "../lineups-data.js";

const send = command => tryCall("live.command", command);

const DRILLS = [
  { id: "clear", icon: "crosshair", color: "orange", title: "Clear Site", text: "Bots hold angles on the site. Clear it and plant." },
  { id: "retake", icon: "bolt", color: "red", title: "Plant & Hold", text: "You plant, bots wait at CT spawn and retake once the bomb is down." },
  { id: "hold", icon: "shield", color: "blue", title: "Hold Site", text: "You start on the site as CT, bots push it." },
  { id: "prefire", icon: "clock", color: "yellow", title: "Prefire", text: "Bots stand on site angles and don't shoot. Clear them: chat shows your time." },
  { id: "clutch", icon: "person", color: "pink", title: "Clutch Retake", text: "The bomb is already planted and bots guard it. Retake alone and defuse." },
  { id: "reflex", icon: "target", color: "green", title: "Reaction Drill", text: "One bot pops up in front of you. Chat shows how fast you hit it." },
  { id: "flick", icon: "refresh", color: "purple", title: "Flick Drill", text: "One bot appears anywhere around you, behind you too. Turn and hit it." },
];
const REACTION = ["reflex", "flick"];
const WEAPONS = [["", "Own gun"], ["ak47", "AK-47"], ["m4a1_silencer", "M4A1-S"], ["awp", "AWP"], ["deagle", "Deagle"], ["usp_silencer", "USP-S"]];

// One click to a server set up for each kind of practice (the same settings as Live Server, kept for next time).
const OFF = { noRoundEnd: false, infiniteAmmo: false, allowCheats: false, botsDontShoot: false, headshotOnly: false, freeForAll: false, autoBhop: false };
const SETUPS = [
  { icon: "eye", color: "indigo", title: "Lineups & Utility", text: "Competitive, no bots, endless round, infinite grenades, cheats on",
    set: { ...OFF, mode: "Competitive", bots: 0, noRoundEnd: true, infiniteAmmo: true, allowCheats: true, maxMoney: true } },
  { icon: "map", color: "orange", title: "Site Drills", text: "Competitive with rounds and full money: then start a drill below",
    set: { ...OFF, mode: "Competitive", bots: 0, maxMoney: true } },
  { icon: "crosshair", color: "red", title: "Aim Warm-up", text: "Free-for-all Deathmatch against bots",
    set: { ...OFF, mode: "Deathmatch", freeForAll: true }, aim: true },
  { icon: "run", color: "teal", title: "Movement", text: "No bots, endless round, auto bunnyhop, no fall damage, cheats on",
    set: { ...OFF, mode: "Casual", bots: 0, noRoundEnd: true, autoBhop: true, noFallDamage: true, allowCheats: true } },
];

/** Map and practice setup, then Start (or Apply when the server already runs). */
function Launch({ play }) {
  const [map, setMap] = useState(LINEUPS[play.settings.map] ? play.settings.map : "de_mirage");
  const [aimBots, setAimBots] = useState(10);
  const [hs, setHs] = useState(false);
  const [shoot, setShoot] = useState(true);
  const running = play.testMode;
  const go = async setup => {
    const extra = setup.aim ? { bots: aimBots, headshotOnly: hs, botsDontShoot: !shoot } : {};
    // Hacker Mode off: aimbot kills don't count in Stats, and practice is about your own aim.
    if (await tryCall("live.set", { ...setup.set, ...extra, map, workshopMap: null, hackerMode: 0 }) === undefined) return;
    tryCall(running ? "play.apply" : "play.start");
  };
  return html`<${Section} title="Launch" note=${running ? `running: ${play.settings.mode} on ${play.status?.map ?? play.settings.map}` : "starts the server and CS2"}
      footer=${running ? "Pick a setup to switch the running server to it." : "CS2 opens and joins by itself when the map is up."}>
    <${Row} icon=${{ name: "map", color: "blue" }} title="Map">
      <${Select} options=${Object.keys(LINEUPS).map(m => ({ value: m, label: mapLabel(m) }))} value=${map} onChange=${setMap} width=${150} />
    <//>
    ${SETUPS.map(setup => html`<${Row} icon=${{ name: setup.icon, color: setup.color }} title=${setup.title} subtitle=${setup.text}>
      ${setup.aim && html`<div class="hstack" style="gap:8px">
        <${Stepper} value=${aimBots} min=${1} max=${20} onChange=${setAimBots} />
        <${Button} size="sm" kind=${hs ? "prominent" : "default"} onClick=${() => setHs(!hs)} title="Only headshots hurt">HS only<//>
        <${Button} size="sm" kind=${shoot ? "default" : "prominent"} onClick=${() => setShoot(!shoot)} title="Bots don't shoot back">Bots passive<//>
      </div>`}
      <${Button} size="sm" kind="prominent" icon="play" disabled=${!play.ready || play.busy} onClick=${() => go(setup)}>${running ? "Switch" : "Start"}<//>
    <//>`)}
    ${running && html`<${Row} icon=${{ name: "stop", color: "red" }} title="Stop the Server" subtitle="CS2 goes back to normal">
      <${Button} size="sm" kind="destructive" onClick=${() => tryCall("play.stop")}>Stop<//>
    <//>`}
  <//>`;
}

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
    if (phase === "go") {
      const ms = Math.round(performance.now() - shownAt.current);
      setTimes(t => [...t.slice(-9), ms]);
      setPhase("idle");
      call("trainer.reaction", ms).catch(() => {}); // for the Stats page; no CS2 folder yet is fine
      return;
    }
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
  const [weapon, setWeapon] = useState(0);
  if (!play) return html`<${Page} title="Trainer" />`;
  const up = !!play.status;
  const drill = id => send(`lst_drill ${id} ${site ? "b" : "a"} ${bots} ${REACTION.includes(id) ? WEAPONS[weapon][0] : ""}`.trim());

  return html`<${Page} title="Trainer"
      subtitle="Learn lineups, train your reactions, aim and movement, and practise sites against bots.">
    <div class="cols wrap" style="--n:2">
      <div class="stack">
        <${Health} />
        <${Launch} play=${play} />
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
          <${Row} icon=${{ name: "crosshair", color: "red" }} title="Gun" subtitle="for the reaction and flick drills">
            <${Select} options=${WEAPONS.map((w, i) => ({ value: i, label: w[1] }))} value=${weapon} onChange=${setWeapon} width=${140} />
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
          <${Row} icon=${{ name: "map", color: "orange" }} title="Course" subtitle="Stand on the start, then the finish, and set each: leaving the start starts the clock (!course)">
            <div class="action-grid" style="padding:0;grid-template-columns:repeat(3,72px)">
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_course start")}>Start<//>
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_course finish")}>Finish<//>
              <${Button} size="sm" disabled=${!up} onClick=${() => send("lst_course clear")}>Clear<//>
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
          <${Row} icon=${{ name: "crosshair", color: "yellow" }} title="Spray Score" subtitle="Spray 8+ bullets at a wall: chat says how far they landed from your first bullet · !spray">
            <${OnOff} up=${up} on="lst_spray 1" off="lst_spray 0" />
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
