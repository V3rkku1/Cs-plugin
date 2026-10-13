# LAN Skin Tester (CS2)

A [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) plugin for previewing
CS2 weapon skins, knives and gloves on **your own local / LAN server**: try a skin, its wear
and its pattern seed before you buy it.

- `!skin` any paint kit on the gun or knife in your hands, by ID or by name
- Change wear (`!wear 0.15`) and pattern seed (`!seed 661`) live
- Any knife (`!knife karambit`) and gloves (`!gloves sport 10037`)
- StatTrak counter and name tags
- Your loadout is saved and comes back after map changes and restarts

> **Only for your own local server.** Skins are visible only on this server and nobody else sees
> them. The plugin is off unless the server runs with `sv_lan 1`. Don't install it on a public
> community server: Valve's server rules don't allow fake inventory items there, and the server's
> login token (GSLT) can be banned.

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

## 3. Config

When the plugin loads for the first time, CounterStrikeSharp creates
`csgo/addons/counterstrikesharp/configs/plugins/LanSkinTester/LanSkinTester.json`:

```json
{
  "RequireSvLan": true,
  "ChatPrefix": "[Skins]",
  "ConfigVersion": 1
}
```

Saved loadouts are in `plugins/LanSkinTester/loadouts.json`.

## 4. Troubleshooting

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

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
./build.sh
```

This creates `build/LanSkinTester.zip`, ready to extract into `game/csgo/`.
On Windows without bash, run `dotnet publish src/LanSkinTester -c Release -o out` and copy
`LanSkinTester.dll` and the `gamedata` folder to `plugins/LanSkinTester/`.

## Credits

The skin-applying technique (attribute lists, knife subclass change, glove refresh) and the
function signature come from [cs2-WeaponPaints](https://github.com/Nereziel/cs2-WeaponPaints) by Nereziel.
