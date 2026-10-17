// Live Server: the server settings (most used first) and the controls for the running match.
import { html, useState, useEffect } from "../vendor/preact-htm.js";
import { tryCall, navigate } from "../api.js";
import { Page, Section, Row, Button, Toggle, Segmented, Slider, Stepper, TextField, Select, Icon, Badge, confirm, prompt, LogView } from "../ui.js";
import { usePlay } from "./play.js";

const set = (key, value) => tryCall("live.set", { [key]: value });
const send = command => tryCall("live.command", command);

// Bot Improver's own console commands (its plugins must be loaded for them to exist). Its weapon and
// Flying Scoutsman "commands" are cfg aliases, so the launcher sends what they do instead (live.botweapon, live.scouts).
const BOT_AIM = [{ value: 0, label: "Mixed" }, { value: 1, label: "Head" }, { value: 2, label: "Body" }];
const BOT_AIM_CMD = ["mixed", "head", "body"];
const BOT_NADES = ["Off", "Less", "Normal", "More", "Max"];
const BOT_GUNS = [
  ["", "Normal buying"], ["ak47", "AK-47"], ["m4a1", "M4A4"], ["m4a1s", "M4A1-S"], ["awp", "AWP"], ["ssg08", "SSG 08"],
  ["famas", "FAMAS"], ["galilar", "Galil AR"], ["aug", "AUG"], ["sg556", "SG 553"], ["scar20", "SCAR-20"], ["g3sg1", "G3SG1"],
  ["mp9", "MP9"], ["mac10", "MAC-10"], ["mp7", "MP7"], ["mp5sd", "MP5-SD"], ["ump45", "UMP-45"], ["p90", "P90"], ["bizon", "PP-Bizon"],
  ["nova", "Nova"], ["xm1014", "XM1014"], ["mag7", "MAG-7"], ["sawedoff", "Sawed-Off"], ["negev", "Negev"], ["m249", "M249"],
  ["deagle", "Desert Eagle"], ["r8", "R8 Revolver"], ["p250", "P250"], ["fn57", "Five-SeveN"], ["cz75a", "CZ75-Auto"], ["elite", "Dual Berettas"],
].map(([value, label]) => ({ value, label }));

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
    if (await confirm({ title: "Reset settings?", message: "Back to a plain CS2 server: Casual on Dust II, no bots, normal money and rules, no cheats or extra plugins.\nFriends, password and launch options are kept.", ok: "Reset", danger: true }))
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
          ${!plugin && html`<${Row} title="Valve Retakes" subtitle="3 terrorists vs 4 counter-terrorists. Loadout cards each round, the buy menu is open at the start." />`}
          ${plugin && html`
            <${Row} title="Max Players" subtitle="Seats in the match, including you (2-10)">
              <${Stepper} value=${s.retakeMaxPlayers} min=${2} max=${10} onChange=${v => set("retakeMaxPlayers", v)} /><//>
            <${Row} title="Terrorists" subtitle="Percent of the players on T">
              <${Stepper} value=${s.retakeTerroristPercent} min=${10} max=${80} step=${5} format=${v => v + "%"} parse=${t => parseInt(t)} onChange=${v => set("retakeTerroristPercent", v)} /><//>
            <${Row} title="Guns" subtitle="Buy your own, or pistol / half-buy / rifle rounds">
              <${Segmented} options=${["Buy", "Allocator"]} value=${s.retakeWeapons === 1 ? 1 : 0} onChange=${v => set("retakeWeapons", v)} /><//>
            ${!buy && html`<${Row} title="Allocator" subtitle="Type !guns to choose. One AWP per team. The buy menu is open too." />`}
            <${Row} title="Buy Time" subtitle="Seconds of buy menu at the start of the round">
              <${Stepper} value=${s.retakeBuySeconds} min=${4} max=${20} onChange=${v => set("retakeBuySeconds", v)} /><//>
            <${Row} title="Full Buys" subtitle="Chance weight of a round with all the money ($16000)">
              <${Stepper} value=${s.retakeFullPercent} min=${0} max=${100} step=${5} onChange=${v => set("retakeFullPercent", v)} /><//>
            <${Row} title="Force Buys" subtitle="Weight of a round with $2900: SMGs, cheap rifles, armor">
              <${Stepper} value=${s.retakeForcePercent} min=${0} max=${100} step=${5} onChange=${v => set("retakeForcePercent", v)} /><//>
            <${Row} title="Eco Rounds" subtitle="Weight of a round with $800: pistols and armor only. Only full buys: 100 / 0 / 0">
              <${Stepper} value=${s.retakeEcoPercent} min=${0} max=${100} step=${5} onChange=${v => set("retakeEcoPercent", v)} /><//>
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
          <${Row} title="Team Size" subtitle="Most players per side, people and bots (classic modes)">
            <${Stepper} value=${s.teamSizeT} min=${1} max=${16} format=${v => v + " T"} parse=${t => parseInt(t)} onChange=${v => set("teamSizeT", v)} />
            <${Stepper} value=${s.teamSizeCT} min=${1} max=${16} format=${v => v + " CT"} parse=${t => parseInt(t)} onChange=${v => set("teamSizeCT", v)} />
            <${Button} size="sm" onClick=${() => tryCall("live.saveTeamSize")} title="Remember these sizes whenever this map is played">Save for this map<//><//>
          <${Row} icon=${{ name: "run", color: "teal" }} title="Calm Bots" subtitle="Bots don't keep jumping around: one jump every few seconds">
            <${Toggle} value=${s.calmBots} onChange=${v => set("calmBots", v)} />
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
          <${Row} icon=${{ name: "crosshair", color: "red" }} title="Headshot Only" subtitle="Only headshots (and knives) hurt players">
            <${Toggle} value=${s.headshotOnly} onChange=${v => set("headshotOnly", v)} /><//>
          <${Row} icon=${{ name: "target", color: "orange" }} title="Pistols Only" subtitle="Deagle, Tec-9, Five-SeveN, USP-S, Glock… other guns can't be bought or picked up">
            <${Toggle} value=${s.pistolsOnly} onChange=${v => set("pistolsOnly", v)} /><//>
          <${Row} icon=${{ name: "crosshair", color: "purple" }} title="One Weapon" subtitle="Everyone, bots too, gets only this gun and a knife">
            <${Select} width=${170} value=${s.onlyWeapon} options=${[["", "Any guns"], ["weapon_awp", "AWP only"], ["weapon_ssg08", "Scout only"], ["weapon_deagle", "Deagle only"], ["knife", "Knives only"]].map(([value, label]) => ({ value, label }))}
              onChange=${v => set("onlyWeapon", v)} /><//>
          <${Row} icon=${{ name: "sparkles", color: "teal" }} title="Low Gravity" subtitle="Floaty jumps and fast air strafing">
            <${Toggle} value=${s.lowGravity} onChange=${v => set("lowGravity", v)} /><//>
          <${Row} icon=${{ name: "refresh", color: "green" }} title="Reload on Kill" subtitle=${s.infiniteAmmo ? "Infinite Ammo already never empties the magazine" : "A kill refills your magazine at once · spare ammo never runs out"}>
            <${Toggle} value=${s.reloadOnKill} onChange=${v => set("reloadOnKill", v)} /><//>
          <${Row} icon=${{ name: "target", color: "pink" }} title="Damage Report" subtitle="Chat shows the damage you deal">
            <${Toggle} value=${s.showDamage} onChange=${v => set("showDamage", v)} /><//>
        <//>

        <${Section} title="Server Plugins" note="built in · players use them in chat" footer="Applied on the next Start or Apply Changes. The downloaded plugins (bots, Deathmatch) restart the server when they load or unload.">
          <${Row} icon=${{ name: "shield", color: "blue" }} title="Spawn Protection" subtitle="2.5 s of safety after spawning · shooting ends it">
            <${Toggle} value=${s.spawnProtection} onChange=${v => set("spawnProtection", v)} /><//>
          <${Row} icon=${{ name: "crosshair", color: "orange" }} title="!guns" subtitle="Players pick their Deathmatch weapons: !guns ak47 deagle">
            <${Toggle} value=${s.gunMenu} onChange=${v => set("gunMenu", v)} /><//>
          <${Row} icon=${{ name: "check", color: "green" }} title="!ready" subtitle="Warmup waits until every player typed !ready">
            <${Toggle} value=${s.readyUp} onChange=${v => set("readyUp", v)} /><//>
          <${Row} icon=${{ name: "map", color: "teal" }} title="!rtv Map Vote" subtitle="Half the players type !rtv, then everyone votes the next map">
            <${Toggle} value=${s.mapVote} onChange=${v => set("mapVote", v)} /><//>
          <${Row} icon=${{ name: "star", color: "yellow" }} title="Round MVP and !stats" subtitle="MVP line after each round · !stats and !top for the map">
            <${Toggle} value=${s.roundSummary} onChange=${v => set("roundSummary", v)} /><//>
          <${Row} icon=${{ name: "trash", color: "orange" }} title="Clean Up Dropped Guns" subtitle="Guns on the ground disappear after 15 seconds: less clutter and lag">
            <${Toggle} value=${s.cleanupGuns} onChange=${v => set("cleanupGuns", v)} /><//>
          <${Row} icon=${{ name: "shield", color: "teal" }} title="No Fall Damage">
            <${Toggle} value=${s.noFallDamage} onChange=${v => set("noFallDamage", v)} /><//>
          <${Row} icon=${{ name: "run", color: "green" }} title="Auto Bunnyhop" subtitle="Everyone bunnyhops by holding jump">
            <${Toggle} value=${s.autoBhop} onChange=${v => set("autoBhop", v)} /><//>
          <${Row} icon=${{ name: "person", color: "purple" }} title="Welcome Message" subtitle="Tells new players which commands they can use">
            <${Toggle} value=${s.welcomeMessage} onChange=${v => set("welcomeMessage", v)} /><//>
          <${Row} icon=${{ name: "bot", color: "green" }} title="Bot Improver" subtitle="Human-like bots: better aim, grenades, movement, strategy and buying, pro names · includes BotAI, Bot Controller and Bot Hider">
            <${Toggle} value=${s.botImprover} onChange=${v => set("botImprover", v)} /><//>
          <${Row} icon=${{ name: "bot", color: "orange" }} title="Bot Controller" subtitle=${s.botImprover ? "Included in Bot Improver" : "!record your movement, then !replay <bot slot> so a bot copies it"}>
            <${Toggle} value=${s.botController || s.botImprover} disabled=${s.botImprover} onChange=${v => set("botController", v)} /><//>
          <${Row} icon=${{ name: "bot", color: "indigo" }} title="Bot Hider" subtitle=${s.botImprover ? "Included in Bot Improver" : "Bots look like real players: no BOT tag, names, avatars, ping · downloads from GitHub on first Start"}>
            <${Toggle} value=${s.botHider || s.botImprover} disabled=${s.botImprover} onChange=${v => set("botHider", v)} /><//>
          <${Row} icon=${{ name: "crosshair", color: "red" }} title="Deathmatch Plugin"
              subtitle=${s.mode === "Deathmatch" ? "NockyCZ's Deathmatch: !gun menu, custom modes (only AWP, pistols…), kill sounds · replaces spawn protection and !guns" : "Only in Deathmatch mode"}>
            <${Toggle} value=${s.deathmatchPlugin} onChange=${v => set("deathmatchPlugin", v)} /><//>
        <//>

        ${s.botImprover && html`<${Section} title="Bot Improver" note=${up ? "changes reach the running server right away" : ""}
            footer="Bot Improver's settings. Forced weapons start the next round.">
          <${Row} icon=${{ name: "crosshair", color: "red" }} title="Bot Aim" subtitle="Where bots aim: mixed picks the spot for the situation">
            <${Segmented} options=${BOT_AIM} value=${s.botAim}
              onChange=${v => { set("botAim", v); if (up) send("bot_aim " + BOT_AIM_CMD[v]); }} /><//>
          <${Row} icon=${{ name: "target", color: "orange" }} title="Bot Grenades" subtitle="How many smokes, flashes, HEs and molotovs bots throw">
            <${Select} width=${150} value=${s.botNades} options=${BOT_NADES.map((label, value) => ({ value, label }))}
              onChange=${v => { set("botNades", v); if (up) send("bot_nades " + BOT_NADES[v].toLowerCase()); }} /><//>
          <${Row} icon=${{ name: "bot", color: "green" }} title="Bots Buy" subtitle="Every bot gets this weapon, or buys like normal">
            <${Select} width=${190} value=${s.botForceWeapon} options=${BOT_GUNS}
              onChange=${v => tryCall("live.botweapon", v)} /><//>
          <${Row} icon=${{ name: "skins", color: "purple" }} title="Bot Skins" subtitle=${s.botSkins ? "Random skins, knives, gloves and agents on every bot" : "Off: bots use default skins and agents"}>
            <${Toggle} value=${s.botSkins} onChange=${v => set("botSkins", v)} /><//>
          <${ActionGrid} disabled=${!up} actions=${[
            ...(s.botSkins ? [["Reroll Bot Skins", "br_reroll", "shuffle"]] : []),
            ["Flying Scoutsman On", () => tryCall("live.scouts", true), "sparkles"],
            ["Flying Scoutsman Off", () => tryCall("live.scouts", false), "stop"]]} />
        <//>`}

        <${Section} title="Hacker Mode">
          <${Row} icon=${{ name: "hacker", color: "green" }} title="Hacker Mode" subtitle=${state.hackerDetail}
              subtitleClass=${s.hackerMode !== 0 ? "note-good" : ""} onClick=${() => navigate("hacker")}>
            <${Button} size="sm" onClick=${e => { e.stopPropagation(); navigate("hacker"); }}>Open<//>
          <//>
        <//>

        <${Section} title="Launch">
          <${Row} icon=${{ name: "play", color: "blue" }} title="Open CS2 Automatically" subtitle="Off = start only the server">
            <${Toggle} value=${s.autoStartGame} onChange=${v => set("autoStartGame", v)} />
          <//>
          <${Row} icon=${{ name: "terminal", color: "indigo" }} title="Launch Options" subtitle="Extra options for the game window">
            <${CommitField} width=${230} value=${s.gameArgs} placeholder="-windowed -w 1920 -h 1080" onCommit=${v => set("gameArgs", v)} />
          <//>
          <${Row} icon=${{ name: "refresh", color: "red" }} title="Reset to Default" subtitle="A plain CS2 server (keeps friends and launch options)">
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
