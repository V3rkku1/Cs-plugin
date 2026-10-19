using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// A CS:GO legacy install (Source 1): the standalone "csgo legacy" app, or the CS2 folder switched to the
/// csgo_legacy beta. Skins come from SourceMod plugins (kgns' weapons and gloves), not from LanSkinTester.dll.
/// </summary>
public class LegacyGame(string root)
{
    public string Root { get; } = root;
    public string Csgo => Path.Combine(Root, "csgo");
    public string Exe => Path.Combine(Root, "csgo.exe");
    public string Addons => Path.Combine(Csgo, "addons");
    public string SourceMod => Path.Combine(Addons, "sourcemod");
    public string CoreCfg => Path.Combine(SourceMod, "configs", "core.cfg");

    public bool MetamodInstalled => File.Exists(Path.Combine(Addons, "metamod.vdf"));
    public bool SourceModInstalled => File.Exists(Path.Combine(SourceMod, "bin", "sourcemod_mm.dll"));
    public bool PtahInstalled => Directory.Exists(Path.Combine(SourceMod, "extensions"))
                                 && Directory.GetFiles(Path.Combine(SourceMod, "extensions"), "PTaH*").Length > 0;
    public bool PluginInstalled(string name) => File.Exists(Path.Combine(SourceMod, "plugins", name + ".smx"));

    public static bool IsValid(string? root) =>
        root != null && File.Exists(Path.Combine(root, "csgo.exe")) && File.Exists(Path.Combine(root, "csgo", "gameinfo.txt"));

    /// <summary>The standalone CS:GO app installs to "csgo legacy"; the csgo_legacy beta keeps CS2's folder.</summary>
    public static string? Find(string? steamPath) =>
        GameFolder.SteamLibraries(steamPath)
            .SelectMany(lib => new[] { "csgo legacy", "Counter-Strike Global Offensive" }.Select(name => Path.Combine(lib, "steamapps", "common", name)))
            .FirstOrDefault(IsValid);

    // SourceMod's "FollowCSGOServerGuidelines" (on by default) blocks the item fields skin plugins write.
    private static readonly Regex Guidelines = new("(\"FollowCSGOServerGuidelines\"\\s+)\"yes\"", RegexOptions.IgnoreCase);

    public bool BlocksSkins
    {
        get
        {
            try { return !File.Exists(CoreCfg) || Guidelines.IsMatch(File.ReadAllText(CoreCfg)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; } // locked: ask again next time
        }
    }

    public static string AllowSkins(string coreCfg) => Guidelines.Replace(coreCfg, "$1\"no\"");

    public void AllowSkins()
    {
        if (File.Exists(CoreCfg)) File.WriteAllText(CoreCfg, AllowSkins(File.ReadAllText(CoreCfg)));
    }

    /// <summary>Name, game_type and game_mode of the modes the launcher offers.</summary>
    public static readonly (string Id, string Name, int Type, int Mode)[] Modes =
    [
        ("casual", "Casual", 0, 0), ("competitive", "Competitive", 0, 1), ("wingman", "Wingman", 0, 2),
        ("armsrace", "Arms Race", 1, 0), ("demolition", "Demolition", 1, 1), ("deathmatch", "Deathmatch", 1, 2),
    ];

    /// <summary>
    /// A listen server: the game hosts the LAN server itself and loads Metamod/SourceMod into its process,
    /// so -insecure is required (VAC must stay off). Friends join with connect &lt;your IP&gt;.
    /// </summary>
    public static string LaunchArguments(string mode, string map, int bots)
    {
        var m = Modes.FirstOrDefault(x => x.Id == mode);
        if (m.Id == null) throw new ArgumentException("Unknown mode: " + mode);
        if (!Regex.IsMatch(map, "^[A-Za-z0-9_]+$")) throw new ArgumentException("Map names are letters, digits and _ (like de_dust2).");
        return $"-insecure -novid -console +sv_lan 1 +game_type {m.Type} +game_mode {m.Mode} +bot_quota {Math.Clamp(bots, 0, 20)} +map {map}";
    }

    public static bool Running() => Process.GetProcessesByName("csgo").Length > 0;

    public void Start(string mode, string map, int bots) =>
        Process.Start(new ProcessStartInfo(Exe) { Arguments = LaunchArguments(mode, map, bots), UseShellExecute = true, WorkingDirectory = Root });
}
