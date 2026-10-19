// Play: start CS:GO legacy as a LAN game with SourceMod, with your unboxed skins on (kgns' weapons and gloves plugins).
import { html, useState } from "../vendor/preact-htm.js";
import { tryCall, useApi, navigate } from "../api.js";
import { Page, Section, Row, Button, Segmented, Stepper, TextField, Badge } from "../ui.js";

const MAPS = ["de_dust2", "de_mirage", "de_inferno", "de_nuke", "de_overpass", "de_ancient", "de_vertigo", "de_anubis", "de_train", "cs_office"];

export function CsgoPlayPage() {
  const [state] = useApi("legacy.state");
  const [pick, setPick] = useState(null);
  if (!state) return html`<${Page} title="Play" />`;
  const v = pick ?? { mode: state.mode, map: state.map, bots: state.bots };
  const set = change => setPick({ ...v, ...change });
  const ready = state.folder && state.steps.every(s => s.state === "ok");

  return html`<${Page} title="Play" subtitle="CS:GO on your PC as a LAN game, with the skins from your inventory."
      actions=${html`<${Button} kind="prominent" size="lg" icon="play" disabled=${!ready || state.busy || state.running}
        onClick=${() => tryCall("legacy.start", v)}>${state.running ? "CS:GO Is Running" : "Start CS:GO"}<//>`}>
    ${!ready && html`<${Section}>
      <${Row} icon=${{ name: "warning", color: "orange" }} title="Finish setup first"
          subtitle=${state.folder ? "Install SourceMod and the skin plugins on Setup." : "Install CS:GO legacy in Steam, then pick its folder on Setup."}>
        <${Button} size="sm" kind="prominent" onClick=${() => navigate("setup")}>Open Setup<//>
      <//>
    <//>`}
    <div class="cols wrap" style="--n:2">
      <div class="stack">
        <${Section} title="Game" footer=${state.running ? "Running - close CS:GO to start another game." : "Used when you press Start CS:GO."}>
          <${Row} icon=${{ name: "game", color: "purple" }} title="Mode">
            <${Segmented} cols=${3} options=${state.modes} value=${v.mode} onChange=${mode => set({ mode })} />
          <//>
          <${Row} icon=${{ name: "map", color: "teal" }} title="Map" subtitle="Any map in your CS:GO files">
            <${TextField} width=${170} value=${v.map} onInput=${map => set({ map })} placeholder="de_dust2" />
          <//>
          <div class="row block hstack" style="flex-wrap:wrap;gap:6px">
            ${MAPS.map(m => html`<${Button} key=${m} size="sm" kind=${v.map === m ? "prominent" : "default"} onClick=${() => set({ map: m })}>${m}<//>`)}
          </div>
          <${Row} icon=${{ name: "bot", color: "orange" }} title="Bots">
            <${Stepper} value=${v.bots} min=${0} max=${20} onChange=${bots => set({ bots })} />
          <//>
        <//>
      </div>
      <div class="stack">
        <${Section} title="Your Skins" footer="In game the plugins' own menus (!ws, !knife, !gloves) work too, but what you equip here wins the next time it syncs.">
          <${Row} icon=${{ name: "skins", color: "blue" }} title="Equip in the Inventory" subtitle="Guns and knife are the same on both sides in CS:GO; gloves can differ per side (Loadout tab)."
              onClick=${() => navigate("inventory")} />
          <${Row} icon=${{ name: "key", color: "yellow" }} title="Open Cases" subtitle="Free, as many as you like" onClick=${() => navigate("cases")} />
          <${Row} icon=${{ name: "refresh", color: "green" }} title="While you play" subtitle="An equip reaches CS:GO within a few seconds; you see it on your next spawn or new weapon." />
        <//>
        <${Section} title="Status">
          <${Row} icon=${{ name: "server", color: state.running ? "green" : "gray" }} title=${state.running ? "CS:GO is running" : "CS:GO is closed"}
              subtitle=${state.folder ?? "No CS:GO folder"}>
            ${state.running ? html`<${Badge} color="green">LAN · VAC off<//>` : null}
          <//>
        <//>
      </div>
    </div>
  <//>`;
}
