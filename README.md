# LAN Skin Tester (CS2)

A [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) plugin for previewing
CS2 weapon skins, knives and gloves on **your own local / LAN server**: try a skin, its wear
and its pattern seed before you buy it.

- `!skin` any paint kit on the gun or knife in your hands, by ID or by name
- Change wear (`!wear 0.15`) and pattern seed (`!seed 661`) live
- Any knife (`!knife karambit`) and gloves (`!gloves sport 10037`)
- StatTrak counter, name tags, **stickers (5 slots) and charms**
- **Import real items** with their exact float, pattern and stickers from a `!gen` code or an inspect link
- Your loadout is saved and comes back after map changes and restarts
- Windows launcher: one-click install, game mode + bot settings, and an inventory-style skin picker

> **Only for your own local server.** Skins are visible only on this server and nobody else sees
> them. The plugin is off unless the server runs with `sv_lan 1`. Don't install it on a public
> community server: Valve's server rules don't allow fake inventory items there, and the server's
> login token (GSLT) can be banned.

---

## Easiest: the launcher (Windows)

**Download:** on GitHub, open **Releases → Latest build**. Every push builds the launcher and plugin
automatically (see `.github/workflows/build.yml`).

Pick one:
- **`LanSkinTesterSetup.exe` (recommended):** the installer. It installs for your Windows user only
  (no admin prompt) into `%LocalAppData%\Programs\LAN Skin Tester`, adds a Start menu shortcut and an
  optional desktop shortcut, and appears in Windows' *Installed apps*, so it can be uninstalled
  there. Uninstalling keeps your loadouts and settings.
- `LanSkinTesterLauncher.exe` (about 65 MB): the same launcher as one portable file.
- `LanSkinTesterLauncher-small.zip` (about 1 MB): extract it and run `LanSkinTesterLauncher.exe`.
  It needs the [.NET 10 Desktop Runtime and ASP.NET Core Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
  If you don't have it, Windows offers a download link the first time you open it.

The launcher has eight pages in its sidebar: Play, Live Server, Inventory, Cases, Hacker Mode, Workshop,
Friends and Setup (Ctrl+1 to Ctrl+8). **Ctrl+K** (or *Search* in the sidebar) finds any page, quick start or action:
type "dm" and press Enter to start Deathmatch. **F11** switches to full screen and back. Maximized or full screen on a big monitor, the
whole page zooms up to fill the window instead of staying small. The launcher shows a toast when a friend joins or
leaves your server. On first start it opens on **SETUP**. The window uses Microsoft's WebView2, which
Windows 10 and 11 already have; without it the launcher opens in your web browser instead.

**SETUP page**

- **CS2 folder:** found automatically (or press *Browse* / *Find automatically*).
- **What's installed:** a checklist of Metamod, CounterStrikeSharp, the plugin, the CounterStrikeSharp
  skin setting and the skin/sticker/charm lists. Each row shows its state and version, has its own
  Install/Update button, and shows a progress bar while downloading.
- **INSTALL / UPDATE EVERYTHING** does all of it in one go.
- **Check for updates** shows the latest Metamod and CounterStrikeSharp versions next to yours. The
  plugin row says when this launcher has a newer plugin.
- **Install from zip...** is for when an automatic download fails. Get the zip from the *Metamod
  downloads* / *CounterStrikeSharp downloads* links.
- **Repair** reinstalls the plugin and fixes settings and leftovers after a crash.
- **Uninstall everything** removes Metamod, CounterStrikeSharp, the plugin and the server config from
  your CS2 folder and restores `gameinfo.gi`. Your loadout is backed up first.
- **Open folder** buttons for the CS2 folder, the plugin folder, the server configs and the launcher's data.

**PLAY page**

1. **Quick start:** one click sets everything up and starts (or switches the running server):
   *DM vs Expert bots*, *Free-for-all*, *Skin showcase* (peaceful bots, third person),
   *Aim practice*, *Competitive 5v5*, *Arms Race*, *Demolition*, *Weapons Expert*,
   *Retakes* (Valve's 3v4 loadout-card mode), *Plugin retakes* (allocator and instadefuse),
   *Retake buy*, *Rush 3v3* when your CS2 has it, plus any setup you saved.
2. **Live Server** (its own page in the sidebar) is where the match is set up and driven. **Play** only has the quick starts and Start. On Live Server, settings are grouped: Game, Bots, Rules, Retakes (only in Retakes mode), Launch, and the live controls. Or pick everything yourself:
   - game mode (Deathmatch, Competitive, Casual, Wingman, Arms Race, Demolition, Weapons Expert, Custom, Retakes) and map
   - number of bots (0–20) and their skill (Easy, Normal, Hard, Expert)
   - which team the bots join, and their weapons: **Rifles** (the default: no AWPs, shotguns, SMGs or
     machine guns, and any sniper a bot still gets is swapped for an AK-47 / M4A4), pistols only,
     snipers only, knives only, or any weapons
   - options: free-for-all, max money with buy anywhere, infinite ammo, rounds that never end with
     respawn, bots that don't shoot, and cheats
   - **Show more options:** round time, friendly fire, a **Workshop map**, your own **CS2 launch
     options** (e.g. `-windowed -w 1920 -h 1080`), and whether CS2 starts and joins automatically.
   - **Workshop map:** paste the map's ID or its Workshop link. The server goes online (unlisted, no
     game server token) and loads it from the Workshop with `host_workshop_map`, so you need internet
     and the first start takes longer while it downloads. Players' games fetch the map automatically
     when they join. **Subscribe** opens the map in Steam, so your own game already has it.
     The server first starts on the normal map you picked, and once it's up (and logged on to Steam)
     the launcher switches it to the Workshop map. Asking for a Workshop map at startup happens before
     Steam is ready, and the server then just sits idle. CS2 waits until the Workshop map has loaded,
     then starts and joins by itself.
   - **If the server crashes or closes by itself**, the Activity log shows its last console lines
     (the server runs with `-condebug` and writes `csgo\console.log`). Send those lines along when
     you report a problem.
3. **START** opens a server window and CS2 (VAC off), which joins it. **Apply changes** sends new bot
   and option settings to the running server; changing the mode or map switches the map. **Open CS2**
   starts the game again if you closed it.
4. **Live Server** shows the map, mode, players and bots, and whether skins are working, updated
   every 2 seconds. The same page holds the server controls: round (restart, end warmup, pause, resume,
   swap, CT/T win), players (add CT, add T, kick, respawn, god), and practice (third person, noclip,
   armor, nades, clear, save/load spot, rethrow, damage report, refresh skins, give weapon).
   **Command** sends anything else to the server console. The buttons light up when the server is up.
5. **Stop & restore** closes the game and the server and puts CS2 back to normal for matchmaking.
   If you close the launcher while test mode is on, it asks you first.

**Retakes** has two styles, chosen in the Retakes group (shown only while the game mode is Retakes).

- **Valve** (the *Retakes* quick start) is the built-in mode: `game_type 0`, `game_mode 5`, and `sv_skirmish_id 12`. The bomb is already planted, it's 3 terrorists against 4 counter-terrorists, and each round starts with Valve's loadout cards. The first team to 8 wins. Bots fill the empty seats (the quick start adds 6, so you plus the bots is a full match). `sv_skirmish_id` stays set across map changes, so every other mode sets it back to `0`.
- **Plugin** (the *Plugin retakes* quick start) uses [B3none's cs2-retakes](https://github.com/B3none/cs2-retakes) (GPL-3.0). The first Start downloads it. It runs as competitive (`game_type 0`, `game_mode 1`) with `sv_skirmish_id 0`, and the launcher parks every retakes plugin outside the plugins folder whenever another mode or Valve retakes is running. Switching a plugin on or off, or changing a setting the plugin only reads at load, restarts the server.

In the Retakes group, Plugin style can set max players (2–10), the terrorist percent, team scramble and how many rounds trigger it, team balance, auto plant, bombsite announcement, and whether players skip the team menu.

**Guns** is either **Allocator** or **Buy**. Allocator is the maintained fork of [Yoni Lerner's retakes allocator](https://github.com/Micka2302/cs2-retakes-allocator-2.0). Rounds rotate through pistol, half-buy, and rifle, and `!guns` saves what you want. **Buy** (*Retake buy* quick start) turns the freeze into a buy: humans are stripped (knife and bomb stay) and get $16000. Bots keep the rifles. The guns, grenades, armor and kit you finish the buy with are remembered separately for T and CT. Buy time (4–20 seconds) and the bomb timer apply on the next round. Switching between Buy and Allocator restarts the server, because the allocator replaces the plugin's own gun hand-out.

**Instadefuse** ([B3none's cs2-instadefuse](https://github.com/B3none/cs2-instadefuse)) finishes a defuse the moment it starts when no terrorist, molotov, or grenade can stop it. It is on for both plugin quick starts. Turning it off restarts the server so the plugin unloads.

**Demolition** is gun-game with a bomb (`game_type 1`, `game_mode 1`) on de_lake, de_safehouse and de_shortdust. **Weapons Expert** is competitive where each gun can be bought once per match. CS2 no longer has its own mode for it, so the server runs competitive and the plugin takes back (and refunds) a gun you already bought (`lst_weapons_expert 1`). **Custom** is an open sandbox (`game_type 3`, `game_mode 0`). Arms Race, Demolition and both retake styles keep their own guns, money and round length.

**Your own quick starts.** *Save setup* stores the mode, map, bots and rules under a name. The new tile sits with the built-in ones and starts the same way: one click. Click the **×** on a saved tile to delete it. Friends, the server password and whose skins are used stay whatever the Friends page says.

**Live Server** controls, besides restart, respawn, third person and giving a weapon:

- *Pause* / *Resume*, *Swap* teams, *CT Win* / *T Win*, *End Warmup*
- *Add CT*, *Add T*, *Kick Bots*
- *God*, *Noclip*, *Armor* (helmet, and a kit for CTs), *Nades*
- *Clear* removes smokes, molotovs and grenades in the world
- *Save Spot* / *Load Spot* and *Rethrow* (back to where you last threw, with that grenade)
- *Damage* toggles a chat line for the damage you deal
- *Command* runs any server console command (Enter sends it)

The same practice tools work in chat: `!god`, `!save [name]`, `!load [name]`, `!spots`, `!rethrow`, `!damage`, `!armor`, `!nades`, `!clear`, `!swap`.

**Rush** (Valve's 3v3 tower mode): Valve doesn't publish its game type numbers, so the launcher
reads them and the map list from your CS2's own `gamemodes.txt` (inside `pak01_dir.vpk`). If your
CS2 has Rush, it appears in the mode list and the *Rush 3v3* quick start works.

**Appearance:** Setup → *Theme* and *Appearance*: ready-made themes (Midnight, OLED Black, Slate, CS2
Orange, Green Ops, Violet, Crimson, Ocean), your own accent, background and panel colours, panel opacity,
title bar colour (match theme, accent or Windows), corners, font, text size, a background picture (with a darken slider), *Reduce Transparency* and *Reduce
Motion*. Everything applies right away. *Theme Code › Copy* puts your look on the clipboard as a short
`LST1-…` code; a friend presses *Paste…* to use it. For full control, *Open custom.css* creates
`%LocalAppData%\LanSkinTester\custom.css` (with notes and examples); it loads after the built-in styles
and reloads when you click back into the launcher.

**Agents, pins and music kits:** Skins › *Agents, Pins & Music* sets your agent for each side, a
collectible pin on the scoreboard and a music kit (round music and your MVP anthem, from the next round).
The data comes from ByMykel's CSGO-API; press *Update* on Setup's item lists once to get them.

**Headshot only and pistols only** (Live Server › Rules, or the *Pistol HS Deathmatch* quick start):
only headshots (and knives) hurt players, and only pistols (Deagle, Tec-9, Five-SeveN, USP-S, Glock, P250,
CZ75, Dualies, R8, P2000) can be bought; other guns are greyed out in the buy menu, refunded if bought
and taken away if picked up. Bots get pistols too.

**Reload on kill** (Live Server › Rules, on in the *Pistol HS Deathmatch* quick start): a kill refills
your magazine at once, and spare ammo never runs out.

**One weapon and low gravity** (Live Server › Rules): AWP, Scout, Deagle or knives only, for everyone
including bots (other guns are taken away and greyed out in the buy menu), and Flying-Scoutsman-style
low gravity. Quick starts: *AWP Deathmatch*, *Flying Scoutsman*, *Knife Fight*, *Deagle Headshots*,
*1v1 Aim Duel* and *Wingman 2v2*. Every quick start uses Bot Improver's smarter bots.

**Calm Bots** (Live Server › Bots, on by default): a bot jumps at most once every 3 seconds, so bots stop
hopping around.

**CASES page**: open CS2 containers like in the game. Pick a case, see what it holds and the odds, press
*Unlock Container*: the reel spins and stops on what you got. Odds are Valve's (Mil-Spec 79.92%,
Restricted 15.98%, Classified 3.20%, Covert 0.64%, knife or gloves 0.26%); knife finishes are equally
likely, Doppler phases follow the community-measured split; the float is made the way CS2 does it (a wear
band first: FN 3%, MW 24%, FT 33%, WW 24%, BS 16%, then squeezed into the skin's own range), pattern
0-1000, StatTrak 10% in weapon cases. Everything you unbox goes to the page's *Inventory*, where
**Equip** puts a gun skin, knife, gloves, pin or music kit on in game. Every container can be opened:
weapon cases, sticker and autograph capsules, souvenir packages, pins, patches, music kit boxes and
graffiti. Nothing touches a Steam inventory.

**INVENTORY page** (the old Skins tab): what you unboxed, laid out like CS2's inventory: All Items,
Weapons, Knives & Gloves, Stickers & Graffiti, Collectibles; sort by newest, rarity, price or name. Click
an item to inspect it (exterior, the exact float on CS2's wear bar, pattern, market price, which case it
came from) and **Equip** / **Unequip** it. It replaces the old Skins page. **Stickers…** on a gun opens a sticker editor like CS2's: apply up to 5 stickers
from your inventory (applying uses the sticker up, like in the game), drag them anywhere on the gun, turn,
resize and scrape them; scraped all the way, a sticker comes off for good. An equipped gun changes in game
within a second (the launcher's preview is a guide). With the admin code, **Add Item**
puts any skin, knife, glove, sticker, agent, pin or music kit straight into the inventory, with the float,
pattern, StatTrak or Souvenir you pick.

**Prices** are real market prices: Skinport's public price list (refreshed every 12 hours, cached), with
the Steam Community Market for anything it lacks. USD, EUR or GBP (Cases page). Opening a weapon case
costs the case's market price plus a key (2.49 USD / 2.35 EUR / 2.05 GBP); the Cases page keeps a tally
of what you spent against what you unboxed.

**Unbox only and the admin code:** by default the Skins page only offers what you unboxed on the Cases page
(plus vanilla guns), agents are off, and the chat commands `!skin`, `!gen`, `!knife` and `!gloves` are off on
your server (`!case` still works). The owner's admin code (Cases page › *Admin code*) turns on Inventory ›
*Add Item* (any skin, knife, glove, sticker, agent, pin or music kit) on that PC; *back to unbox only* locks it again.

**Cases in game:** type `!cases` to list the real CS2 weapon cases and `!case <number>` to open one. It
spins on your screen with Valve's odds (knife or gloves 0.26%) and equips what you unbox with a random
float, pattern and a 10% StatTrak chance. Only on your server: your Steam inventory is never touched.

**Server plugins** (Live Server › *Server Plugins*, built into the plugin, each can be switched off):

- *Clean up dropped guns* (on by default): guns lying on the ground for 15 seconds disappear.
- *No fall damage* and *Auto bunnyhop* (everyone hops by holding jump).
- *Spawn protection:* 2.5 seconds without damage after spawning (you look see-through); shooting ends it.
- *!guns:* `!guns ak47 deagle` gives you those weapons on every Deathmatch respawn. `!guns off` stops it.
- *!ready:* the warmup waits until every player typed `!ready` (off by default). `!unready` takes it back.
- *!rtv map vote:* when half the players type `!rtv`, everyone votes the next map from the mode's list
  with `!vote 1`-`!vote 6` (20 seconds, or as soon as everyone voted).
- *Round MVP and !stats:* the round's best player in chat after each round; `!stats` shows your kills,
  deaths, headshot % and damage on this map, `!top` the best three.
- *Welcome message:* new players get one line with the commands they can use.
- *Bot Hider* ([XBribo/CS2-Bot-Hider](https://github.com/XBribo/CS2-Bot-Hider), AGPL-3.0): bots look like
  real players (no BOT tag, names, avatars, ping). Downloaded from its GitHub release on the first Start,
  and parked (not loaded) while switched off.
- *Bot Improver* ([ed0ard/CS2-Bot-Improver](https://github.com/ed0ard/CS2-Bot-Improver), AGPL-3.0): more
  human bots: aim, grenades, movement, strategy, buying, pro names. Only its plugins are installed (its
  package's gameinfo.gi, mode configs and bot profiles are left out: the launcher manages gameinfo.gi and
  restores it for online play). Includes BotAI, Bot Controller and Bot Hider. While it's on, Live Server
  gets a *Bot Improver* section: bot aim (mixed / head / body), how many grenades bots throw, a weapon
  every bot buys, *Bot Skins* (random skins, knives, gloves and agents on bots; off by default, so
  bots look like normal CS2 bots), *Reroll Bot Skins* and Flying Scoutsman on/off. Changes reach a running server right away.
- *BotAI* ([ed0ard/CS2-BotAI](https://github.com/ed0ard/CS2-BotAI), the maintained fork of
  [Austinbots/CS2-BotAI](https://github.com/Austinbots/CS2-BotAI)): just the smarter bot decisions.
- *Bot Controller* ([XBribo/CS2-Bot-Controller](https://github.com/XBribo/CS2-Bot-Controller), AGPL-3.0):
  `!record` your movement, `!replay <bot slot>` to have a bot copy it.
- *Deathmatch plugin* ([NockyCZ/CS2-Deathmatch](https://github.com/NockyCZ/CS2-Deathmatch), MIT, Deathmatch
  mode only): `!gun` weapon menu, custom modes (only AWP, pistols, ...), kill sounds and a spawn editor. It
  replaces the built-in spawn protection and `!guns`. Downloaded on first use like Bot Hider.

**Hacker mode (fun)** has its own page in the sidebar and is always Off when the launcher starts (or use the quick starts *Hacker vs bots* and
*Catch the hacker*). Changes there apply to the running server right away, no Apply needed.
*Quick profiles* (Legit, Closet, Rage, Movement, All off) set every cheat in one click:

- **Who hacks:** *Everyone* (great against expert bots) or *One random*. With *One random*, a new
  random player gets the hacks each round and glows green for everyone else, so the rest hunt them.
- **Powers** (each can be turned off):
  - *Chams:* a solid colored copy of each player, visible through walls. Enemies and teammates use
    different colors, and only hackers see them.
  - *Box ESP:* a 3D wire box around every player, through walls. The same beams follow the player, so
    turning the profile on does not spawn a new box every step.
  - *Skeleton ESP:* a stick figure on the body (crouch and facing). The server has no client bone
    overlay, so this is drawn in the world and only sent to hackers.
  - *Name tags:* name, HP, armor, gun and grenades (including the bomb) floating above each player.
  - *Aimbot style:* **Legit** pulls the crosshair smoothly after a short reaction. **Silent** never moves
    your view: when a shot of yours would miss, the enemy nearest your crosshair (inside the aim area)
    takes the hit. **Rage** snaps onto the target and shoots.
    The triggerbot presses attack when an enemy is under the crosshair, after a short random delay.
  - *Body:* head, neck or chest. Legit and silent still miss by a little at low aggression.
  - *Aim at:* **closest enemy**, **nearest crosshair**, **lowest HP**, or **highest threat** (the enemy
    whose aim is closest to you).
  - *Aim key:* hold fire (default), always on, hold right click, hold Shift, hold E or hold F.
    **Own aim key:** type any key (`mouse4`, `mouse5`, `x`, `alt`…) and the launcher binds it in CS2
    on this PC (`bind mouse4 +lst_aim`, from `cfg/lanskintester_client.cfg`). The key's old binding
    is replaced. In game: `!aimkey mouse2` picks your own, `!aimtoggle` (or `bind x css_aimtoggle`)
    turns the aimbot always-on.
  - *Aim through walls:* the aimbot and the triggerbot also lock through walls (off by default).
    Otherwise a line-of-sight trace from your eyes to their head or chest has to be clear (walls,
    floors, props and glass block it; smokes don't).
  - *Triggerbot:* fires the moment an enemy model sits on your crosshair, after a small random delay
    so it is less instant. Higher aggression shortens that delay. It really shoots: CS2 doesn't let a
    server press fire for a player, so the plugin tells the launcher on your PC (localhost, port 27099)
    and the launcher clicks your left mouse button, once per shot at the gun's fire rate. It only clicks
    while the launcher's VAC-off session runs and the CS2 window is in front. Friends on their own PCs
    don't have that, so for them the server can only hold fire (CS2 may ignore it).
  - *Damage boost:* 2× damage (off by default).
  - *Recoil control:* sprays stay on the crosshair. The plugin clears each hacker's recoil kick every
    tick, and the server-wide recoil cvars are set too.
  - *Zero spread:* every bullet goes exactly where you aim. CS2 only has this server-wide
    (`weapon_accuracy_nospread`), so it applies to everyone, bots included.
  - *Radar hack:* every enemy shows on your radar, even when they make no noise.
  - *Bunnyhop:* hold jump to bhop perfectly.
  - *No flash:* flashbangs don't blind you, and smoke entities are not sent to you.
  - *Enemy info:* the three nearest enemies in the middle of the screen: name, HP, armor, gun,
    grenades, distance, direction, and whether you can see them.
  - *Infinite ammo.*
  - *Speedhack:* 35% faster.
- **In-game cheat menu:** type `!cheats` (or bind a key once in the CS2 console: `bind F3 css_cheats`).
  It appears as a board of world text in front of you, only visible to you (it doesn't use the
  flickery center-screen panel):
  - **W / S** move, **D** or **E** switches / turns a slider up, **A** turns it down, **R** closes.
    You stand still while it's open.
  - Each player switches their own cheats: chams, box ESP, skeleton, name tags, aimbot, **style**
    (legit / silent / rage), body part, **aggression** (a 1-10 slider), aim key, aim target, triggerbot,
    recoil control, enemy info, radar, speedhack, bunnyhop, infinite ammo, no flash, damage boost,
    third person, or everything off.
- **Aim aggression** (1-10, the slider on the Hacker page is the default): 1 is gentle and loose
  (small 9° lock-on area, slow pull, ~230 ms reaction, a little off the body part you picked, and the
  trigger waits longer); 10 is a hard lock (36° area, fast pull, ~50 ms reaction, dead-centre on that
  body part, trigger almost immediate). Rage style snaps instead of easing in.
  - It only works in hacker mode. With **One random**, only that round's hacker gets it.
- Hacker kills get silly chat lines ("{victim} has been diagnosed with a skill issue"). `!hacks` in
  chat shows who is hacking.
- It all runs on your server: glowing copies of the player models that only some players receive,
  view angles set by the server, and extra damage. Nobody's game is modified, so it's as safe as the
  rest of this tool.

**SKINS page**

- On the left, pick a category (Knives, Gloves, Pistols, Rifles, SMGs, Heavy) and a weapon. Your
  equipped knife and gloves are marked **EQUIPPED**.
- In the middle, skin cards with pictures and rarity colours. Search by name or rarity.
- On the right, the preview and settings:
  - **Wear:** a slider plus FN / MW / FT / WW / BS buttons. Only conditions the skin really exists in
    can be picked; a Karambit Fade only goes up to 0.08.
  - **Pattern seed:** with a *Random* button.
  - **StatTrak™** with a kill count, and a **name tag**.
  - **5 sticker slots and a charm:** click a slot to search 11,000+ stickers (filter Holo, Foil,
    Gold…, with a scrape amount); right-click a slot to remove it.
- **EQUIP** (or double-click a card) saves it, and your weapon changes in game about a second later.
- **Copy !gen** / **Copy inspect link** copy your creation so you can share it.
- **Import real item...** opens the import window (see [Importing real items](#importing-real-items)).
- **Loadouts...**: save your whole loadout under a name and load it again later, **Randomize
  everything** (a random skin on every weapon plus a random knife and gloves, with realistic floats),
  or **Reset everything**.

**WORKSHOP page**: every map Steam has downloaded for CS2 (title, picture, size), a search of the Steam
Workshop (popular, new, top rated) and **Host**, which starts the server on that map (or switches the
running one). *Subscribe* opens the map in Steam; it shows under Downloaded once Steam has it. Custom
weapon and player models aren't supported: CS2 servers can't hand those to players without extra
add-on plugins and every player installing the same files.

**FRIENDS page**: play together

*Friends List:* friends hosting on your Wi-Fi or Radmin network show up by themselves (the launcher
announces your server on UDP port 27098 while you host). Add other friends with their invite code; the
list checks every few seconds whether they're hosting and shows **Join**.

*Host (you):*
1. **How are you connected?**
   - *Same Wi-Fi*: your friend is at your place.
   - *Radmin / Hamachi*: over the internet with the free [Radmin VPN](https://www.radmin-vpn.com/).
     You both join the same Radmin network first.
   - *Internet (UPnP)*: over the internet with nothing else to install. The launcher asks your router
     to open UDP port 27015 (UPnP) when you start hosting, and closes it again when you press Stop.
     A password is always used in this mode (one is made for you if you leave it empty).
2. **Your address** is found automatically: your `192.168.x.x` address, your Radmin `26.x.x.x`
   address, or your internet address from the router. *Copy Code* copies just the **invite code**
   (`LST-…`), which holds the address, port and password in one piece. The Play page's Server card has
   the same **Copy IP** and **Copy Code** buttons.
3. **Options:**
   - **Skins:** *Everyone uses mine*, or *Each picks own*. With *Each picks own*, friends use `!skin`,
     `!knife`, `!gloves` and `!gen` in chat, and you keep the launcher's loadout.
   - **Password** is optional.
   - **Windows Firewall:** press *Allow* once (it asks for admin).
4. **Start Hosting** uses the mode, map and bots from the Play page. The list underneath shows who joined.

*Friend:* download the launcher, open **Friends → Join a Friend**, paste the invite code (or the whole
invite message, an address, `connect …` or a `steam://connect/…` link) and press **Join Game**.
If CS2 is already open, it connects without restarting (through Steam); otherwise CS2 starts with VAC
off for that game. The last servers you joined appear as **Recent** buttons for one-click rejoin.
They don't need to install anything: the launcher only starts CS2 with `-insecure +connect <address>`.
Next time they start CS2 from Steam it's completely normal.

*Why this won't get you banned:*
- `-insecure` is Valve's own switch: VAC simply doesn't run for that game. Nothing is hidden or bypassed.
- Your Steam inventory is never touched. Skins only exist on this server.
- Your friend's game files are not changed at all. Yours only change while test mode is on, and **Stop**
  puts them back.
- The server has no game server token and isn't listed in the server browser. Don't run this plugin on
  a public server: that's against Valve's server rules.

Radmin, Hamachi and internet players aren't "local" for CS2's `sv_lan 1`, so in those modes the server
runs with `sv_lan 0` and the launcher tells the plugin it's a private server (`lst_private_server 1`).

*If Internet (UPnP) doesn't work:*
- "No UPnP router found": turn on UPnP in your router's settings (often under *Advanced* or *NAT*), then press *Retry*.
- "Your provider shares one address (CGNAT)": your internet provider doesn't give you your own
  address, so nobody can connect in. This is common on mobile internet and some fiber. Use Radmin instead.
- "Port 27015 is already forwarded to another PC": remove that rule in your router, or host from that PC.
- You can also forward UDP port 27015 to your PC in the router by hand and press *Start anyway*.

**Handy extras**

- **Restores CS2 by itself.** When CS2 and the server are closed (with the X, after a crash, or
  even with the launcher closed in between), the launcher switches test mode off a few seconds
  later. You don't have to remember Stop before playing online.
- **Updates itself.** When GitHub has a newer build (checked at start and every few hours), a blue
  *Update available* card appears in the sidebar. Click it: the launcher downloads the new version,
  closes, swaps itself and starts again (not while test mode is on). If its folder isn't writable,
  it opens the download page instead. The sidebar shows which build you have.
- **Notifications.** Short pop-ups at the bottom confirm actions (equipped, copied, started, applied…).
- **Remembers** the window size and position and the page you had open.
- **Keyboard shortcuts:**

  | Keys | Does |
  | --- | --- |
  | `Ctrl+1` … `Ctrl+5` | Play, Skins, Hacker Mode, Friends, Setup |
  | `Ctrl+F` | Search skins |
  | `F5` | Start, or apply changes to the running server |
  | `Shift+F5` | Stop |

**The plugin updates itself:** every launcher build carries the matching plugin. When you press
Start, the launcher compares the installed plugin with its own and replaces it if they differ, so
new features and fixes work without pressing Update on the Setup page.

While test mode is on, start CS2 **only from the launcher**. Starting it from Steam in that state
(without `-insecure`) could get you VAC-kicked. The launcher shows an orange warning when test mode is on.

The rest of this README is for doing everything by hand.

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
| `lst_bot_no_snipers 1` / `0` | Bots never keep a sniper rifle (swapped for an AK-47 / M4A4) |
| `lst_shared_loadout 1` / `0` | Everyone uses the launcher's skins / each player picks their own |
| `lst_private_server 1` | Allow skins with `sv_lan 0` (Radmin / VPN friends). Only for your own private server |
| `lst_host_account <id>` | The host's Steam account id: the host always gets the launcher's loadout |

## Importing real items

You can copy a real item exactly: the same skin, float, pattern, StatTrak, name tag, stickers and charm.
Use the launcher's **Import real item...** button or the chat commands `!gen` / `!inspect`. These
formats work:

| Paste this | Exact float + pattern + stickers? |
| --- | --- |
| **Inspect link with item data**: `steam://run/730//+csgo_econ_action_preview%2000180720...` (made by the game for previews, and by CSFloat and similar sites) | Yes |
| **!gen code**: `!gen 7 282 661 0.15 4787 0 0 0 ...` (CSFloat: open a listing → *Copy !gen*) | Yes |
| **Steam Market listing name or link**: `StatTrak™ AK-47 \| Redline (Field-Tested)` (launcher only) | No: picks a typical float for the condition |
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

GitHub Actions builds everything on every push and puts the files on the **Latest build** release.
Builds of other branches are under *Actions → Build → Artifacts*. To build yourself:

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
./build.sh
```

This creates `build/LanSkinTester.zip` (ready to extract into `game/csgo/`),
`build/LanSkinTesterLauncher.exe` and `build/LanSkinTesterLauncher-small.zip` (the launcher, with the plugin built in).
On Windows without bash, run `dotnet publish src/LanSkinTester -c Release -o out` and copy
`LanSkinTester.dll` and the `gamedata` folder to `plugins/LanSkinTester/`.

The launcher's UI is HTML/CSS/JS in `src/Launcher/web/` (see `src/Launcher/web/DESIGN.md`). Run
`LanSkinTesterLauncher.exe --serve 5099 --token dev --web-dir src\Launcher\web` to open it in a normal
browser at http://127.0.0.1:5099/?t=dev and see edits on reload.

## Credits

- Launcher UI: [Preact](https://preactjs.com) and [htm](https://github.com/developit/htm) (MIT / Apache-2.0), [Inter](https://rsms.me/inter/) (OFL).
- Skin, sticker, charm, agent and pin data and pictures: [ByMykel/CSGO-API](https://github.com/ByMykel/CSGO-API) (MIT).
- Inspect link format: checked against [cs2inspect](https://pypi.org/project/cs2inspect/).
- The skin-applying technique (attribute lists, knife subclass change, glove refresh, stickers, charms, agents, pins)
  and the function signature come from [cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints) by Nereziel.
- Launcher font: [Inter](https://github.com/rsms/inter) by Rasmus Andersson (SIL Open Font License, `assets/fonts/Inter-LICENSE.txt`).
