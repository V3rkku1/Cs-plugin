using System.Text.Json;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher;

/// <summary>Remembers the CS2 folder and server settings in %AppData%\LanSkinTester\launcher.json.</summary>
public class LauncherSettings
{
    public string? GamePath { get; set; }
    public ServerSettings Server { get; set; } = new();

    /// <summary>Window position/size and the page that was open, restored on the next start.</summary>
    public int[]? Window { get; set; } // x, y, width, height
    public bool Maximized { get; set; }
    public string? LastPage { get; set; }

    /// <summary>Text look (applied at start): font "inter" or "segoe", "sharp" (ClearType) or "smooth", size "normal" or "large".</summary>
    public string TextFont { get; set; } = "inter";
    public string TextRendering { get; set; } = "sharp";
    public string TextSize { get; set; } = "normal";

    /// <summary>The settings tab last open on the Play page.</summary>
    public int PlayTab { get; set; }

    /// <summary>Quick starts the user saved from the Play page (mode, map, bots, rules).</summary>
    public List<SavedSetup> SavedSetups { get; set; } = new();

    /// <summary>Servers joined lately (newest first), for one-click rejoin on the Friends page.</summary>
    public List<JoinTarget> RecentServers { get; set; } = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanSkinTester", "launcher.json");

    public static LauncherSettings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception)
        {
            return new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

/// <summary>A named snapshot of the Play page, shown as an extra quick start.</summary>
public class SavedSetup
{
    public string Name { get; set; } = "";
    public ServerSettings Settings { get; set; } = new();
}
