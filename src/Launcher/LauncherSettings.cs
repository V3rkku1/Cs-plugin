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
