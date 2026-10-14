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
