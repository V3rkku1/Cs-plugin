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

    /// <summary>Text size: "small", "normal" or "large".</summary>
    public string TextSize { get; set; } = "normal";

    /// <summary>Accessibility: solid panels instead of see-through glass, and no animations.</summary>
    public bool ReduceGlass { get; set; }
    public bool ReduceMotion { get; set; }

    /// <summary>
    /// The launcher's look (Setup › Appearance): accent, background and panel colours, panel opacity,
    /// corner roundness, font and a background picture. Missing keys use the default look.
    /// </summary>
    public Dictionary<string, string> Theme { get; set; } = new();

    /// <summary>Quick starts the user saved from the Play page (mode, map, bots, rules).</summary>
    public List<SavedSetup> SavedSetups { get; set; } = new();

    /// <summary>Servers joined lately (newest first), for one-click rejoin on the Friends page.</summary>
    public List<JoinTarget> RecentServers { get; set; } = new();

    /// <summary>Saved friends (Friends page): a name and their server address and password.</summary>
    public List<FriendContact> FriendsList { get; set; } = new();

    /// <summary>The name friends see when this PC hosts (in their launcher's friends list).</summary>
    public string HostName { get; set; } = Environment.UserName;

    /// <summary>
    /// The owner's admin code was entered on this PC: every skin, knife, glove, sticker and agent can be used.
    /// Without it, the Skins page only offers what was unboxed on the Cases page ("unbox only").
    /// </summary>
    public bool AdminUnlocked { get; set; }
    public int CasesOpened { get; set; }

    /// <summary>Prices are shown in this currency (USD, EUR or GBP); what the opened cases and keys cost so far.</summary>
    public string Currency { get; set; } = "USD";
    public double CasesSpent { get; set; }

    /// <summary>The play wallet (Cases / Inventory): opening costs money, selling pays the market price minus Steam's fee.</summary>
    public double Wallet { get; set; } = 100;
    public bool UseWallet { get; set; } = true;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanSkinTester", "launcher.json");

    public static LauncherSettings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath)) ?? new();
            ServerSettings.SkinCommands = settings.AdminUnlocked;
            return settings;
        }
        catch (Exception)
        {
            ServerSettings.SkinCommands = false;
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

/// <summary>A friend on the Friends page.</summary>
public class FriendContact
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Password { get; set; } = "";
}
