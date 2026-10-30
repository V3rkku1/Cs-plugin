# LAN Skin Tester (CS2)

Preview CS2 skins, knives, gloves, stickers and charms on **your own local server** before you buy them,
and play with bots and friends. Two parts:

- **The plugin** ([CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)): `!skin`, `!knife`,
  `!gloves`, wear, pattern seed, StatTrak, name tags, 5 stickers and a charm, real items from a `!gen` code or
  inspect link. Your loadout survives map changes and restarts.
- **The launcher** (Windows): installs everything in one click, starts the server and CS2, and has an
  inventory, case opening, trade ups, game modes, bots, Workshop maps and friends.

> **Only for your own local server.** Skins exist only on this server and nobody else sees them. The plugin
> is off unless the server runs with `sv_lan 1` (or as the launcher's private friends server). Don't install
> it on a public community server: Valve's rules don't allow fake inventory items there, and the server's
> login token (GSLT) can be banned.

---

## The launcher (Windows)

**Download:** GitHub → **Releases → Latest build** (built from every push to `main`).

- **`LanSkinTesterSetup.exe` (recommended):** installs for your Windows user only (no admin prompt) into
  `%LocalAppData%\Programs\LAN Skin Tester`, with a Start menu shortcut, an optional desktop shortcut and an
  entry in *Installed apps*. Uninstalling keeps your loadouts and settings.
- **`LanSkinTesterLauncher.exe`:** the same launcher as one portable file (about 8 MB).

It needs Microsoft's WebView2, which Windows 10 and 11 already have. If it's missing, the launcher says so;
install the *Evergreen Runtime* from microsoft.com/edge/webview2.

On first start it opens on **Setup**. Press **Install Everything**, then go to **Play** and pick a quick start.

### Pages

The sidebar has ten pages (**Ctrl+1** to **Ctrl+9** for the first nine). **Ctrl+K** finds any page, quick start or action
(type "dm", Enter starts Deathmatch). **F5** starts or applies changes, **Shift+F5** stops, **F11** is full
screen, **Ctrl+F** searches the inventory. On a big screen the page zooms to fill the window.

**Play.** *Before You Start* lists what would stop the server (missing mods, a CS2 update the add-ons
aren't updated for, the server port in use, a nearly full disk), each with its fix. Quick starts: *Competitive 5v5*, *Free-for-all Deathmatch*, *Valve Retakes*, *Plugin Retakes*, plus
your own (*Save Current Setup* stores the mode, map, bots and rules under a name; **×** deletes it). **Start** opens the server window and CS2 with VAC off, and CS2 joins when the server is up.
**Open CS2** starts the game again if you closed it. **Stop** closes both and puts CS2 back to normal.

**Live Server.** Everything about the match, applied with **Apply Changes** (a mode or map change reloads
the map; a few settings restart the server and ask first):

- **Game:** mode (Deathmatch, Competitive, Casual, Wingman, Arms Race, Demolition, Weapons Expert, Custom,
  Retakes, and Rush when your CS2 has it), map, a **Workshop map** (ID or link), team sizes
  (*Save for this map*).
- **Bots:** 0–20, skill, team, weapons (**Rifles** by default: no AWPs, shotguns, SMGs or machine guns, and
  *Rifles* picks what bots keep: AK-47 and M4s, AK only, M4A4 only, M4A1-S only or all rifles; anything else
  is swapped), *Calm Bots* (no bunny-hopping), *Bot Skins* (random skins, knife and gloves on every bot,
  with *Reroll*; Bot Improver adds agents).
- **Rules:** free-for-all, max money, infinite ammo, endless rounds, bots don't shoot, cheats, round time,
  friendly fire, headshot only, pistols only, reload on kill, one weapon (AWP, Scout, Deagle or knives),
  low gravity, and a **Fun Mode** for Casual / Competitive rounds: *Zombie* (a few players start as zombies with a
  knife, extra speed and more health the more humans they face; whoever they kill joins them; humans win by
  surviving the round) or *Hide & Seek* (T hide
  with knives while CT wait 30 s, then seek; T win if one is alive when time runs out). Both are quick starts on
  the Play page too.
- **Performance:** CPU and memory of the server and CS2 and the PC's free memory, with a tip when it struggles.
- **Server Plugins:** clean up dropped guns, no fall damage, auto bunnyhop, spawn protection, `!guns`,
  `!ready`, `!rtv` map vote, round MVP and `!stats`, welcome message, and downloaded ones: Bot Improver,
  Bot Hider, Bot Controller and the Deathmatch plugin (see [Server plugins](#server-plugins)).
- **Experimental › Custom Models:** models from Steam Workshop addons that replace the knife, a gun or the T / CT
  agent for every player. Paste the addon's ID or link and subscribe to it in Steam: the launcher then lists the
  models inside it to pick from, and what each replaces. The launcher installs
  [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager), which mounts the addons and has everyone
  who joins download them; the plugin puts the models on, in first person too. The server goes online (unlisted)
  to download the addons, so it needs internet. Models must be made for CS2 (Workshop Tools) with the stock
  animations and skeleton; skins don't show on them. May break after a CS2 update.
- **Launch:** your own CS2 launch options, and whether CS2 starts automatically.
- **Live controls** (when the server is up): restart, end warmup, pause, resume, swap, CT/T win; add CT,
  add T, kick bots, respawn, god; third person, noclip, armor, nades, clear, save/load spot, rethrow,
  damage report, refresh skins, give weapon; **Session Stats** (K, D, K/D, HS%, ADR for everyone, bots too,
  with *Reset Stats*); **Command** runs any server console command.
- **Logs:** the launcher's activity, the server console and the CounterStrikeSharp log, with *Errors only*.
  **Copy Logs** copies all of it as one report, errors first, to paste when asking for help.
- **Reset to Default** makes it a plain CS2 server (Casual, no bots, normal rules, no extra plugins); friends,
  password and launch options stay.

**Inventory.** What you unboxed, like CS2's inventory: All Items, Loadout, Weapons, Knives & Gloves,
Stickers & Graffiti, Collectibles; search, filter and sort by newest, rarity, price or name. Click an item
to inspect it (exterior, exact float, pattern, price, case) and **Equip** it, for both sides or **EQUIP T** /
**EQUIP CT**. **Try in Game** equips it, kicks the bots and puts it in your hands on the running server. The **Loadout** tab shows what each side wears; *Use Default* takes it off. *Presets* save the whole
loadout under a name ("Blue set") and equip it again with one click. An equipped item
changes in game within a second.
- **Stickers…** on a gun: apply up to 5 stickers from your inventory (applying uses the sticker up), place,
  turn, resize and scrape them; scraped all the way, a sticker comes off.
- **Trade Up:** 10 guns of one grade (all StatTrak™ or none, no souvenirs) for one of the next grade, or 5
  Covert guns for a knife or gloves, with CS2's ticket odds and float formula. You see every possible result
  with its chance, float and price before you sign.
- **Export** / **Import** save the inventory to a file and add one back (import needs the admin code).
- **Preview wear:** drag a slider in the inspect panel to see roughly how the skin looks from Factory New to
  Battle-Scarred. **StatTrak™ kills** really count: kills on the server add to the counter on the gun.
- **Storage Units:** named boxes of up to 1000 items each. Stored items are hidden from the inventory and
  can't be equipped, traded up or gifted until you take them out; an equipped item comes off when stored.
  Deleting a unit puts its items back. *Store in a unit…* is in the inspect panel.
- **Fade %:** Fade, Amber Fade and Acid Fade skins show their fade percentage (80–100%, from the pattern) in
  the inspect panel.
- **Random Loadout** (Loadout tab): a random skin you own on every gun, plus a random knife and gloves.
- **Gift to a Friend:** the item leaves your inventory and you get a code (copied); your friend pastes it at
  **Redeem Gift**. One code, one item. (Codes are signed, but the key ships in every launcher: fine between
  friends, not a real economy.)

**Cases.** Open any CS2 container for free: weapon cases, capsules, souvenir packages, pins, patches,
music kits, graffiti. Valve's odds (Mil-Spec 79.92%, Restricted 15.98%, Classified 3.20%, Covert 0.64%, knife
or gloves 0.26%), real float ranges, pattern 0–1000, StatTrak 10% in weapon cases. *Open ×5* / *×10*, Space
opens, a click skips the reel. Prices are real market prices for information (Skinport, then the Steam
Market; USD, EUR or GBP), and the page shows what your openings would have cost (keys at Steam's price plus the
containers) and your profit or loss against what you unboxed.
- **Wishlist:** ★ a skin in a case's contents; the case list then shows which cases have your wished skins and
  the chance per opening.
- **Collection Book:** every weapon case and souvenir package with how many of its skins you own.
- **Case Battle:** you and 1–3 bots open the same case 1–5 times; the highest total value takes every item, a
  loss leaves you nothing.
- **Upgrade:** risk an unequipped skin for a better one; the chance is its value against the target's, minus 10%
  (at most 80%). Win: the new skin. Lose: yours is gone.

**Unbox only and the admin code:** by default you can equip only what you unboxed (plus vanilla guns), and
`!skin`, `!gen`, `!knife` and `!gloves` are off on your server. The owner's admin code (Cases › *Admin code*)
unlocks every item on that PC and Inventory › **Add Item** (any skin, knife, glove, sticker, agent, pin or
music kit with the float and pattern you pick).

**Hacker Mode** (fun, on your own server against bots or friends who agreed). Always off when the launcher
starts; changes reach the running server at once. Profiles (Legit, Closet, Rage, Movement, All Off), who hacks
(*Everyone*, or *One random* per round who glows for the others), and each power: chams, box and skeleton
ESP, name tags, aimbot (legit / silent / rage, body part, target, key, aggression 1–10, through walls),
triggerbot, recoil control, zero spread, radar, bunnyhop, no flash, enemy info, infinite ammo, speed, damage
boost. In game, `!cheats` opens a menu where each player sets their own. The triggerbot clicks your mouse
through the launcher (localhost port 27099), only while the test session runs and CS2 is in front.

**Trainer.** *Launch:* pick a map and a practice setup (Lineups & Utility, Site Drills, Aim Warm-up
with bot count, headshots only and passive bots, Movement) and the server starts, or switches if it runs.
*Lineups:* 278 built-in smokes, flashes and molotovs for Mirage, Inferno, Dust II, Ancient,
Anubis, Nuke, Overpass and Train (positions and angles from cs2util.com), filtered by grenade, side and
name; **Go** puts you on the spot, aimed, with the grenade, and chat says how to throw it (**?** opens the
lineup's pictures). Your own lineups from `!savenade` are listed too. *Grenade Practice:* one-click
Practice Mode (endless round, free buys, infinite grenades), trajectory preview, the air time in chat when
a grenade pops, rethrow and clear. *Site drills* against bots on a bomb map (Competitive or Casual), site A or
B, 1–5 bots: *Clear Site* (bots stand on random spots inside the bomb site and hold angles), *Plant & Hold*
(bots wait at CT spawn and retake once you plant), *Hold Site* (you start on the site as CT, bots push),
*Prefire* (bots on site angles don't shoot; chat times how fast you clear them), *Clutch Retake* (the bomb is
already planted and bots guard it; retake alone and defuse); chat keeps your round score. *Reaction Drill:* one bot pops up in front of you and chat shows how many ms you
took to hit it (3 s and it moves on). *Flick Drill:* the same, but the bot can appear anywhere around you,
behind you too. Both can hand you a gun first (AK-47, M4A1-S, AWP, Deagle, USP-S). *Movement:* jump stats on screen (speed, distance, pre-speed, gain, strafe sync, strafes,
bhop chain; `!speed`), easy bhop, checkpoint and teleport, timed *courses* (set a start and a finish on any
map, `!course start|finish|clear`, best time kept), noclip. *Aim:* counter-strafe check on every
shot (`!cs`), spray score (how far a spray lands from its first bullet, `!spray`), bullet impacts, frozen bots, bots that copy your movement. Plus a reaction test in the
launcher itself. A drill or Practice Mode changes the server until you turn it off; **Apply Changes** on
Live Server puts your setup back.

**Stats.** Are you improving? Each skill this week against last week (better / worse), a chart per day and
a table (2 weeks to all time): Reaction Drill, Flick Drill and Reaction Test times, headshot %, K/D, accuracy, damage
per round, stopped shots (counter-strafe check), strafe sync, longest jump, prefire time, spray spread, drill headshots (kills on bots that
don't shoot back, kept apart from your real headshot % and K/D) and drill rounds won, plus your
personal bests, achievements, your **last spray** drawn bullet by bullet (with the 1.5° and 3° rings), and all-time tables per gun (kills, headshots, accuracy) and per map (rounds
won, K/D, damage per round, drill wins, best course time). Kills, shots and damage count in every mode, except with an aimbot and in the Reaction
Drill; only flat jumps count. With friends on the server, pick whose stats to see. **Reset** starts over
(the old file is kept as `training.jsonl.bak`). Kept in
`plugins/LanSkinTester/training.jsonl`.

**Workshop.** The maps Steam has downloaded for CS2, a search of the Workshop, **Host** (starts or switches
the server on that map) and *Subscribe* (opens it in Steam). A Workshop map loads the official way: the
server goes online (unlisted, no token), boots on a normal map, then switches with `host_workshop_map`; CS2
joins once the map is loaded. A new map starts in Custom, or a fitting mode from its name (`aim_`, `ar_`,
`de_`…); the mode you pick is remembered per map. Maps that change maps with their own script (Movement
Hub) start as an offline game instead. Custom weapon and player models aren't supported (Workshop addon models are, as an experiment: Live Server › Experimental).

**Friends.**
- *Friends List:* friends hosting on your Wi-Fi or Radmin network show up by themselves (UDP port 27098),
  saved friends show whether they're hosting, and **Join** joins them.
- *Host:* pick how they reach you: **Same Wi-Fi**, **Radmin / Hamachi** (free
  [Radmin VPN](https://www.radmin-vpn.com/), same network), or **Internet (UPnP)** (the launcher opens UDP
  port 27015 on your router while you host, and always uses a password). **Copy Code** copies an invite code
  (`LST-…`: address, port and password in one line). Choose whose skins are used (*everyone uses mine* or
  *each picks own* with `!skin` in chat), an optional password, and press **Allow** for Windows Firewall once.
  **Start Hosting** uses the Live Server settings.
- *Join a Friend:* paste their code (or an address, `connect …` or `steam://connect/…`). CS2 starts with VAC
  off for that game, or an open CS2 connects through Steam. Your own skins go along to their server.

*Why this won't get you banned:* `-insecure` is Valve's own switch (VAC doesn't run for that game), your
Steam inventory is never touched, a friend's game files never change, yours only while test mode is on (Stop
puts them back), and the server has no token and isn't listed. If UPnP doesn't work: turn UPnP on in the
router; on CGNAT (common on mobile internet) use Radmin; or forward UDP 27015 by hand and press *Start anyway*.

**Setup.** The CS2 folder (found automatically, or *Browse*), and a checklist of Metamod, CounterStrikeSharp,
the plugin, the CounterStrikeSharp skin setting and the item lists, each with its own Install/Update button.
*Update Everything* (newest Metamod, CounterStrikeSharp, server plugins, skin data and launcher; this also
runs by itself after a CS2 update, and Start waits for it), *Check for updates*, *Install from zip* (when a
download fails), *Repair*, *Windows notifications* (server up, a friend joined, an update; only while the
launcher is in the background), *Show the Tour* (the first-start guide), *Uninstall everything* (restores
`gameinfo.gi`; the loadout is backed up), *Open folder* buttons, and *Reset Launcher*. **Appearance:** themes,
accent / background / panel colours, opacity, title bar colour, corners, font, text size, a background picture,
*Reduce Transparency* and *Reduce Motion*; *Theme Code* shares your look as `LST1-…`; *Open custom.css* edits
`%LocalAppData%\LanSkinTester\custom.css`, loaded after the built-in styles.

### Good to know

- **Restores CS2 by itself:** when CS2 and the server are closed (X, crash, or with the launcher closed in
  between), test mode switches off a few seconds later. Closing the launcher during a test asks first.
- **While test mode is on, start CS2 only from the launcher.** Started from Steam without `-insecure` it
  could get you VAC-kicked. The sidebar shows *Test mode on* in orange.
- **Updates itself:** a newer build shows an *Update available* card (checked at start and every 15 minutes);
  at start-up it installs by itself when *Update Automatically* is on. Your inventory and loadout are backed
  up first (`%LocalAppData%\LanSkinTester\backups`, the last 5).
- **The plugin updates itself:** each launcher carries its plugin and puts it in when you press Start.
- **Your data:** settings in `%AppData%\LanSkinTester\launcher.json`; inventory, item data, pictures and
  theme in `%LocalAppData%\LanSkinTester`; the loadout in the plugin folder (`loadouts.json`).

### Retakes

- **Valve** (*Valve Retakes*): Valve's built-in mode (`game_type 0`, `game_mode 5`, `sv_skirmish_id 12`):
  bomb planted, 3 T vs 4 CT, loadout cards, first to 8. Bots fill empty seats.
- **Plugin** (*Plugin Retakes*): [B3none's cs2-retakes](https://github.com/B3none/cs2-retakes) (GPL-3.0),
  downloaded on first use, run as competitive. Settings: max players (2–10), terrorist percent, scramble,
  balance, auto plant, site announcement, join straight in, bomb timer. **Guns:** *Buy* (freeze-time buy with
  $16000, remembered per side; bots keep rifles) or *Allocator*
  ([cs2-retakes-allocator](https://github.com/Micka2302/cs2-retakes-allocator-2.0): pistol, half-buy and
  rifle rounds, `!guns`). **Instadefuse** ([B3none's cs2-instadefuse](https://github.com/B3none/cs2-instadefuse))
  finishes a defuse that nothing can stop.
- **Seats for everyone:** bots fill the places nobody uses and give one up for every person who joins
  (`lst_retake_seats`), so every friend gets a team. The team menu never greys out a side.

Retakes plugins are parked outside the plugins folder whenever they shouldn't load. Switching a plugin on or
off, or a setting it only reads at load, restarts the server.

### Server plugins

Downloaded from their GitHub releases on first use and parked while switched off. Only the server loads them:
the launcher takes the Metamod line out of `gameinfo.gi` as soon as the server has read it, so your game never
loads them.

- **Bot Improver** ([ed0ard/CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver), AGPL-3.0, on in every
  quick start): more human bots (aim, grenades, movement, buying, pro names). Includes BotAI, Bot Controller and
  Bot Hider. Its section on Live Server sets bot aim, grenades, a weapon every bot buys, *Bot Skins* and
  Flying Scoutsman. Only its plugins are installed; the launcher manages `gameinfo.gi` itself.
- **Bot Hider** ([XBribo/CS2-Bot-Hider](https://github.com/XBribo/CS2-Bot-Hider), AGPL-3.0): bots look like
  players (no BOT tag, names, avatars, ping).
- **Bot Controller** ([XBribo/CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller), AGPL-3.0):
  `!record` your movement, `!replay <bot slot>`.
- **Deathmatch plugin** ([NockyCZ/CS2-Deathmatch](https://github.com/NockyCZ/CS2-Deathmatch), MIT, Deathmatch
  only): `!gun` menu, custom modes, kill sounds, spawn editor.

The rest of this README is for doing everything by hand, without the launcher.

---

## 1. Set up a local CS2 server

The cleanest way is a separate CS2 server install (below). If you want to use your normal game
folder instead, see [1b](#1b-or-use-your-normal-cs2-install-windows).
Changing your game's `gameinfo.gi` without `-insecure` can get you VAC-kicked. With a separate
server folder, your game stays untouched and you just connect to `localhost`.

1. Download [SteamCMD](https://developer.valvesoftware.com/wiki/SteamCMD) and install the server
   (about 60 GB):

   ```
   steamcmd +force_install_dir C:\cs2-server +login anonymous +app_update 730 validate +quit
   ```

2. Install **Metamod:Source 2.x** (latest dev build from <https://www.sourcemm.net/downloads.php?branch=dev>).
   Extract it into `C:\cs2-server\game\csgo\`.
   Then open `C:\cs2-server\game\csgo\gameinfo.gi` and add the `metamod` line right above
   `Game csgo`:

   ```
   SearchPaths
   {
       Game_LowViolence   csgo_lv
       Game    csgo/addons/metamod
       Game    csgo
       ...
   ```

3. Install **CounterStrikeSharp**: get the `with-runtime` build for your OS from
   <https://github.com/roflmuffin/CounterStrikeSharp/releases> and extract it into
   `C:\cs2-server\game\csgo\`.

   Then open `C:\cs2-server\game\csgo\addons\counterstrikesharp\configs\core.json` and set
   `"FollowCS2ServerGuidelines": false`. If `core.json` doesn't exist yet, create it with just
   `{ "FollowCS2ServerGuidelines": false }`. **Without this, the knife model changes but skins don't**:
   CounterStrikeSharp blocks the fields skins need. The launcher and `skin-test-ON.bat` do this for you.

4. Install **this plugin**. Download `LanSkinTester.zip` (or build it, see below) and extract it
   into `C:\cs2-server\game\csgo\`, so that you get
   `csgo\addons\counterstrikesharp\plugins\LanSkinTester\LanSkinTester.dll`.

5. *(Optional, recommended)* Install the skin names so you can type names instead of IDs. The
   names also let the plugin pick the right weapon model for older skins. Create the folder
   `...\plugins\LanSkinTester\skins\` and save these two files into it:
   - <https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/data/skins_en.json>
   - <https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/data/gloves_en.json>

   PowerShell:
   ```powershell
   $d = "C:\cs2-server\game\csgo\addons\counterstrikesharp\plugins\LanSkinTester\skins"
   mkdir $d -Force
   foreach ($f in "skins_en.json","gloves_en.json") {
     iwr "https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/data/$f" -OutFile "$d\$f"
   }
   ```

6. Start the server:

   ```
   C:\cs2-server\game\bin\win64\cs2.exe -dedicated -insecure +sv_lan 1 +map de_dust2 +game_type 0 +game_mode 1
   ```

   On Linux, use `./game/cs2.sh -dedicated -insecure +sv_lan 1 +map de_dust2`.

7. Start CS2 normally, open the console (`~`) and type `connect localhost`.
   Type `meta list` and `css_plugins list` in the **server** window to check everything loaded.

Useful server commands for testing: `mp_warmup_end`, `bot_kick`, `mp_roundtime 60`,
`mp_buy_anywhere 1`, `mp_maxmoney 60000`, `sv_cheats 1` (allows `thirdperson`, so you can look at
your gloves and weapon from outside).

## 1b. Or: use your normal CS2 install (Windows)

You don't need to download a second copy of CS2. Your game folder can run a server as well
(`cs2.exe -dedicated`). The only risk is `gameinfo.gi`: while the Metamod line is in it, you
**must** start CS2 with `-insecure` (VAC off). If you forget, VAC can see Metamod as a cheat.
Two scripts in `scripts/windows/` switch this on and off for you.

1. Find your CS2 folder: in Steam, right-click CS2 → *Manage* → *Browse local files*.
   That opens `...\steamapps\common\Counter-Strike Global Offensive`.
2. Extract **Metamod 2.x dev**, **CounterStrikeSharp with-runtime** and **LanSkinTester.zip**
   into `game\csgo\` inside that folder (the same as steps 2–5 above, **but don't edit `gameinfo.gi`
   yourself**). Just having these files in the folder does nothing until `gameinfo.gi` points to them.
3. Copy `skin-test.ps1`, `skin-test-ON.bat` and `skin-test-OFF.bat` from `scripts/windows/`
   anywhere (for example your desktop).
4. **To test skins:** close CS2 and double-click `skin-test-ON.bat`. It
   - adds the Metamod line to `gameinfo.gi` (and saves a `gameinfo.gi.backup`)
   - starts a local server window (`-dedicated -insecure +sv_lan 1 +map de_dust2`)
   - starts CS2 with `-insecure` and connects to `localhost`

   Type `!skinhelp` in chat. Use `!knife`, `!skin`, `!gloves` and the other commands.
5. **When you're done:** close CS2 **and** the server window, then double-click `skin-test-OFF.bat`.
   It removes the Metamod line and your game is back to normal for matchmaking, Premier and FACEIT.

If the scripts can't find CS2 (for example in an unusual Steam library), run in PowerShell:
`powershell -ExecutionPolicy Bypass -File skin-test.ps1 on -GamePath "D:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"`.
To try another map, add `-Map de_mirage`.

If anything goes wrong, Steam → CS2 → *Properties* → *Installed Files* → *Verify integrity of
game files* also puts back the original `gameinfo.gi`. A CS2 update can do the same, and after
that you just run `skin-test-ON.bat` again.

## 2. Commands

Type these in chat with `!` (for example `!skin redline`) or in the console with `css_` (for
example `css_skin 282`). Most commands change **the weapon in your hands**.

| Command | What it does |
| --- | --- |
| `!skin <id or name> [wear] [seed]` | Put a skin on the weapon/knife in your hands. Example: `!skin 344 0.01 0` or `!skin dragon lore` |
| `!wear <0-1>` | Change the wear: 0.00–0.07 Factory New, 0.07–0.15 Minimal Wear, 0.15–0.38 Field-Tested, 0.38–0.45 Well-Worn, 0.45–1 Battle-Scarred |
| `!seed <0-1000>` | Change the pattern seed (Case Hardened blue gems, Fade %, Crimson Web web positions, …) |
| `!st [kills or off]` | Turn StatTrak on or off, or set the kill count: `!st 1337` |
| `!nametag <text or off>` | Add a name tag (max 20 characters) |
| `!legacy` | Swap between the old and new weapon model. Use it if a skin looks stretched or broken |
| `!skinreset` | Remove the skin from the weapon in your hands |
| `!knife <name or default or list>` | Pick a knife: bayonet, classic, flip, gut, karambit, m9, huntsman, falchion, bowie, butterfly, daggers, paracord, survival, ursus, navaja, nomad, stiletto, talon, skeleton, kukri |
| `!gloves <type> <id or name> [wear] [seed]` | Gloves: brokenfang, bloodhound, sport, driver, wraps, moto, specialist, hydra. `!gloves off` resets them |
| `!skinsearch <text>` | Find paint IDs by name (needs the skin names from step 5) |
| `!gen <code>` / `!inspect <link>` | Import a real item with its exact float, pattern and stickers (see [Importing real items](#importing-real-items)) |
| `!skinhelp` | Show the command list in game |
| `!give <weapon>` | Give yourself a weapon: `!give ak47`, `!give awp`, `!give deagle`, `!give knife` |
| `!3p` / `!fp` | Third person / first person, to look at your skins |
| `!respawn` | Respawn yourself |
| `!noclip` | Fly through walls (type it again to land) |
| `!god` | God mode (type it again to turn it off) |
| `!save [name]` / `!load [name]` / `!spots` | Save where you stand and go back. Names are optional |
| `!rethrow` | Back to your last grenade throw, with that grenade |
| `!speed` / `!cs` | Jump stats on screen / counter-strafe check on your shots |
| `!savenade <name>` / `!lineup <name>` / `!lineups` / `!delnade <name>` | Save your last throw as a lineup for this map (kept across restarts, shared by everyone), go back to it with that grenade, list or delete them |
| `!damage` | Toggle the damage report in chat |
| `!armor` / `!nades` / `!clear` | Armor and a kit, a set of grenades, or remove smokes and fires |
| `!swap` | Swap the two teams |
| `!hacks` | Who is hacking in hacker mode |
| `!cheats` | Open the cheat menu (W/S move, D switch, R close) |
| `!aimkey fire\|always\|mouse2\|shift\|e\|f` | Your own aimbot key |
| `!aimtoggle` | Aimbot always on / back to your key (bind a key: `bind x css_aimtoggle`) |

The practice commands (`!give`, `!3p`, `!respawn`, `!noclip`, `!god`, `!save`, `!rethrow` and the rest) only work on a LAN or private server, like the skins.

### Examples

```
!skin 282                 AK-47 Redline (hold the AK)
!skin 344 0.0001          AWP Dragon Lore, as clean as possible
!knife karambit           then hold the knife and:
!skin 38 0.01 412         Karambit Fade, seed 412
!skin 44                  Case Hardened, then try different blue-gem seeds with !seed 387
!gloves sport 10037       Sport Gloves | Pandora's Box
!gloves driver 10041 0.2  Driver Gloves | King Snake, Field-Tested
!st 1337                  StatTrak with 1337 kills
```

Some common paint IDs: Redline 282, Vulcan 302, Fire Serpent 180 (AK) · Dragon Lore 344,
Asiimov 279 (AWP) · Howl 309 (M4A4) · Printstream 984 (M4A1-S) · Kill Confirmed 504 (USP-S) ·
Blaze 37 (Deagle) · Fade 38, Case Hardened 44, Doppler 415–421, Marble Fade 413 (knives).
To find more, use `!skinsearch` or look up the "Finish Catalog" / paint index on sites like csgostash.

### Server console commands

The launcher's quick actions use these. You can also type them in the server window:

| Command | What it does |
| --- | --- |
| `lst_give weapon_ak47` | Give a weapon to every player (with your skin on it) |
| `lst_view third` / `lst_view first` | Switch players to third or first person |
| `lst_respawn` | Respawn dead players |
| `lst_refresh` | Re-apply skins to everyone |
| `lst_show_damage 1` / `0` / `toggle` | Damage report in chat |
| `lst_god` / `lst_noclip` | God mode / noclip for every human |
| `lst_armor` / `lst_nades` / `lst_clear` | Armor and a kit, grenades, or remove smokes and fires |
| `lst_spot save` / `lst_spot load` | Save or load a practice spot |
| `lst_rethrow` | Back to the last grenade throw |
| `lst_drill clear\|retake\|hold\|reflex\|flick [a\|b] [bots] [weapon]` / `lst_drill off` | Start or stop a Trainer drill (`!drill` in chat) |
| `lst_speedo 1` / `lst_speedo 0` | Speedometer and jump stats for every human |
| `lst_lineup <name>\|next` / `lst_delnade <name>` | Send everyone to a saved lineup, or delete one |
| `lst_goto x y z pitch yaw smoke\|flash\|molotov\|he [text]` | Send everyone to a spot, aimed, with that grenade |
| `lst_nadeinfo 1` / `lst_cstrafe 1` | Grenade air time in chat / counter-strafe check on every shot (`0` turns off) |
| `lst_swap` | Swap the teams |
| `lst_endround ct` / `t` | End the round |
| `lst_score <ct> <t>` | Set the score |
| `lst_pause on` / `off` | Pause or resume |
| `lst_hack off` / `all` / `one` | Hacker mode: off, everyone hacks, or one random hacker per round |
| `lst_hack_powers wallhack,boxesp,boneesp,tags,aimbot,trigger,aimwalls,onetap,ammo,speed,bhop,noflash,espinfo,norecoil,radar` | Which hacker powers are on (`onetap` = damage boost; `all` or `none` also work) |
| `lst_aim_target closest\|crosshair\|health\|threat` | Who the aimbot picks: nearest, closest to the crosshair, lowest HP, or who is aiming at you |
| `lst_aim_style legit\|silent\|rage` | Smooth aim, bullet-only aim, or snap-and-shoot |
| `lst_aim_bone head\|neck\|chest` | Body part for the aimbot and the triggerbot |
| `lst_aim_key fire\|always\|mouse2\|shift\|e\|f\|custom` | When the aimbot works (`custom`: only a key bound to `+lst_aim`) |
| `lst_aim_aggression 1-10` | Default aimbot aggression (players can change their own in `!cheats`) |
| `lst_bot_no_snipers 1` / `0` | Bots only keep the rifles `lst_bot_rifles` allows (snipers and other rifles are swapped) |
| `lst_bot_rifles ak47,m4a1,m4a1_silencer` | Rifles bots may keep (`famas`, `galilar`, `aug`, `sg556` too) |
| `lst_stats_reset` | Start the session stats over |
| `lst_bot_skins 1` / `0` / `reroll` | Random skins, knife and gloves on every bot (from the skins list), or new ones |
| `lst_shared_loadout 1` / `0` | Everyone uses the launcher's skins / each player picks their own |
| `lst_private_server 1` | Allow skins with `sv_lan 0` (Radmin / VPN friends). Only for your own private server |
| `lst_host_account <id>` | The host's Steam account id: the host always gets the launcher's loadout |

## Importing real items

You can copy a real item exactly: the same skin, float, pattern, StatTrak, name tag, stickers and charm.
Use the chat commands `!gen` / `!inspect`. These formats work:

| Paste this | Exact float + pattern + stickers? |
| --- | --- |
| **Inspect link with item data**: `steam://run/730//+csgo_econ_action_preview%2000180720...` (made by the game for previews, and by CSFloat and similar sites) | Yes |
| **!gen code**: `!gen 7 282 661 0.15 4787 0 0 0 ...` (CSFloat: open a listing → *Copy !gen*) | Yes |
| **Old Steam Market "Inspect in Game" link** (`...M625254122282020305A6760596155D7629834065155301373`) | Not possible: see below |

Old market inspect links only point to the item on Valve's servers; the skin data isn't in the link.
Resolving them needs a logged-in Steam account running a bot that asks Valve's game coordinator, which
is what sites like CSFloat do. So for a market listing, open it on CSFloat (or a similar site) and copy
its **!gen** code or inspect link instead.

In game:

```
!gen 9 344 661 0.0123 4787 0 0 0 5935 0.2 0 0 0 0     AWP Dragon Lore, float 0.0123, pattern 661, 2 stickers
!inspect steam://run/730//+csgo_econ_action_preview%2000180720...
```

The item is equipped right away. If it's a weapon you don't have, you get it.

## 3. Config

When the plugin loads for the first time, CounterStrikeSharp creates
`csgo/addons/counterstrikesharp/configs/plugins/LanSkinTester/LanSkinTester.json`:

```json
{
  "RequireSvLan": true,
  "ChatPrefix": "[Skins]",
  "SingleLoadout": true,
  "ConfigVersion": 1
}
```

With `SingleLoadout` on (the default), everyone on the server shares one loadout, which is the one
the launcher edits. Turn it off if friends join your LAN server and each should have their own skins.
The launcher's Friends page overrides this setting with `lst_shared_loadout`. The host (the player on
the server's PC) always uses the launcher's loadout.

Saved loadouts are in `plugins/LanSkinTester/loadouts.json`.

## 4. Troubleshooting

- **The knife model changes but no skins show**, or chat says "Skins are blocked by CounterStrikeSharp":
  set `"FollowCS2ServerGuidelines": false` in `addons/counterstrikesharp/configs/core.json` and
  restart the server. The launcher does this when you press Start.
- **Nothing happens / "Skins are disabled"**: start the server with `+sv_lan 1`.
- **Gun skins work but gloves and StatTrak don't, and the server log says it could not load
  `CAttributeList_SetOrAddAttributeValueByName`**: a CS2 update changed the game code. Copy the
  updated signature from
  [cs2-WeaponPaints gamedata](https://github.com/Nereziel/cs2-WeaponPaints/blob/main/gamedata/weaponpaints.json)
  into `plugins/LanSkinTester/gamedata/lanskintester.json` and restart the server.
- **The plugin doesn't load after a CS2 update**: update Metamod and CounterStrikeSharp to their
  latest builds. CS2 updates often break them for a day or two.
- **The skin looks stretched or the texture is in the wrong place**: type `!legacy`.
- **The gloves look doubled for a moment**: switch weapons once, or respawn.

## Building from source

GitHub Actions builds and tests everything on every push (on Windows) and publishes `main` as the **Latest
build** release. Other branches' builds are under *Actions → Build → Artifacts* (they don't update
themselves).

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) for the plugin and [Rust](https://rustup.rs)
for the launcher, a [Tauri](https://tauri.app) app.

```
./build.sh
```

This makes `build/LanSkinTester.zip` (extract into `game/csgo/`), `build/LanSkinTesterLauncher.exe` (plugin
built in) and, with NSIS installed, `build/LanSkinTesterSetup.exe`. The launcher builds on Windows (Git Bash),
or on Linux with [cargo-xwin](https://github.com/rust-cross/cargo-xwin). Plugin only, without bash:
`dotnet publish src/LanSkinTester -c Release -o out`, then copy `LanSkinTester.dll` and `gamedata` to
`plugins/LanSkinTester/`.

Tests: `dotnet test tests/LanSkinTester.Tests` (plugin) and `cargo test` in `src/Launcher` (launcher).
The launcher's UI is HTML/CSS/JS in `src/Launcher/web/` and its logic is Rust in `src/Launcher/src/`; see
`src/Launcher/DESIGN.md`.

## Credits

- Launcher: [Tauri](https://tauri.app) (MIT / Apache-2.0); UI: [Preact](https://preactjs.com) and [htm](https://github.com/developit/htm) (MIT / Apache-2.0), [Inter](https://rsms.me/inter/) (OFL).
- Skin, sticker, charm, agent and pin data and pictures: [ByMykel/CSGO-API](https://github.com/ByMykel/CSGO-API) (MIT).
- Inspect link format: checked against [cs2inspect](https://pypi.org/project/cs2inspect/).
- The skin-applying technique (attribute lists, knife subclass change, glove refresh, stickers, charms, agents, pins)
  and the function signature come from [cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints) by Nereziel.
- CS:GO legacy skins: [weapons](https://github.com/kgns/weapons) and [gloves](https://github.com/kgns/gloves) by kgns,
  [PTaH](https://github.com/komashchenko/PTaH) by komashchenko, [SourceMod and Metamod:Source](https://www.sourcemod.net) (downloaded, not bundled).
- Launcher font: [Inter](https://github.com/rsms/inter) by Rasmus Andersson (SIL Open Font License, `src/Launcher/web/fonts/Inter-LICENSE.txt`).
