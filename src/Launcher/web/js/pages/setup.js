// Setup: CS2 folder, the install checklist, maintenance, folders and the activity log.
import { html, useState } from "../vendor/preact-htm.js";
import { tryCall, useApi, useEvent } from "../api.js";
import { Page, Section, Row, Button, Segmented, Toggle, StateIcon, Progress, LogView, confirm } from "../ui.js";

export function SetupPage() {
  const [state] = useApi("setup.state");
  const [progress, setProgress] = useState({});
  useEvent("setup.progress", ({ id, p }) => setProgress(old => ({ ...old, [id]: p })));
  if (!state) return html`<${Page} title="Setup" />`;

  const busy = state.busy;
  const allOk = state.steps.every(s => s.state === "ok");
  const uninstall = async () => {
    if (await confirm({
      title: "Uninstall everything?",
      message: "This removes Metamod, CounterStrikeSharp and the LAN Skin Tester plugin from your CS2 folder and puts gameinfo.gi back to normal.\n\nYour saved loadout is backed up to the launcher data folder.",
      ok: "Uninstall", danger: true,
    })) tryCall("setup.uninstall");
  };

  return html`<${Page} title="Setup" subtitle="Install, update or remove everything the skin test needs.">
    <div class="cols wrap" style="--n:2; grid-template-columns: minmax(0,1.25fr) minmax(0,1fr)">
      <div class="stack">
        <${Section} title="Game">
          <${Row} icon=${{ name: "game", color: "orange" }} title="Counter-Strike 2"
              subtitle=${state.folder ?? "Not found - press Browse or Find"} subtitleClass=${state.folder ? "" : "warn"}>
            <${Button} size="sm" onClick=${() => tryCall("setup.browse")}>Browse…<//>
            <${Button} size="sm" onClick=${() => tryCall("setup.find")}>Find<//>
          <//>
        <//>

        <${Section} title="Components" note=${allOk ? "everything is installed" : ""}>
          ${state.steps.map(step => {
            const p = progress[step.id];
            const running = p !== undefined && p !== null;
            return html`<div class="row" key=${step.id}>
              <${StateIcon} state=${running ? "busy" : step.state} />
              <div class="text">
                <div class="title">${step.title}</div>
                <div class="subtitle">${step.detail}</div>
                ${running && html`<${Progress} value=${p} />`}
              </div>
              <div class="accessory">
                <${Button} size="sm" kind=${step.state === "ok" ? "default" : "prominent"} disabled=${!step.enabled}
                  onClick=${() => tryCall("setup.step", step.id)}>${step.action}<//>
              </div>
            </div>`;
          })}
          <div class="row block">
            <${Button} kind="prominent" size="lg" block icon="download" disabled=${!state.hasGame || busy} busy=${busy}
              onClick=${() => tryCall("setup.all")}>${busy ? "Working…" : allOk ? "Reinstall Everything" : "Install Everything"}<//>
          </div>
        <//>

        <${Section} title="Maintenance">
          <${Row} icon=${{ name: "refresh", color: "blue" }} title="Check for Updates" subtitle="Compare with the newest Metamod and CounterStrikeSharp">
            <${Button} size="sm" disabled=${busy} onClick=${() => tryCall("setup.checkUpdates")}>Check<//>
          <//>
          <${Row} icon=${{ name: "zip", color: "indigo" }} title="Install from Zip" subtitle="If a download fails: get the zip from the links below">
            <${Button} size="sm" disabled=${!state.hasGame || busy} onClick=${() => tryCall("setup.zip")}>Choose Zip…<//>
          <//>
          <${Row} icon=${{ name: "wrench", color: "teal" }} title="Repair" subtitle="Reinstall the plugin and fix leftovers after a crash">
            <${Button} size="sm" disabled=${!state.hasGame || busy} onClick=${() => tryCall("setup.repair")}>Repair<//>
          <//>
          <${Row} icon=${{ name: "trash", color: "red" }} title="Uninstall Everything" subtitle="Remove all mods from your CS2 folder (loadout is backed up)">
            <${Button} size="sm" kind="destructive" disabled=${!state.hasGame || busy} onClick=${uninstall}>Uninstall<//>
          <//>
        <//>

        <${Section} title="Folders and Links">
          <${Row} icon=${{ name: "folder", color: "blue" }} title="CS2 Folder" onClick=${() => tryCall("app.openFolder", "game")} />
          <${Row} icon=${{ name: "folder", color: "purple" }} title="Plugin Folder" onClick=${() => tryCall("app.openFolder", "plugin")} />
          <${Row} icon=${{ name: "folder", color: "orange" }} title="Server Configs" onClick=${() => tryCall("app.openFolder", "cfg")} />
          <${Row} icon=${{ name: "folder", color: "green" }} title="Launcher Data" onClick=${() => tryCall("app.openFolder", "data")} />
          <${Row} icon=${{ name: "link", color: "teal" }} title="Metamod Downloads" subtitle="Opens in your browser" onClick=${() => tryCall("app.openUrl", state.metamodPage)} />
          <${Row} icon=${{ name: "link", color: "teal" }} title="CounterStrikeSharp Downloads" subtitle="Opens in your browser" onClick=${() => tryCall("app.openUrl", state.cssPage)} />
        <//>
      </div>

      <div class="stack">
        <${Section} title="Appearance">
          <${Row} icon=${{ name: "textsize", color: "blue" }} title="Text Size">
            <${Segmented} options=${[{ value: "normal", label: "Normal" }, { value: "large", label: "Large" }]} value=${state.textSize}
              onChange=${v => tryCall("setup.appearance", { textSize: v })} />
          <//>
          <${Row} icon=${{ name: "layers", color: "indigo" }} title="Reduce Transparency" subtitle="Solid panels instead of glass">
            <${Toggle} value=${state.reduceGlass} onChange=${v => tryCall("setup.appearance", { reduceGlass: v })} />
          <//>
          <${Row} icon=${{ name: "sparkles", color: "pink" }} title="Reduce Motion" subtitle="No animations">
            <${Toggle} value=${state.reduceMotion} onChange=${v => tryCall("setup.appearance", { reduceMotion: v })} />
          <//>
        <//>

        <${Section} title="Log">
          <${LogView} height=${520} />
        <//>
      </div>
    </div>
  <//>`;
}
