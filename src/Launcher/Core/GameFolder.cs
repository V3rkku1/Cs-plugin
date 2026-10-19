using System.Text.RegularExpressions;

namespace LanSkinTester.Launcher.Core;

/// <summary>Paths inside a CS2 install ("...\steamapps\common\Counter-Strike Global Offensive").</summary>
public class GameFolder(string root)
{
    public string Root { get; } = root;
    public string Csgo => Path.Combine(Root, "game", "csgo");
    public string GameInfo => Path.Combine(Csgo, "gameinfo.gi");
    public string Exe => Path.Combine(Root, "game", "bin", "win64", "cs2.exe");
    public string Addons => Path.Combine(Csgo, "addons");
    public string PluginDir => Path.Combine(Addons, "counterstrikesharp", "plugins", "LanSkinTester");
    public string SkinNamesDir => Path.Combine(PluginDir, "skins");
    public string LoadoutFile => Path.Combine(PluginDir, "loadouts.json");

    public bool MetamodInstalled => Directory.Exists(Path.Combine(Addons, "metamod"));
    public bool CounterStrikeSharpInstalled => File.Exists(Path.Combine(Addons, "counterstrikesharp", "bin", "win64", "counterstrikesharp.dll"))
                                               || Directory.Exists(Path.Combine(Addons, "counterstrikesharp", "api"));
    public bool PluginInstalled => File.Exists(Path.Combine(PluginDir, "LanSkinTester.dll"));
    public bool SkinNamesInstalled => Directory.Exists(SkinNamesDir) && Directory.GetFiles(SkinNamesDir, "*.json").Length > 0;

    public static bool IsValid(string? root) => root != null && File.Exists(Path.Combine(root, "game", "csgo", "gameinfo.gi"));

    /// <summary>Looks through every Steam library for CS2.</summary>
    public static string? Find(string? steamPath) =>
        SteamLibraries(steamPath).Select(lib => Path.Combine(lib, "steamapps", "common", "Counter-Strike Global Offensive")).FirstOrDefault(IsValid);

    /// <summary>Every Steam library folder (libraryfolders.vdf), plus the default Steam folder.</summary>
    public static List<string> SteamLibraries(string? steamPath)
    {
        var libraries = new List<string>();
        if (steamPath != null)
        {
            libraries.Add(steamPath);
            var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                libraries.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"").Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
        }
        libraries.Add(@"C:\Program Files (x86)\Steam");
        return libraries;
    }
}
