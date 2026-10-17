using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher;

/// <summary>Everything the pages share: settings, the CS2 folder, item data, pictures and the activity log.</summary>
public class AppState
{
    public LauncherSettings Settings { get; } = LauncherSettings.Load();
    public HttpClient Http { get; } = new() { Timeout = TimeSpan.FromMinutes(5) };
    public ImageCache Images { get; }
    public ItemDatabase Db { get; private set; } = ItemDatabase.LoadCached(ItemDatabase.DataDir);
    public GameFolder? Game { get; private set; }

    /// <summary>Market prices for the Cases and Inventory pages.</summary>
    public PriceList Prices { get; }

    /// <summary>Folder, install state, test mode or item data changed: pages refresh.</summary>
    public event Action? Changed;
    public event Action<string>? Logged;

    public static string LauncherDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSkinTester");

    public AppState()
    {
        Images = new ImageCache(Http);
        // Steam's community pages (Workshop search) refuse requests that don't say what they are.
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LanSkinTester/2");
        Prices = new PriceList(ItemDatabase.DataDir, Log, NotifyChanged);
        // Hacker mode is never carried over from last time: it is on only when picked in this session.
        Settings.Server.HackerMode = 0;
    }

    public bool Ready => Game is { MetamodInstalled: true, CounterStrikeSharpInstalled: true, PluginInstalled: true };
    public bool TestModeOn => Game != null && new TestSession(Game).IsOn;

    public void Log(string line) => Logged?.Invoke($"[{DateTime.Now:HH:mm:ss}] {line}");

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Saves launcher.json; a failure is only logged (it is saved again with the next change).</summary>
    public void SaveSettings()
    {
        try { Settings.Save(); }
        catch (IOException ex) { Log("Could not save settings: " + ex.Message); }
    }

    /// <summary>Steam's folder from the registry, for finding CS2.</summary>
    public static string? SteamPath() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;

    public void SetGame(string? path)
    {
        Game = GameFolder.IsValid(path) ? new GameFolder(path!) : null;
        if (Game != null)
        {
            Settings.GamePath = Game.Root;
            Settings.Save();
            // Keep the plugin's skin names in sync with the launcher's data (e.g. after picking another folder).
            if (!Db.IsEmpty && Game.PluginInstalled && !File.Exists(Path.Combine(Game.SkinNamesDir, "skins.json")))
                Db.WritePluginSkinNames(Game.SkinNamesDir);
            if (ServerSettings.DiscoverModes(Game)) Log("Found Valve's Rush mode in your CS2 files.");
        }
        NotifyChanged();
    }

    public void ReloadDb()
    {
        Db = ItemDatabase.LoadCached(ItemDatabase.DataDir);
        if (Game != null && Directory.Exists(Game.PluginDir) && !Db.IsEmpty)
            Db.WritePluginSkinNames(Game.SkinNamesDir);
        NotifyChanged();
    }

    /// <summary>The plugin's live report, or null if the server isn't running (or the report is stale).</summary>
    public ServerStatus? ReadServerStatus()
    {
        if (Game == null) return null;
        var path = Path.Combine(Game.PluginDir, ServerStatus.FileName);
        try
        {
            if (!File.Exists(path)) return null;
            var status = System.Text.Json.JsonSerializer.Deserialize<ServerStatus>(File.ReadAllText(path));
            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (status?.Time ?? 0);
            return age is >= -5 and <= 8 ? status : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Turns off CounterStrikeSharp's FollowCS2ServerGuidelines (it blocks skins).</summary>
    public void AllowSkinsInCss()
    {
        if (Game?.CounterStrikeSharpInstalled == true && CssCoreConfig.AllowSkins(Game))
            Log("Set FollowCS2ServerGuidelines to false in CounterStrikeSharp's core.json (otherwise only the knife model changes, not the skin).");
    }
}
