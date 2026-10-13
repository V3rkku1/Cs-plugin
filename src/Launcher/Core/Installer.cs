using System.IO.Compression;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>Downloads and installs Metamod, CounterStrikeSharp, the plugin and the skin names.</summary>
public class Installer
{
    public const string MetamodPage = "https://www.sourcemm.net/downloads.php?branch=dev";
    public const string CounterStrikeSharpPage = "https://github.com/roflmuffin/CounterStrikeSharp/releases/latest";

    private const string MetamodLatest = "https://mms.alliedmods.net/mmsdrop/2.0/mmsource-latest-windows";
    private const string MetamodDrop = "https://mms.alliedmods.net/mmsdrop/2.0/";
    private const string CounterStrikeSharpApi = "https://api.github.com/repos/roflmuffin/CounterStrikeSharp/releases/latest";
    private const string SkinNamesBase = "https://raw.githubusercontent.com/Nereziel/cs2-WeaponPaints/main/website/data/";
    private static readonly string[] SkinNameFiles = ["skins_en.json", "gloves_en.json"];

    private readonly GameFolder _game;
    private readonly HttpClient _http;
    private readonly Action<string> _log;

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

    public async Task InstallSkinNamesAsync()
    {
        Directory.CreateDirectory(_game.SkinNamesDir);
        foreach (var file in SkinNameFiles)
        {
            _log($"Downloading {file}...");
            var bytes = await _http.GetByteArrayAsync(SkinNamesBase + file);
            await File.WriteAllBytesAsync(Path.Combine(_game.SkinNamesDir, file), bytes);
        }
        _log("Skin names installed.");
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
        await using (var stream = await _http.GetStreamAsync(url))
        await using (var file = File.Create(path))
            await stream.CopyToAsync(file);
        return path;
    }
}
