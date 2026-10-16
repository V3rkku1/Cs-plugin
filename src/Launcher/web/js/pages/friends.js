// Friends: host step by step (connection, address and invite, options, start), or join a friend.
import { html, useState, useEffect } from "../vendor/preact-htm.js";
import { tryCall, useApi } from "../api.js";
import { Page, Section, Row, Button, Segmented, Select, TextField, Icon, Badge } from "../ui.js";
import { go, useFlash } from "./play.js";

const networks = [
  { value: 0, label: "Same Wi-Fi" },
  { value: 1, label: "Radmin / Hamachi" },
  { value: 2, label: "Internet (UPnP)" },
];
const networkHelp = [
  "Your friend is at your place, on the same Wi-Fi or router.",
  "Free VPN: you both install Radmin VPN and join the same network first. Works from anywhere.",
  "The launcher opens the port on your router (UPnP). Nothing else to install - your router must allow UPnP.",
];

const kindColor = { good: "var(--green)", warning: "var(--orange)", muted: "var(--text-2)", running: "var(--blue)", busy: "var(--blue)", off: "var(--text-3)" };

function Step({ n, title, done, children, footer }) {
  return html`<${Section} footer=${footer} title=${html`<span class=${"step-num" + (done ? " done" : "")}>${done ? html`<${Icon} name="check" size=${12} stroke=${3} />` : n}</span>${title}`}>
    ${children}
  <//>`;
}

export function FriendsPage() {
  const [state] = useApi("friends.state", undefined, ["play.tick"]);
  const [pick, setPick] = useState(null);
  const [password, setPassword] = useState(null);
  const [code, setCode] = useState("");
  const [joinPassword, setJoinPassword] = useState("");
  const [copy, copied, copying] = useFlash();
  useEffect(() => setPassword(null), [state?.password]);
  if (!state) return html`<${Page} title="Friends" />`;

  const address = state.addresses.find(a => a.address === pick)?.address ?? state.addresses[0]?.address ?? null;
  const hosting = state.testMode;
  const savePassword = () => { if (password != null && password !== state.password) tryCall("friends.set", { password }); };
  const join = (args) => tryCall("friends.join", args);
  const copyButton = (what, label, kind = "default") => html`<${Button} kind=${kind} icon=${copied === what ? "check" : "copy"} disabled=${!address}
      busy=${copying === what} onClick=${() => copy(what, "friends.copy", { what, address })}>${copied === what ? "Copied" : label}<//>`;

  return html`<${Page} title="Friends" subtitle="Play together on the same Wi-Fi, over Radmin VPN or over the internet.">
    <div class="cols wrap friends-cols">
      <div class="stack">
        <div class="col-head"><${Icon} name="server" size=${18} /> Host a Game</div>

        <${Step} n="1" title="How are you connected?" done=${!!address && state.addressKind !== "warning"}>
          <div class="pad-block">
            <${Segmented} class="full" options=${networks} value=${state.network} onChange=${v => { setPick(null); tryCall("friends.set", { network: v }); }} />
            <p class="small muted help">${networkHelp[state.network]}</p>
          </div>
        <//>

        <${Step} n="2" title="Send this to your friend" footer="The invite code has your address, port and password in one line. Your friend pastes it in Join a Friend.">
          <div class="pad-block address-block">
            <div class="address-line">
              <div class=${"address" + (address ? " mono" : " none")}>${address ? `${address}:${state.port}` : state.searching ? "Looking for your address…" : "No address yet"}</div>
              ${state.addresses.length > 1 && html`<${Select} value=${address} onChange=${setPick}
                options=${state.addresses.map(a => ({ value: a.address, label: a.address, note: a.kind }))} />`}
            </div>
            <div class="small address-note" style=${`color:${kindColor[state.addressKind]}`}>
              ${state.searching && html`<span class="state-icon busy mini"></span>`}${state.addressNote}
            </div>
            <div class="hstack copy-row">
              ${copyButton("code", "Copy Invite Code", "prominent")}
              ${copyButton("address", "Copy IP")}
              ${copyButton("message", "Copy Invite Message")}
              ${state.canRetry && html`<${Button} icon="refresh" onClick=${() => tryCall("friends.findRouter")}>Find Router Again<//>`}
            </div>
          </div>
        <//>

        <${Step} n="3" title="Options">
          <${Row} icon=${{ name: "skins", color: "pink" }} title="Skins" subtitle=${state.sharedSkins ? "Everyone plays with your loadout" : "Friends pick their own with !skin in chat"}>
            <${Segmented} options=${[{ value: true, label: "Everyone uses mine" }, { value: false, label: "Each picks own" }]}
              value=${state.sharedSkins} onChange=${v => tryCall("friends.set", { sharedSkins: v })} />
          <//>
          <${Row} icon=${{ name: "key", color: "yellow" }} title="Password" subtitle=${state.network === 2 ? "Internet mode always uses one (made for you on Start)" : "Optional"}>
            <div onFocusOut=${savePassword}>
              <${TextField} width=${170} value=${password ?? state.password} placeholder="None" onInput=${setPassword} onEnter=${savePassword} />
            </div>
          <//>
          <${Row} icon=${{ name: "shield", color: "green" }} title="Windows Firewall"
              subtitle=${state.firewallOk ? "CS2 is allowed - friends can reach your server" : "Lets friends reach your server (asks for admin once)"}>
            ${state.firewallOk
              ? html`<${Badge} color="green">Allowed<//>`
              : html`<${Button} size="sm" disabled=${!state.hasGame} onClick=${() => tryCall("friends.firewall")}>Allow<//>`}
          <//>
        <//>

        <${Step} n="4" title="Play" done=${hosting && state.players.length > 1}>
          <div class="row host-row">
            <span class=${"live-dot" + (state.hostKind === "off" ? "" : " on")} style=${`--c:${kindColor[state.hostKind]}`}></span>
            <div class="text">
              <div class="title">${state.hostTitle}</div>
              <div class="subtitle">${state.hostDetail}</div>
            </div>
            <div class="accessory">
              ${hosting
                ? html`<${Button} kind="destructive" icon="stop" onClick=${() => tryCall("play.stop")}>Stop<//>`
                : html`<${Button} kind="prominent" icon="play" disabled=${!state.ready} busy=${state.busy} onClick=${() => tryCall("friends.host")}>Start Hosting<//>`}
            </div>
          </div>
          <${Row} icon=${{ name: "sliders", color: "blue" }} title="Mode, Map and Bots" subtitle="Set them on Live Server" onClick=${() => go("live")} />
        <//>
      </div>

      <div class="stack">
        <div class="col-head"><${Icon} name="friends" size=${18} /> Join a Friend</div>
        <${Section}>
          <div class="pad-block join-block">
            <label class="field-label">Invite code or address</label>
            <${TextField} icon="link" value=${code} onInput=${setCode} onEnter=${() => join({ text: code, password: joinPassword })} placeholder="LST-… or 192.168.1.20" />
            <label class="field-label">Password</label>
            <${TextField} icon="key" value=${joinPassword} onInput=${setJoinPassword} placeholder="If they set one" />
            <${Button} kind="prominent" size="lg" block icon="play" disabled=${!state.hasGame || !code.trim()}
              onClick=${() => join({ text: code, password: joinPassword })}>Join Game<//>
            <p class="small muted help">Nothing to install. CS2 starts with VAC off for this one game (normal again next time). If CS2 is already open, it just connects.</p>
          </div>
        <//>

        ${state.recent.length > 0 && html`<${Section} title="Recent">
          ${state.recent.map((r, i) => html`<${Row} key=${r.address} icon=${{ name: "clock", color: "indigo" }} title=${r.address}
              subtitle=${r.password ? "With password" : "No password"} class="clickable" onClick=${() => join({ recent: i })}>
            <${Button} size="sm" kind="prominent" disabled=${!state.hasGame} onClick=${e => { e.stopPropagation(); join({ recent: i }); }}>Join<//>
            <${Button} size="sm" kind="plain" icon="x" title="Forget" onClick=${e => { e.stopPropagation(); tryCall("friends.forget", i); }} />
          <//>`)}
        <//>`}

        <${Section} title="Staying Safe">
          ${[
            ["Your inventory never changes", "Skins only exist on this private server"],
            ["VAC is off, not tricked", "-insecure is Valve's own switch for offline/LAN play"],
            ["Your friend's game isn't modified", "Joining only starts CS2 with launch options"],
            ["Not public", "No server token, not in the server browser"],
            ["Stop puts everything back", "Then play matchmaking as normal"],
          ].map(([t, sub]) => html`<${Row} icon=${{ name: "check", color: "green" }} title=${t} subtitle=${sub} />`)}
        <//>

        <${Section} title="Friends Pick Skins in Chat">
          <div class="chat-help mono">
            <div><b>!skin</b> <i>name [wear]</i><span>skin the gun in your hands</span></div>
            <div><b>!knife</b> <i>name</i> · <b>!gloves</b> <i>type name</i></div>
            <div><b>!gen</b> <i>code</i><span>copy a real item (float, pattern, stickers)</span></div>
            <div><b>!skinsearch</b> <i>text</i> · <b>!skinhelp</b></div>
          </div>
        <//>
      </div>
    </div>
  <//>`;
}
