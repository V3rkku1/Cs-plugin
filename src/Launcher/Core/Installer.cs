using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LanSkinTester.Launcher.Core;

/// <summary>Downloads and installs Metamod, CounterStrikeSharp, the plugin and the skin names.</summary>
public class Installer
{
    public const string MetamodPage = "https://www.sourcemm.net/downloads.php?branch=dev";
    public const string CounterStrikeSharpPage = "https://github.com/roflmuffin/CounterStrikeSharp/releases/latest";

    private const string MetamodLatest = "https://mms.alliedmods.net/mmsdrop/2.0/mmsource-latest-windows";
    private const string MetamodDrop = "https://mms.alliedmods.net/mmsdrop/2.0/";
    private const string CounterStrikeSharpApi = "https://api.github.com/repos/roflmuffin/CounterStrikeSharp/releases/latest";

    private readonly GameFolder _game;
    private readonly HttpClient _http;
    private readonly Action<string> _log;

    /// <summary>Download progress of the current step, 0..1 (or -1 when the size is unknown).</summary>
    public Action<double>? Progress { get; set; }

    public Installer(GameFolder game, HttpClient http, Action<string> log)
    {
        _game = game;
        _http = http;
        _log = log;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LanSkinTester-Launcher", "1.0"));
    }

    public async Task InstallMetamodAsync()
    {
        _log("Looking up the latest Metamod 2.x build...");
        var file = (await _http.GetStringAsync(MetamodLatest)).Trim();
        _log($"Downloading {file}...");
        await InstallZipAsync(await DownloadAsync(MetamodDrop + file));
        _log("Metamod installed.");
    }

    public async Task InstallCounterStrikeSharpAsync()
    {
        _log("Looking up the latest CounterStrikeSharp release...");
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(CounterStrikeSharpApi));
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .FirstOrDefault(a => a.Name.Contains("with-runtime") && a.Name.Contains("windows") && a.Name.EndsWith(".zip"));
        if (asset.Url == "")
            throw new InvalidOperationException("No Windows with-runtime build found in the latest CounterStrikeSharp release.");

        _log($"Downloading {asset.Name}...");
        await InstallZipAsync(await DownloadAsync(asset.Url));
        _log("CounterStrikeSharp installed.");
    }

    // ---------------------------------------------------------------------------------------------
    // Plugin retakes and the plugins that ride with it. Each one is parked outside the plugins
    // folder when it should not load, so it never fights Valve's own retakes mode.
    // Instadefuse: B3none, GPL-3.0. Allocator: the maintained fork of Yoni Lerner's allocator.
    // ---------------------------------------------------------------------------------------------

    public const string RetakesPage = "https://github.com/B3none/cs2-retakes";
    public const string InstadefusePage = "https://github.com/B3none/cs2-instadefuse";
    public const string AllocatorPage = "https://github.com/Micka2302/cs2-retakes-allocator-2.0";

    private const string RetakesApi = "https://api.github.com/repos/B3none/cs2-retakes/releases/latest";
    private const string InstadefuseApi = "https://api.github.com/repos/B3none/cs2-instadefuse/releases/latest";
    private const string AllocatorApi = "https://api.github.com/repos/Micka2302/cs2-retakes-allocator-2.0/releases/latest";

    private static readonly string[] AllocatorTrees =
    [
        "plugins/RetakesAllocator",
        "plugins/SharpModMenu",
        "shared/SharpModMenu",
        "shared/CSSUniversalMenuAPI",
    ];

    private static string CssRoot(GameFolder game) => Path.Combine(game.Addons, "counterstrikesharp");

    public static bool ThirdPartyRetakesLoaded(GameFolder game) => TreeLoaded(game, "plugins/RetakesPlugin");

    public static bool RetakesInstalled(GameFolder game) => TreeInstalled(game, "plugins/RetakesPlugin", "RetakesPlugin.dll");

    public static bool InstadefuseInstalled(GameFolder game) => TreeInstalled(game, "plugins/InstadefusePlugin", "InstadefusePlugin.dll");

    public static bool AllocatorInstalled(GameFolder game) => TreeInstalled(game, "plugins/RetakesAllocator", "RetakesAllocator.dll");

    /// <summary>True when the plugins on disk are not the set this server wants loaded.</summary>
    public static bool RetakePluginsMismatch(GameFolder game, ServerSettings server)
    {
        var plugin = server.IsPluginRetakes;
        return TreeLoaded(game, "plugins/RetakesPlugin") != plugin
            || TreeLoaded(game, "plugins/InstadefusePlugin") != (plugin && server.RetakeInstadefuse)
            || TreeLoaded(game, "plugins/RetakesAllocator") != server.UsesAllocator;
    }

    public async Task InstallRetakesAsync() =>
        await InstallReleaseAsync(RetakesApi, "cs2-retakes",
            name => name.StartsWith("RetakesPlugin", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip")
                    && !name.Contains("no-map-configs", StringComparison.OrdinalIgnoreCase));

    public async Task InstallInstadefuseAsync() =>
        await InstallReleaseAsync(InstadefuseApi, "cs2-instadefuse", name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    public async Task InstallAllocatorAsync() =>
        await InstallReleaseAsync(AllocatorApi, "retakes allocator",
            name => name.Contains("Allocator", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads or parks retakes, instadefuse and the allocator to match the current settings.</summary>
    public static void SetRetakesSuite(GameFolder game, ServerSettings server)
    {
        var plugin = server.IsPluginRetakes;
        SetTreeActive(game, "plugins/RetakesPlugin", plugin);
        SetTreeActive(game, "plugins/InstadefusePlugin", plugin && server.RetakeInstadefuse);
        foreach (var tree in AllocatorTrees)
            SetTreeActive(game, tree, server.UsesAllocator);
    }

    // ---------------------------------------------------------------------------------------------
    // Optional server plugins, downloaded on first use and parked when switched off:
    // Bot Hider (XBribo, AGPL-3.0, a Metamod plugin + a CounterStrikeSharp part) makes bots look like
    // players; NockyCZ's Deathmatch (MIT) adds a weapon menu, custom modes, kill sounds and spawn editing.
    // ---------------------------------------------------------------------------------------------

    public const string BotHiderPage = "https://github.com/XBribo/CS2-Bot-Hider";
    public const string DeathmatchPage = "https://github.com/NockyCZ/CS2-Deathmatch";
    private const string BotHiderApi = "https://api.github.com/repos/XBribo/CS2-Bot-Hider/releases/latest";
    private const string DeathmatchApi = "https://api.github.com/repos/NockyCZ/CS2-Deathmatch/releases/latest";

    public const string BotImproverPage = "https://github.com/ed0ard/CS2-Bot-Improver";
    public const string BotAiPage = "https://github.com/ed0ard/CS2-BotAI";
    public const string BotControllerPage = "https://github.com/XBribo/CS2-Bot-Controller";
    private const string BotImproverApi = "https://api.github.com/repos/ed0ard/CS2-Bot-Improver/releases/latest";
    private const string BotAiApi = "https://api.github.com/repos/ed0ard/CS2-BotAI/releases/latest";
    private const string BotControllerApi = "https://api.github.com/repos/XBribo/CS2-Bot-Controller/releases/latest";

    /// <summary>The CounterStrikeSharp plugins inside Bot Improver (BotAI is also its own download).</summary>
    private static readonly string[] ImproverPlugins =
        ["BotAimImprover", "NadeSystem", "BotBuy", "BotState", "RoundDamageRecap", "BotRandomizer"];

    private static string Vdf(GameFolder game, string name) => Path.Combine(game.Addons, "metamod", name + ".vdf");

    private static bool NativeInstalled(GameFolder game, string name) =>
        File.Exists(Vdf(game, name)) || File.Exists(Vdf(game, name) + ".off");

    public static bool BotHiderInstalled(GameFolder game) =>
        File.Exists(Path.Combine(game.Addons, "BotHider", "bin", "win64", "BotHider.dll"));

    public static bool DeathmatchInstalled(GameFolder game) => TreeInstalled(game, "plugins/Deathmatch", "Deathmatch.dll");

    public static bool BotImproverInstalled(GameFolder game) => TreeExists(game, "plugins/BotState");

    public static bool BotAiInstalled(GameFolder game) => TreeExists(game, "plugins/BotAI");

    public static bool BotControllerInstalled(GameFolder game) => NativeInstalled(game, "BotController");

    private static bool TreeExists(GameFolder game, string relative) =>
        Directory.Exists(TreePath(game, relative, parked: false)) || Directory.Exists(TreePath(game, relative, parked: true));

    // What each downloaded plugin should be: Bot Improver brings BotAI, Bot Controller and Bot Hider along.
    private static bool WantHider(ServerSettings s) => s.BotHider || s.BotImprover;
    private static bool WantController(ServerSettings s) => s.BotController || s.BotImprover;
    private static bool WantBotAi(ServerSettings s) => s.BotAi || s.BotImprover;

    /// <summary>BotRandomizer gives bots random skins, knives, gloves, agents and music kits: only when Bot Skins is on.</summary>
    private static bool WantImproverPlugin(ServerSettings s, string plugin) =>
        s.BotImprover && (plugin != "BotRandomizer" || s.BotSkins);

    /// <summary>True when an installed optional plugin is loaded / parked differently from these settings.</summary>
    public static bool ExtraPluginsMismatch(GameFolder game, ServerSettings server)
    {
        bool Native(string name, bool want) => NativeInstalled(game, name) && File.Exists(Vdf(game, name)) != want;
        bool Tree(string relative, bool want) => TreeExists(game, relative) && TreeLoaded(game, relative) != want;
        return Native("BotHider", WantHider(server)) || Native("BotController", WantController(server))
            || Tree("plugins/BotHiderImpl", WantHider(server)) || Tree("plugins/BotControllerImpl", WantController(server))
            || Native("BotVision", server.BotImprover) || Tree("plugins/BotAI", WantBotAi(server))
            || Tree("plugins/Deathmatch", server.UsesDeathmatchPlugin)
            || ImproverPlugins.Any(p => Tree("plugins/" + p, WantImproverPlugin(server, p)));
    }

    /// <summary>Loads or parks every optional plugin to match the current settings.</summary>
    public static void SetExtraPlugins(GameFolder game, ServerSettings server)
    {
        SetNativeActive(game, "BotHider", WantHider(server));
        SetTreeActive(game, "plugins/BotHiderImpl", WantHider(server));
        if (WantHider(server)) SetBotHiderIdentity(game, server.IsPluginRetakes ? "bot" : "player");
        SetNativeActive(game, "BotController", WantController(server));
        // Bot Controller's CounterStrikeSharp half calls into the Metamod half: they load and park together.
        SetTreeActive(game, "plugins/BotControllerImpl", WantController(server));
        SetNativeActive(game, "BotVision", server.BotImprover);
        SetTreeActive(game, "plugins/BotAI", WantBotAi(server));
        foreach (var plugin in ImproverPlugins) SetTreeActive(game, "plugins/" + plugin, WantImproverPlugin(server, plugin));
        SetTreeActive(game, "plugins/Deathmatch", server.UsesDeathmatchPlugin);
        SetTreeActive(game, "shared/DeathmatchAPI", server.UsesDeathmatchPlugin);
    }

    /// <summary>
    /// Bot Hider's "player" identity clears the engine's bot flag. B3none's retakes plugin then takes
    /// the bots for people (it moves them to spectator and holds seats for them), so retakes keeps
    /// Valve's bot flag ("bot"); names, Steam profiles and ping still come from Bot Hider. Read when
    /// the server starts.
    /// </summary>
    private static void SetBotHiderIdentity(GameFolder game, string mode)
    {
        var path = Path.Combine(game.Addons, "BotHider", "config.json");
        if (!File.Exists(path)) return;
        try
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path)) as System.Text.Json.Nodes.JsonObject;
            if (root == null || root["identity_mode"]?.GetValue<string>() == mode) return;
            root["identity_mode"] = mode;
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // A config we can't read: Bot Hider uses its own defaults.
        }
    }

    /// <summary>Metamod only loads *.vdf files, so a parked native plugin is renamed to .vdf.off.</summary>
    private static void SetNativeActive(GameFolder game, string name, bool on)
    {
        var vdf = Vdf(game, name);
        var parked = vdf + ".off";
        if (on && File.Exists(parked) && !File.Exists(vdf)) File.Move(parked, vdf);
        else if (!on && File.Exists(vdf)) File.Move(vdf, parked, overwrite: true);
    }

    /// <summary>Downloads the first release asset that matches; with several picks, the earliest pick that matches wins.</summary>
    private async Task<string> ReleaseAssetAsync(string api, string label, params Func<string, bool>[] picks)
    {
        _log($"Looking up the latest {label} release...");
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(api));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .Where(a => a.Url != "")
            .ToList();
        var url = PickAsset(assets.Select(a => a.Name), picks) is { } name ? assets.First(a => a.Name == name).Url
            : throw new InvalidOperationException($"No matching zip in the latest {label} release.");
        _log($"Downloading {label}...");
        return await DownloadAsync(url);
    }

    /// <summary>The asset name the earliest matching pick chooses, or null.</summary>
    public static string? PickAsset(IEnumerable<string> names, params Func<string, bool>[] picks)
    {
        var list = names.ToList();
        return picks.Select(pick => list.FirstOrDefault(pick)).FirstOrDefault(n => n != null);
    }

    /// <summary>
    /// Bot Improver's Windows packages: "_rules_unchanged" keeps the game rules (not every release has it;
    /// v1.4.6 only has CS2BotImprover.zip). Only the plugins are taken from either, so both work.
    /// </summary>
    public static readonly Func<string, bool>[] BotImproverAssets =
    [
        n => n.Contains("rules_unchanged", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
             && !n.Contains("linux", StringComparison.OrdinalIgnoreCase),
        n => n.Contains("BotImprover", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
             && !n.Contains("linux", StringComparison.OrdinalIgnoreCase),
    ];

    public async Task InstallBotHiderAsync()
    {
        // The zip has addons/ at its root.
        await InstallZipAsync(await ReleaseAssetAsync(BotHiderApi, "Bot Hider", n => n.Contains("windows", StringComparison.OrdinalIgnoreCase)));
        _log("Bot Hider installed.");
    }

    public async Task InstallDeathmatchAsync() =>
        await InstallReleaseAsync(DeathmatchApi, "Deathmatch plugin", name => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

    public async Task InstallBotAiAsync()
    {
        await InstallCssBundleAsync(await ReleaseAssetAsync(BotAiApi, "BotAI", n => n.Equals("BotAI.zip", StringComparison.OrdinalIgnoreCase)));
        _log("BotAI installed.");
    }

    public async Task InstallBotControllerAsync()
    {
        await InstallZipAsync(await ReleaseAssetAsync(BotControllerApi, "Bot Controller",
            n => n.Contains("MM-windows", StringComparison.OrdinalIgnoreCase)));
        await InstallZipAsync(await ReleaseAssetAsync(BotControllerApi, "Bot Controller (CounterStrikeSharp part)",
            n => n.Contains("CSS-API", StringComparison.OrdinalIgnoreCase)));
        _log("Bot Controller installed.");
    }

    /// <summary>
    /// Bot Improver's package changes gameinfo.gi, Valve's mode configs and bot profiles too. Only its
    /// plugins are taken: the launcher manages gameinfo.gi itself (and puts it back for online play).
    /// </summary>
    public async Task InstallBotImproverAsync()
    {
        var zipPath = await ReleaseAssetAsync(BotImproverApi, "Bot Improver", BotImproverAssets);
        await Task.Run(() =>
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var count = 0;
            foreach (var entry in zip.Entries)
            {
                var full = entry.FullName.Replace('\\', '/');
                var at = full.StartsWith("addons/") ? 0 : full.IndexOf("/addons/", StringComparison.Ordinal) + 1;
                if (at <= 0 && !full.StartsWith("addons/") || full.EndsWith('/')) continue;
                var relative = full[at..];
                if (!ImproverFile(relative)) continue;
                var target = Path.GetFullPath(Path.Combine(_game.Csgo, relative));
                if (!target.StartsWith(Path.GetFullPath(_game.Addons), StringComparison.OrdinalIgnoreCase)) continue;
                if (relative.StartsWith("addons/counterstrikesharp/configs/") && File.Exists(target)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
                count++;
            }
            if (count == 0) throw new InvalidDataException("The Bot Improver package had no plugin files.");
            _log($"Extracted {count} Bot Improver files.");
        });
        _log("Bot Improver installed.");
    }

    private static bool ImproverFile(string relative)
    {
        string[] prefixes =
        [
            "addons/counterstrikesharp/plugins/", "addons/counterstrikesharp/shared/", "addons/counterstrikesharp/data/",
            "addons/counterstrikesharp/configs/plugins/", "addons/BotHider/", "addons/BotController/", "addons/BotVision/",
        ];
        if (relative.StartsWith("addons/counterstrikesharp/plugins/disabled/", StringComparison.OrdinalIgnoreCase)) return false;
        if (prefixes.Any(p => relative.StartsWith(p, StringComparison.OrdinalIgnoreCase))) return true;
        return relative.StartsWith("addons/metamod/Bot", StringComparison.OrdinalIgnoreCase)
               && relative.EndsWith(".vdf", StringComparison.OrdinalIgnoreCase) && !relative.Contains(".linux.");
    }

    private async Task InstallReleaseAsync(string api, string label, Func<string, bool> pick)
    {
        _log($"Looking up the latest {label} release...");
        using var doc = JsonDocument.Parse(await _http.GetStringAsync(api));
        var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
            .FirstOrDefault(a => pick(a.Name) && a.Url != "");
        if (asset.Url == "")
            throw new InvalidOperationException($"No zip found in the latest {label} release.");

        _log($"Downloading {asset.Name}...");
        await InstallCssBundleAsync(await DownloadAsync(asset.Url));
        _log($"{label} installed.");
    }

    /// <summary>
    /// Extracts a CounterStrikeSharp plugin zip. Accepts an addons/counterstrikesharp tree, a bundle
    /// rooted at plugins/shared/configs, or a single plugin folder.
    /// </summary>
    public Task InstallCssBundleAsync(string zipPath) => Task.Run(() =>
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var root = Path.GetFullPath(CssRoot(_game));
        var count = 0;
        foreach (var entry in zip.Entries)
        {
            var full = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (full.Length == 0 || full.EndsWith('/')) continue;
            var relative = CssRelative(full);
            if (relative == null) continue;
            var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            if (relative.StartsWith("configs/", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            count++;
        }
        if (count == 0)
            throw new InvalidDataException($"{Path.GetFileName(zipPath)} has no CounterStrikeSharp plugin files.");
        _log($"Extracted {count} files from {Path.GetFileName(zipPath)}.");
    });

    private static string? CssRelative(string full)
    {
        const string nested = "addons/counterstrikesharp/";
        var at = full.IndexOf(nested, StringComparison.OrdinalIgnoreCase);
        if (at >= 0) return full[(at + nested.Length)..];
        if (full.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)
            || full.StartsWith("shared/", StringComparison.OrdinalIgnoreCase)
            || full.StartsWith("configs/", StringComparison.OrdinalIgnoreCase)
            || full.StartsWith("gamedata/", StringComparison.OrdinalIgnoreCase))
            return full;
        var slash = full.IndexOf('/');
        if (slash <= 0) return null;
        var top = full[..slash];
        if (top.EndsWith("Plugin", StringComparison.OrdinalIgnoreCase) || top.Equals("RetakesAllocator", StringComparison.OrdinalIgnoreCase)
            || top.Equals("BotAI", StringComparison.OrdinalIgnoreCase))
            return "plugins/" + full;
        return null;
    }

    private static bool TreeLoaded(GameFolder game, string relative) =>
        Directory.Exists(TreePath(game, relative, parked: false));

    private static bool TreeInstalled(GameFolder game, string relative, string file) =>
        File.Exists(Path.Combine(TreePath(game, relative, parked: false), file))
        || File.Exists(Path.Combine(TreePath(game, relative, parked: true), file));

    private static void SetTreeActive(GameFolder game, string relative, bool on)
    {
        var active = TreePath(game, relative, parked: false);
        var parked = TreePath(game, relative, parked: true);
        // A fresh download lands in the plugins folder: it wins over an older parked copy.
        if (on && Directory.Exists(active))
        {
            if (Directory.Exists(parked)) Directory.Delete(parked, recursive: true);
            return;
        }
        var (from, to) = on ? (parked, active) : (active, parked);
        if (!Directory.Exists(from)) return;
        if (Directory.Exists(to)) Directory.Delete(to, recursive: true);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);
    }

    private static string TreePath(GameFolder game, string relative, bool parked)
    {
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var root = parked ? Path.Combine(CssRoot(game), "lst-parked") : CssRoot(game);
        return Path.Combine([root, .. parts]);
    }

    /// <summary>
    /// True when the installed plugin is exactly the one inside this launcher. Compares the files, not
    /// version numbers, so any newer launcher build updates the plugin.
    /// </summary>
    public static bool PluginMatchesBundled(GameFolder game)
    {
        using var bundled = Assembly.GetExecutingAssembly().GetManifestResourceStream("plugin/LanSkinTester.dll");
        if (bundled == null) return true; // launcher built without the plugin: nothing to compare
        var installed = Path.Combine(game.PluginDir, "LanSkinTester.dll");
        if (!File.Exists(installed)) return false;
        using var copy = new MemoryStream();
        bundled.CopyTo(copy);
        return copy.ToArray().AsSpan().SequenceEqual(File.ReadAllBytes(installed));
    }

    /// <summary>The plugin itself ships inside the launcher exe.</summary>
    public void InstallPlugin()
    {
        var asm = Assembly.GetExecutingAssembly();
        var resources = asm.GetManifestResourceNames().Where(n => n.StartsWith("plugin/")).ToList();
        if (resources.Count == 0)
            throw new InvalidOperationException("This launcher was built without the plugin inside. Build it with build.sh.");

        foreach (var name in resources)
        {
            var target = Path.Combine(_game.PluginDir, name["plugin/".Length..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = asm.GetManifestResourceStream(name)!;
            using var output = File.Create(target);
            input.CopyTo(output);
        }
        _log("LAN Skin Tester plugin installed.");
    }

    /// <summary>
    /// Extracts a Metamod or CounterStrikeSharp zip into game\csgo. Works whether the zip has "addons/" at
    /// its root or inside a folder. Existing CounterStrikeSharp config files are kept.
    /// </summary>
    public Task InstallZipAsync(string zipPath) => Task.Run(() =>
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var count = 0;
        foreach (var entry in zip.Entries)
        {
            var full = entry.FullName.Replace('\\', '/');
            int at;
            if (full.StartsWith("addons/")) at = 0;
            else
            {
                var i = full.IndexOf("/addons/", StringComparison.Ordinal);
                if (i < 0) continue;
                at = i + 1;
            }

            var relative = full[at..];
            var target = Path.GetFullPath(Path.Combine(_game.Csgo, relative));
            if (!target.StartsWith(Path.GetFullPath(_game.Addons), StringComparison.OrdinalIgnoreCase))
                continue; // never write outside game\csgo\addons

            if (relative.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            if (relative.StartsWith("addons/counterstrikesharp/configs/") && File.Exists(target))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            count++;
        }

        if (count == 0)
            throw new InvalidDataException($"{Path.GetFileName(zipPath)} has no addons folder - is it the right zip?");
        _log($"Extracted {count} files from {Path.GetFileName(zipPath)}.");
    });

    private async Task<string> DownloadAsync(string url)
    {
        var path = Path.Combine(Path.GetTempPath(), "LanSkinTester-" + Path.GetFileName(new Uri(url).LocalPath));
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var file = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            Progress?.Invoke(total > 0 ? (double)done / total : -1);
        }
        return path;
    }

    // ---------------------------------------------------------------------------------------------
    // Versions / updates
    // ---------------------------------------------------------------------------------------------

    /// <summary>Version of the plugin inside this launcher.</summary>
    public static Version? BundledPluginVersion()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("plugin/LanSkinTester.dll");
        if (stream == null) return null;
        using var pe = new PEReader(stream);
        return pe.GetMetadataReader().GetAssemblyDefinition().Version;
    }

    public static Version? InstalledPluginVersion(GameFolder game)
    {
        var dll = Path.Combine(game.PluginDir, "LanSkinTester.dll");
        if (!File.Exists(dll)) return null;
        try { return AssemblyName.GetAssemblyName(dll).Version; }
        catch (Exception) { return null; }
    }

    /// <summary>"1.0.376" read from CounterStrikeSharp's API dll.</summary>
    public static string? InstalledCssVersion(GameFolder game)
    {
        var dll = Path.Combine(game.Addons, "counterstrikesharp", "api", "CounterStrikeSharp.API.dll");
        return File.Exists(dll) ? Short(FileVersionInfo.GetVersionInfo(dll).ProductVersion) : null;
    }

    public static string? InstalledMetamodVersion(GameFolder game)
    {
        var bin = Path.Combine(game.Addons, "metamod", "bin", "win64");
        if (!Directory.Exists(bin)) return null;
        var dll = Directory.GetFiles(bin, "*.dll").FirstOrDefault(f => Path.GetFileName(f).Contains("metamod", StringComparison.OrdinalIgnoreCase));
        return dll != null ? Short(FileVersionInfo.GetVersionInfo(dll).FileVersion) : null;
    }

    public async Task<(string? Metamod, string? CounterStrikeSharp)> LatestVersionsAsync()
    {
        string? metamod = null, css = null;
        try
        {
            // "mmsource-2.0.0-git1380-windows.zip" -> "2.0.0-git1380"
            var file = (await _http.GetStringAsync(MetamodLatest)).Trim();
            var m = Regex.Match(file, @"mmsource-(.+)-windows");
            metamod = m.Success ? m.Groups[1].Value : file;
        }
        catch (Exception) { }
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(CounterStrikeSharpApi));
            css = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v');
        }
        catch (Exception) { }
        return (metamod, css);
    }

    private static string? Short(string? version) => version?.Split('+', ' ')[0];

    // ---------------------------------------------------------------------------------------------
    // Uninstall
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Removes everything this launcher installed (Metamod, CounterStrikeSharp, plugin, server cfg) and
    /// restores gameinfo.gi. The saved loadout is copied to the launcher's data folder first.
    /// </summary>
    public void UninstallAll(string backupDir)
    {
        if (File.Exists(_game.LoadoutFile))
        {
            Directory.CreateDirectory(backupDir);
            var backup = Path.Combine(backupDir, $"loadouts-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(_game.LoadoutFile, backup);
            _log($"Saved your loadout to {backup}");
        }

        GameInfoPatcher.Disable(_game.GameInfo);
        foreach (var dir in new[] { Path.Combine(_game.Addons, "counterstrikesharp"), Path.Combine(_game.Addons, "metamod") })
        {
            if (!Directory.Exists(dir)) continue;
            Directory.Delete(dir, recursive: true);
            _log($"Removed {dir}");
        }
        foreach (var file in new[]
                 {
                     Path.Combine(_game.Addons, "metamod.vdf"), Path.Combine(_game.Addons, "metamod_x64.vdf"),
                     Path.Combine(_game.Csgo, "cfg", "lanskintester_server.cfg"),
                 })
        {
            if (!File.Exists(file)) continue;
            File.Delete(file);
            _log($"Removed {file}");
        }
        _log("Uninstalled. CS2 is completely back to normal.");
    }
}
