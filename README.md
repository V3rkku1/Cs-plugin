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

Two versions, same app:
- `LanSkinTesterLauncher.exe` (about 50 MB): one file, nothing else to install.
- `LanSkinTesterLauncher-small.zip` (about 0.1 MB): extract it and run `LanSkinTesterLauncher.exe`.
  It needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).
  If you don't have it, Windows offers a download link the first time you open it.

The launcher has four pages: Play, Skins, Friends and Setup. On first start it opens on **SETUP**.

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
   *Aim practice*, *Competitive 5v5*, *Arms Race*.
2. **Server:** or pick everything yourself:
   - game mode (Deathmatch, Competitive, Casual, Wingman, Arms Race) and map
   - number of bots (0–20) and their skill (Easy, Normal, Hard, Expert)
   - which team the bots join, and whether they use any weapons, pistols only, snipers only or knives only
   - options: free-for-all, max money with buy anywhere, infinite ammo, rounds that never end with
     respawn, bots that don't shoot, and cheats
   - **Show more options:** round time, friendly fire, a **Workshop map** (id or link; the server
     downloads it, so you need internet), your own **CS2 launch options** (e.g. `-windowed -w 1920 -h 1080`),
     and whether CS2 starts and joins automatically.
3. **START** opens a server window and CS2 (VAC off), which joins it. **Apply changes** sends new bot
   and option settings to the running server; changing the mode or map switches the map. **Open CS2**
   starts the game again if you closed it.
4. **Live server** shows the map, mode, players and bots, and whether skins are working, updated
   every 2 seconds. Quick actions: *Restart*, *End warmup*, *Add bot*, *Kick bots*, *Third person*,
   *First person*, *Respawn me*, *Refresh skins*, and *Give weapon* (any gun, or your knife).
5. **Stop & restore** closes the game and the server and puts CS2 back to normal for matchmaking.
   If you close the launcher while test mode is on, it asks you first.

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

**FRIENDS page**: play together

*Host (you):*
1. **How are you connected?** Pick *Same Wi-Fi / LAN* (your friend is at your place) or
   *Radmin VPN / Hamachi* (over the internet: install the free [Radmin VPN](https://www.radmin-vpn.com/)
   and both join the same network).
2. **Your address** is found automatically (your `192.168.x.x` or Radmin `26.x.x.x` address). *Copy
   Invite* copies a short message with the download link, the address and the password.
3. **Options:**
   - **Skins:** *Everyone uses mine*, or *Each picks own*. With *Each picks own*, friends use `!skin`,
     `!knife`, `!gloves` and `!gen` in chat, and you keep the launcher's loadout.
   - **Password** is optional.
   - **Windows Firewall:** press *Allow* once (it asks for admin).
4. **Start Hosting** uses the mode, map and bots from the Play page. The list underneath shows who joined.

*Friend:* download the launcher, open **Friends → Join a Friend**, and type the address and password.
They don't need to install anything: the launcher only starts CS2 with `-insecure +connect <address>`.
Next time they start CS2 from Steam it's completely normal.

*Why this won't get you banned:*
- `-insecure` is Valve's own switch: VAC simply doesn't run for that game. Nothing is hidden or bypassed.
- Your Steam inventory is never touched. Skins only exist on this server.
- Your friend's game files are not changed at all. Yours only change while test mode is on, and **Stop**
  puts them back.
- The server has no game server token and isn't listed in the server browser. Don't run this plugin on
  a public server: that's against Valve's server rules.

Radmin and Hamachi addresses aren't "local" for CS2's `sv_lan 1`, so in that mode the server runs with
`sv_lan 0` and the launcher tells the plugin it's a private server (`lst_private_server 1`).

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

## Credits

- Skin, sticker and charm data and pictures: [ByMykel/CSGO-API](https://github.com/ByMykel/CSGO-API) (MIT).
- Inspect link format: checked against [cs2inspect](https://pypi.org/project/cs2inspect/).
- The skin-applying technique (attribute lists, knife subclass change, glove refresh, stickers, charms)
  and the function signature come from [cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints) by Nereziel.
