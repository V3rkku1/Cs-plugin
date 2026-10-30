//! The CS2 install: its paths, gameinfo.gi, CounterStrikeSharp's core.json, Workshop maps and the game's
//! own files (VPK archives, KeyValues text) for finding modes like Rush.

use regex::Regex;
use serde_json::{json, Map, Value};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::LazyLock;

/// Paths inside a CS2 install ("...\steamapps\common\Counter-Strike Global Offensive").
#[derive(Clone, Debug)]
pub struct GameFolder {
    pub root: PathBuf,
}

impl GameFolder {
    pub fn new(root: impl Into<PathBuf>) -> Self {
        Self { root: root.into() }
    }
    pub fn csgo(&self) -> PathBuf {
        self.root.join("game").join("csgo")
    }
    pub fn game_info(&self) -> PathBuf {
        self.csgo().join("gameinfo.gi")
    }
    /// CS2's build ("1.41.9.2", PatchVersion in steam.inf). Each update can break Metamod and the plugins.
    pub fn cs2_build(&self) -> Option<String> {
        patch_version(&fs::read_to_string(self.csgo().join("steam.inf")).ok()?)
    }
    pub fn exe(&self) -> PathBuf {
        self.root
            .join("game")
            .join("bin")
            .join("win64")
            .join("cs2.exe")
    }
    pub fn addons(&self) -> PathBuf {
        self.csgo().join("addons")
    }
    pub fn css(&self) -> PathBuf {
        self.addons().join("counterstrikesharp")
    }
    pub fn plugin_dir(&self) -> PathBuf {
        self.css().join("plugins").join("LanSkinTester")
    }
    pub fn skin_names_dir(&self) -> PathBuf {
        self.plugin_dir().join("skins")
    }
    pub fn loadout_file(&self) -> PathBuf {
        self.plugin_dir().join("loadouts.json")
    }
    pub fn cfg_dir(&self) -> PathBuf {
        self.csgo().join("cfg")
    }

    pub fn metamod_installed(&self) -> bool {
        self.addons().join("metamod").is_dir()
    }
    pub fn css_installed(&self) -> bool {
        self.css()
            .join("bin")
            .join("win64")
            .join("counterstrikesharp.dll")
            .is_file()
            || self.css().join("api").is_dir()
    }
    pub fn plugin_installed(&self) -> bool {
        self.plugin_dir().join("LanSkinTester.dll").is_file()
    }

    pub fn is_valid(root: Option<&str>) -> bool {
        root.is_some_and(|r| {
            !r.is_empty()
                && Path::new(r)
                    .join("game")
                    .join("csgo")
                    .join("gameinfo.gi")
                    .is_file()
        })
    }

    /// Looks through every Steam library for CS2.
    pub fn find(steam: Option<String>) -> Option<String> {
        steam_libraries(steam)
            .into_iter()
            .map(|lib| {
                Path::new(&lib)
                    .join("steamapps")
                    .join("common")
                    .join("Counter-Strike Global Offensive")
                    .display()
                    .to_string()
            })
            .find(|p| Self::is_valid(Some(p)))
    }
}

/// Every Steam library folder (libraryfolders.vdf), plus the default Steam folder.
pub fn steam_libraries(steam: Option<String>) -> Vec<String> {
    static PATH: LazyLock<Regex> = LazyLock::new(|| Regex::new(r#""path"\s+"([^"]+)""#).unwrap());
    let mut libraries = vec![];
    if let Some(steam) = steam {
        if let Ok(vdf) = fs::read_to_string(
            Path::new(&steam)
                .join("steamapps")
                .join("libraryfolders.vdf"),
        ) {
            libraries.push(steam.clone());
            libraries.extend(PATH.captures_iter(&vdf).map(|c| c[1].replace(r"\\", r"\")));
        } else {
            libraries.push(steam);
        }
    }
    libraries.push(r"C:\Program Files (x86)\Steam".into());
    libraries
}

/// Steam's folder from the registry, for finding CS2.
pub fn steam_path() -> Option<String> {
    #[cfg(windows)]
    {
        let key = winreg::RegKey::predef(winreg::enums::HKEY_CURRENT_USER)
            .open_subkey(r"Software\Valve\Steam")
            .ok()?;
        key.get_value::<String, _>("SteamPath").ok()
    }
    #[cfg(not(windows))]
    None
}

// -------------------------------------------------------------------------------------------------
// gameinfo.gi: the Metamod search path. While it's there, CS2 must run with -insecure.
// -------------------------------------------------------------------------------------------------

const METAMOD_LINE: &str = "Game\tcsgo/addons/metamod";
static IS_METAMOD: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"^\s*Game\s+csgo/addons/metamod\s*$").unwrap());
static IS_CSGO: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"^(\s*)Game\s+csgo\s*$").unwrap());

fn lines(path: &Path) -> std::io::Result<Vec<String>> {
    Ok(fs::read_to_string(path)?
        .lines()
        .map(str::to_string)
        .collect())
}

fn write_lines(path: &Path, lines: &[String]) -> std::io::Result<()> {
    fs::write(path, lines.join("\r\n") + "\r\n")
}

pub fn metamod_enabled(game_info: &Path) -> bool {
    lines(game_info).is_ok_and(|l| l.iter().any(|l| IS_METAMOD.is_match(l)))
}

pub fn enable_metamod(game_info: &Path) -> Result<(), String> {
    let mut l = lines(game_info).map_err(|e| e.to_string())?;
    if l.iter().any(|l| IS_METAMOD.is_match(l)) {
        return Ok(());
    }
    let index = l.iter().position(|l| IS_CSGO.is_match(l)).ok_or(
        "gameinfo.gi has an unexpected format (no \"Game csgo\" line). Nothing was changed.",
    )?;
    fs::copy(game_info, format!("{}.backup", game_info.display())).map_err(|e| e.to_string())?;
    let indent = IS_CSGO.captures(&l[index]).unwrap()[1].to_string();
    l.insert(index, indent + METAMOD_LINE);
    write_lines(game_info, &l).map_err(|e| e.to_string())
}

pub fn disable_metamod(game_info: &Path) -> std::io::Result<()> {
    let l = lines(game_info)?;
    let kept: Vec<String> = l
        .iter()
        .filter(|l| !IS_METAMOD.is_match(l))
        .cloned()
        .collect();
    if kept.len() != l.len() {
        write_lines(game_info, &kept)?;
    }
    Ok(())
}

// -------------------------------------------------------------------------------------------------
// CounterStrikeSharp's FollowCS2ServerGuidelines (on by default) blocks the item fields skins need.
// -------------------------------------------------------------------------------------------------

const GUIDELINES: &str = "FollowCS2ServerGuidelines";

fn core_json(game: &GameFolder) -> PathBuf {
    game.css().join("configs").join("core.json")
}

/// core.json allows // comments and trailing commas.
fn parse_loose_json(text: &str) -> Option<Value> {
    static COMMENT: LazyLock<Regex> =
        LazyLock::new(|| Regex::new(r#"(?m)("(?:[^"\\]|\\.)*")|//[^\n]*|/\*(?s:.*?)\*/"#).unwrap());
    static TRAILING: LazyLock<Regex> = LazyLock::new(|| Regex::new(r",(\s*[}\]])").unwrap());
    let clean = COMMENT.replace_all(text, |c: &regex::Captures| {
        c.get(1).map_or(String::new(), |s| s.as_str().to_string())
    });
    serde_json::from_str(&TRAILING.replace_all(&clean, "$1")).ok()
}

/// True when skins are blocked (also when core.json doesn't exist yet: the default is on).
pub fn css_blocks_skins(game: &GameFolder) -> bool {
    let Ok(text) = fs::read_to_string(core_json(game)) else {
        return true;
    };
    parse_loose_json(&text)
        .and_then(|v| v.get(GUIDELINES).and_then(Value::as_bool))
        .unwrap_or(true)
}

/// Turns the option off, keeping every other setting. Returns true if the file changed.
pub fn css_allow_skins(game: &GameFolder) -> std::io::Result<bool> {
    if !css_blocks_skins(game) {
        return Ok(false);
    }
    let path = core_json(game);
    let mut config = match fs::read_to_string(&path) {
        Ok(text) => match parse_loose_json(&text) {
            Some(Value::Object(o)) => o,
            _ => {
                fs::copy(&path, format!("{}.broken", path.display()))?;
                Map::new()
            }
        },
        Err(_) => Map::new(),
    };
    config.insert(GUIDELINES.into(), json!(false));
    fs::create_dir_all(path.parent().unwrap())?;
    fs::write(&path, serde_json::to_string_pretty(&config).unwrap())?;
    Ok(true)
}

// -------------------------------------------------------------------------------------------------
// Workshop maps Steam has downloaded, and clean-up of map copies older launchers put in csgo\maps.
// -------------------------------------------------------------------------------------------------

const COPIES_FILE: &str = ".lanskintester_workshop_copies";

fn workshop_root(game: &GameFolder) -> PathBuf {
    game.root
        .join("..")
        .join("..")
        .join("workshop")
        .join("content")
        .join("730")
}

pub fn workshop_dir(game: &GameFolder, id: &str) -> PathBuf {
    workshop_root(game).join(id)
}

pub fn subscribe_url(id: &str) -> String {
    format!("steam://url/CommunityFilePage/{id}")
}

fn vpks(dir: &Path, out: &mut Vec<PathBuf>) {
    let Ok(entries) = fs::read_dir(dir) else {
        return;
    };
    for e in entries.flatten() {
        let p = e.path();
        if p.is_dir() {
            vpks(&p, out);
        } else if p.extension().is_some_and(|x| x.eq_ignore_ascii_case("vpk")) {
            out.push(p);
        }
    }
}

/// Steam has downloaded the map: any .vpk in its folder.
pub fn workshop_downloaded(game: &GameFolder, id: &str) -> bool {
    find_workshop_vpk(game, id).is_some()
}

/// The map's .vpk (the biggest: some items also ship small extra ones), if Steam has downloaded it.
pub fn find_workshop_vpk(game: &GameFolder, id: &str) -> Option<PathBuf> {
    let mut all = vec![];
    vpks(&workshop_dir(game, id), &mut all);
    all.into_iter()
        .max_by_key(|p| fs::metadata(p).map(|m| m.len()).unwrap_or(0))
}

/// The models (.vmdl paths) in an addon Steam has downloaded, or None when it isn't downloaded.
pub fn addon_models(game: &GameFolder, id: &str) -> Option<Vec<String>> {
    let mut all = vec![];
    vpks(&workshop_dir(game, id), &mut all);
    // A split addon lists every file in its _dir.vpk; a single-file one in itself.
    let vpk = all.iter().find(|p| p.to_string_lossy().ends_with("_dir.vpk")).or(all.first())?;
    let mut models: Vec<String> = vpk_files(vpk)
        .into_iter()
        .filter(|(_, _, ext)| ext == "vmdl_c")
        .map(|(folder, name, _)| if folder.is_empty() { format!("{name}.vmdl") } else { format!("{folder}/{name}.vmdl") })
        .collect();
    models.sort();
    Some(models)
}

pub struct Downloaded {
    pub id: String,
    pub bytes: u64,
    pub updated: chrono::DateTime<chrono::Utc>,
}

/// Every Workshop item Steam has downloaded for CS2 that contains a map (.vpk).
pub fn all_downloaded(game: &GameFolder) -> Vec<Downloaded> {
    let Ok(entries) = fs::read_dir(workshop_root(game)) else {
        return vec![];
    };
    entries
        .flatten()
        .filter_map(|e| {
            let id = e.file_name().to_string_lossy().to_string();
            if !id.chars().all(|c| c.is_ascii_digit()) {
                return None;
            }
            let vpk = find_workshop_vpk(game, &id)?;
            let meta = fs::metadata(vpk).ok()?;
            Some(Downloaded {
                id,
                bytes: meta.len(),
                updated: meta.modified().ok()?.into(),
            })
        })
        .collect()
}

/// A map whose own script changes maps with map_workshop (Movement Hub and other single-player hubs): that
/// only works in an offline game. Returns the map to start offline (the first in its addoninfo.txt).
pub fn offline_only_map(game: &GameFolder, id: &str) -> Option<String> {
    static MAPS: LazyLock<Regex> =
        LazyLock::new(|| Regex::new(r#"maps\s*=\s*\[\s*"([^"]+)""#).unwrap());
    let dir = workshop_dir(game, id);
    let vpk = fs::read_dir(&dir)
        .ok()?
        .flatten()
        .map(|e| e.path())
        .find(|p| p.to_string_lossy().ends_with("_dir.vpk"))
        .or_else(|| find_workshop_vpk(game, id))?;
    let text = |folder: &str, name: &str, ext: &str| {
        vpk_read(&vpk, folder, name, ext)
            .map(|b| String::from_utf8_lossy(&b).into_owned())
            .unwrap_or_default()
    };
    let scripted = vpk_files(&vpk).iter().any(|(folder, name, ext)| {
        folder == "scripts" && ext == "vjs_c" && text(folder, name, ext).contains("map_workshop")
    });
    if !scripted {
        return None;
    }
    MAPS.captures(&text("", "addoninfo", "txt"))
        .map(|c| c[1].to_string())
}

/// Deletes the map copies older launchers made (after CS2 has closed).
pub fn remove_workshop_copies(game: &GameFolder) {
    let maps = game.csgo().join("maps");
    let list = maps.join(COPIES_FILE);
    let Ok(text) = fs::read_to_string(&list) else {
        return;
    };
    let left: Vec<&str> = text
        .lines()
        .filter(|l| !l.trim().is_empty())
        .filter(|file| {
            let path = maps.join(Path::new(file).file_name().unwrap_or_default()); // only inside csgo\maps
            path.exists() && fs::remove_file(path).is_err()
        })
        .collect();
    let _ = if left.is_empty() {
        fs::remove_file(&list)
    } else {
        fs::write(&list, left.join("\n"))
    };
}

// -------------------------------------------------------------------------------------------------
// VPK (version 1 or 2) directory archives
// -------------------------------------------------------------------------------------------------

struct Reader<'a> {
    data: &'a [u8],
    pos: usize,
}

impl Reader<'_> {
    fn take(&mut self, n: usize) -> Option<&[u8]> {
        let s = self.data.get(self.pos..self.pos + n)?;
        self.pos += n;
        Some(s)
    }
    fn u16(&mut self) -> Option<u16> {
        Some(u16::from_le_bytes(self.take(2)?.try_into().ok()?))
    }
    fn u32(&mut self) -> Option<u32> {
        Some(u32::from_le_bytes(self.take(4)?.try_into().ok()?))
    }
    fn string(&mut self) -> Option<String> {
        let end = self.data[self.pos..].iter().position(|&b| b == 0)?;
        let s = String::from_utf8_lossy(&self.data[self.pos..self.pos + end]).into_owned();
        self.pos += end + 1;
        Some(s)
    }
}

struct VpkEntry {
    folder: String,
    name: String,
    ext: String,
    preload: Vec<u8>,
    archive: u16,
    offset: u32,
    length: u32,
}

/// The directory tree: extension, then folder, then file name; each level ends with an empty string. Only the
/// header and the tree are read (a Workshop map's single-file VPK can be hundreds of MB).
fn vpk_entries(dir_file: &Path) -> Option<(Vec<VpkEntry>, usize)> {
    use std::io::Read;
    let mut file = fs::File::open(dir_file).ok()?;
    let mut header = [0u8; 28];
    file.read_exact(&mut header[..12]).ok()?;
    let mut r = Reader {
        data: &header,
        pos: 0,
    };
    if r.u32()? != 0x55AA1234 {
        return None;
    }
    let version = r.u32()?;
    let tree = r.u32()? as usize;
    let header_size = match version {
        1 => 12,
        2 => 28,
        _ => return None,
    };
    if version == 2 {
        file.read_exact(&mut header[12..28]).ok()?;
    }
    let mut data = vec![0u8; tree];
    file.read_exact(&mut data).ok()?;
    let mut r = Reader {
        data: &data,
        pos: 0,
    };
    let mut out = vec![];
    loop {
        let ext = r.string()?;
        if ext.is_empty() {
            break;
        }
        loop {
            let folder = r.string()?;
            if folder.is_empty() {
                break;
            }
            loop {
                let name = r.string()?;
                if name.is_empty() {
                    break;
                }
                r.u32()?; // CRC
                let preload = r.u16()? as usize;
                let archive = r.u16()?;
                let offset = r.u32()?;
                let length = r.u32()?;
                r.u16()?; // terminator
                let preload = r.take(preload)?.to_vec();
                let folder = if folder == " " {
                    String::new()
                } else {
                    folder.clone()
                };
                out.push(VpkEntry {
                    folder,
                    name,
                    ext: ext.clone(),
                    preload,
                    archive,
                    offset,
                    length,
                });
            }
        }
    }
    Some((out, header_size + tree))
}

/// Every file in the archive as (folder, name, extension); the root folder is "".
pub fn vpk_files(dir_file: &Path) -> Vec<(String, String, String)> {
    vpk_entries(dir_file)
        .map(|(e, _)| e.into_iter().map(|e| (e.folder, e.name, e.ext)).collect())
        .unwrap_or_default()
}

/// One file out of the archive; `folder` "" is the root.
pub fn vpk_read(dir_file: &Path, folder: &str, name: &str, ext: &str) -> Option<Vec<u8>> {
    let (entries, data_start) = vpk_entries(dir_file)?;
    let folder = folder.replace('\\', "/");
    let e = entries.into_iter().find(|e| {
        e.ext.eq_ignore_ascii_case(ext)
            && e.folder.eq_ignore_ascii_case(&folder)
            && e.name.eq_ignore_ascii_case(name)
    })?;
    if e.length == 0 {
        return Some(e.preload);
    }
    let (file, start) = if e.archive == 0x7FFF {
        (dir_file.to_path_buf(), data_start + e.offset as usize)
    } else {
        (
            PathBuf::from(
                dir_file
                    .to_string_lossy()
                    .replace("_dir.vpk", &format!("_{:03}.vpk", e.archive)),
            ),
            e.offset as usize,
        )
    };
    use std::io::{Read, Seek, SeekFrom};
    let mut f = fs::File::open(file).ok()?;
    f.seek(SeekFrom::Start(start as u64)).ok()?;
    let mut out = e.preload;
    let at = out.len();
    out.resize(at + e.length as usize, 0);
    f.read_exact(&mut out[at..]).ok()?;
    Some(out)
}

// -------------------------------------------------------------------------------------------------
// Valve KeyValues text ("key" "value" and "key" { ... }), and the game's mode list
// -------------------------------------------------------------------------------------------------

#[derive(Debug, Default)]
pub struct KeyValues {
    pub key: String,
    pub value: Option<String>,
    pub children: Vec<KeyValues>,
}

impl KeyValues {
    pub fn child(&self, key: &str) -> Option<&KeyValues> {
        self.children
            .iter()
            .find(|c| c.key.eq_ignore_ascii_case(key))
    }

    pub fn parse(text: &str) -> KeyValues {
        let mut stack = vec![KeyValues::default()];
        let mut pending: Option<String> = None;
        for token in tokenize(text) {
            match token.as_str() {
                "{" => stack.push(KeyValues {
                    key: pending.take().unwrap_or_default(),
                    ..Default::default()
                }),
                "}" => {
                    if stack.len() > 1 {
                        let node = stack.pop().unwrap();
                        stack.last_mut().unwrap().children.push(node);
                    }
                    pending = None;
                }
                _ => match pending.take() {
                    None => pending = Some(token),
                    Some(key) => stack.last_mut().unwrap().children.push(KeyValues {
                        key,
                        value: Some(token),
                        children: vec![],
                    }),
                },
            }
        }
        while stack.len() > 1 {
            let node = stack.pop().unwrap();
            stack.last_mut().unwrap().children.push(node);
        }
        let mut root = stack.pop().unwrap();
        // The file's own top-level block ("GameModes.txt" { ... }) holds everything.
        if root.children.len() == 1 && root.children[0].value.is_none() {
            root.children.pop().unwrap()
        } else {
            root
        }
    }
}

fn tokenize(text: &str) -> Vec<String> {
    let c: Vec<char> = text.chars().collect();
    let mut out = vec![];
    let mut i = 0;
    while i < c.len() {
        match c[i] {
            ch if ch.is_whitespace() => i += 1,
            '/' if c.get(i + 1) == Some(&'/') => {
                while i < c.len() && c[i] != '\n' {
                    i += 1;
                }
            }
            '{' | '}' => {
                out.push(c[i].to_string());
                i += 1;
            }
            // Platform conditionals like [$WIN32].
            '[' => {
                while i < c.len() && c[i] != ']' {
                    i += 1;
                }
                i += 1;
            }
            '"' => {
                let mut s = String::new();
                i += 1;
                while i < c.len() && c[i] != '"' {
                    if c[i] == '\\' && i + 1 < c.len() {
                        i += 1;
                    }
                    s.push(c[i]);
                    i += 1;
                }
                i += 1;
                out.push(s);
            }
            _ => {
                let mut s = String::new();
                while i < c.len() && !c[i].is_whitespace() && !matches!(c[i], '{' | '}' | '"') {
                    s.push(c[i]);
                    i += 1;
                }
                out.push(s);
            }
        }
    }
    out
}

#[derive(Clone, Debug, PartialEq)]
pub struct GameMode {
    pub name: String,
    pub game_type: i32,
    pub mode: i32,
    pub maps: Vec<String>,
}

/// Finds a mode in CS2's gamemodes.txt (loose or inside pak01_dir.vpk) whose name contains `name`.
pub fn find_game_mode(game: &GameFolder, name: &str, display: &str) -> Option<GameMode> {
    let text = read_game_modes(game)?;
    let root = KeyValues::parse(&text);
    for t in &root.child("gameTypes")?.children {
        let Some(type_value) = t
            .child("value")
            .and_then(|v| v.value.as_deref()?.parse().ok())
        else {
            continue;
        };
        for mode in t
            .child("gameModes")
            .map(|m| &m.children[..])
            .unwrap_or_default()
        {
            if !mode.key.to_lowercase().contains(&name.to_lowercase()) {
                continue;
            }
            let Some(mode_value) = mode
                .child("value")
                .and_then(|v| v.value.as_deref()?.parse().ok())
            else {
                continue;
            };
            let maps = maps_of(&root, mode, game);
            if !maps.is_empty() {
                return Some(GameMode {
                    name: display.into(),
                    game_type: type_value,
                    mode: mode_value,
                    maps,
                });
            }
        }
    }
    None
}

fn maps_of(root: &KeyValues, mode: &KeyValues, game: &GameFolder) -> Vec<String> {
    let mut maps: Vec<String> = vec![];
    for key in ["maplist", "maps"] {
        if let Some(list) = mode.child(key) {
            maps.extend(list.children.iter().map(|c| c.key.clone()));
        }
    }
    if let Some(groups) = root.child("mapgroups") {
        for key in ["mapgroupsMP", "mapgroupsSP", "mapgroups"] {
            for group in mode.child(key).map(|g| &g.children[..]).unwrap_or_default() {
                if let Some(group_maps) = groups.child(&group.key).and_then(|g| g.child("maps")) {
                    maps.extend(group_maps.children.iter().map(|c| c.key.clone()));
                }
            }
        }
    }
    let mut all: Vec<String> = vec![];
    for m in maps {
        if !all.iter().any(|x| x.eq_ignore_ascii_case(&m)) {
            all.push(m);
        }
    }
    // Prefer the maps that are installed as maps\<name>.vpk (all of them if none are found there).
    let installed: Vec<String> = all
        .iter()
        .filter(|m| game.csgo().join("maps").join(format!("{m}.vpk")).exists())
        .cloned()
        .collect();
    if installed.is_empty() {
        all
    } else {
        installed
    }
}

fn read_game_modes(game: &GameFolder) -> Option<String> {
    for loose in ["gamemodes.txt", "gamemodes_server.txt"] {
        if let Ok(text) = fs::read_to_string(game.csgo().join(loose)) {
            if text.to_lowercase().contains("gametypes") {
                return Some(text);
            }
        }
    }
    vpk_read(&game.csgo().join("pak01_dir.vpk"), "", "gamemodes", "txt")
        .map(|b| String::from_utf8_lossy(&b).into_owned())
}

fn patch_version(steam_inf: &str) -> Option<String> {
    steam_inf
        .lines()
        .find_map(|l| l.trim().strip_prefix("PatchVersion="))
        .map(|v| v.trim().to_string())
        .filter(|v| !v.is_empty())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_cs2_build() {
        let inf = "ClientVersion=2000123\r\nPatchVersion=1.41.9.2\r\nProductName=cs2\r\n";
        assert_eq!(patch_version(inf).as_deref(), Some("1.41.9.2"));
        assert_eq!(patch_version("ProductName=cs2"), None);
    }

    const GAME_INFO: &str = "\"GameInfo\"\n{\n\tFileSystem\n\t{\n\t\tSearchPaths\n\t\t{\n\t\t\tGame_LowViolence\tcsgo_lv\n\t\t\tGame\tcsgo\n\t\t\tGame\tcsgo_imported\n\t\t\tMod\tcsgo\n\t\t}\n\t}\n}\n";

    pub fn temp_game() -> GameFolder {
        let root = std::env::temp_dir().join(format!("lst-game-{}", crate::util::random_hex(6)));
        let game = GameFolder::new(root);
        fs::create_dir_all(game.csgo()).unwrap();
        fs::write(game.game_info(), GAME_INFO).unwrap();
        game
    }

    #[test]
    fn metamod_line_goes_in_above_csgo_and_comes_out_again() {
        let game = temp_game();
        enable_metamod(&game.game_info()).unwrap();
        let l = lines(&game.game_info()).unwrap();
        let metamod = l
            .iter()
            .position(|l| l.trim() == "Game\tcsgo/addons/metamod")
            .unwrap();
        let csgo = l.iter().position(|l| l.trim() == "Game\tcsgo").unwrap();
        assert_eq!(
            metamod + 1,
            csgo,
            "Metamod must be the search path right above csgo"
        );
        assert!(Path::new(&format!("{}.backup", game.game_info().display())).exists());
        enable_metamod(&game.game_info()).unwrap(); // twice: still one line
        assert_eq!(
            lines(&game.game_info())
                .unwrap()
                .iter()
                .filter(|l| l.contains("addons/metamod"))
                .count(),
            1
        );
        disable_metamod(&game.game_info()).unwrap();
        assert_eq!(
            fs::read_to_string(game.game_info())
                .unwrap()
                .replace('\r', "")
                .trim_end(),
            GAME_INFO.trim_end()
        );
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn unexpected_game_info_is_left_alone() {
        let game = temp_game();
        fs::write(game.game_info(), "\"GameInfo\" { }").unwrap();
        assert!(enable_metamod(&game.game_info()).is_err());
        assert_eq!(
            fs::read_to_string(game.game_info()).unwrap(),
            "\"GameInfo\" { }"
        );
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn css_config_keeps_other_settings() {
        let game = temp_game();
        fs::create_dir_all(game.css().join("configs")).unwrap();
        fs::write(core_json(&game), "{\n // comment\n \"PluginHotReloadEnabled\": true,\n \"FollowCS2ServerGuidelines\": true,\n}").unwrap();
        assert!(css_blocks_skins(&game));
        assert!(css_allow_skins(&game).unwrap());
        assert!(!css_blocks_skins(&game));
        assert!(fs::read_to_string(core_json(&game))
            .unwrap()
            .contains("PluginHotReloadEnabled"));
        fs::remove_dir_all(&game.root).unwrap();
    }

    #[test]
    fn vpk_files_inside_the_archive() {
        let mut tree = vec![];
        let mut entry = |ext: &str, folder: &str, name: &str, offset: u32, len: u32| {
            for s in [ext, folder, name] {
                tree.extend(s.as_bytes());
                tree.push(0);
            }
            tree.extend(0u32.to_le_bytes()); // CRC
            tree.extend(0u16.to_le_bytes()); // preload
            tree.extend(0x7FFFu16.to_le_bytes()); // data in this file
            tree.extend(offset.to_le_bytes());
            tree.extend(len.to_le_bytes());
            tree.extend(0xFFFFu16.to_le_bytes());
            tree.extend([0, 0]); // end of files, of folders
        };
        entry("txt", " ", "gamemodes", 0, 5);
        entry("vjs_c", "scripts", "hub", 5, 3);
        tree.push(0); // end of extensions
        let mut vpk = 0x55AA1234u32.to_le_bytes().to_vec();
        vpk.extend(1u32.to_le_bytes());
        vpk.extend((tree.len() as u32).to_le_bytes());
        vpk.extend(&tree);
        vpk.extend(b"hellomap");
        let file = std::env::temp_dir().join(format!("lst-{}_dir.vpk", crate::util::random_hex(6)));
        fs::write(&file, vpk).unwrap();
        assert_eq!(
            vpk_files(&file),
            [
                ("".into(), "gamemodes".into(), "txt".into()),
                ("scripts".into(), "hub".into(), "vjs_c".into())
            ]
        );
        assert_eq!(vpk_read(&file, "", "gamemodes", "txt").unwrap(), b"hello");
        assert_eq!(vpk_read(&file, "scripts", "hub", "vjs_c").unwrap(), b"map");
        fs::remove_file(file).unwrap();
    }

    #[test]
    fn keyvalues_find_rush() {
        let game = temp_game();
        fs::write(game.csgo().join("gamemodes.txt"), r#""GameModes.txt" { "gameTypes" { "classic" { "value" "0" "gameModes" { "rush" { "value" "6" "maplist" { "de_dust2" "" } } } } } }"#).unwrap();
        let mode = find_game_mode(&game, "rush", "Rush").unwrap();
        assert_eq!(
            (mode.game_type, mode.mode, mode.maps),
            (0, 6, vec!["de_dust2".to_string()])
        );
        fs::remove_dir_all(&game.root).unwrap();
    }
}
