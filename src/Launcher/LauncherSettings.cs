using System.Text.Json;

namespace LanSkinTester.Launcher;

/// <summary>Remembers the CS2 folder and last map in %AppData%\LanSkinTester\launcher.json.</summary>
public class LauncherSettings
{
    public string? GamePath { get; set; }
    public string Map { get; set; } = "de_dust2";

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
