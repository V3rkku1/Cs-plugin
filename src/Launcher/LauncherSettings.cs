using System.Text.Json;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher;

/// <summary>Remembers the CS2 folder and server settings in %AppData%\LanSkinTester\launcher.json.</summary>
public class LauncherSettings
{
    public string? GamePath { get; set; }
    public ServerSettings Server { get; set; } = new();

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
