namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Workshop maps for the local server. A LAN server (sv_lan 1) is offline from Steam, so it can't
/// download Workshop maps with host_workshop_map. Instead, when Steam already downloaded the map
/// (you subscribed to it), its .vpk is copied into csgo\maps and loaded with a plain "map" - instant
/// and offline. The copies are listed in a small file and removed again on Stop.
/// </summary>
public static class WorkshopMaps
{
    private const string CopiesFile = ".lanskintester_workshop_copies";

    /// <summary>steamapps\workshop\content\730\&lt;id&gt; in the same Steam library as CS2.</summary>
    public static string ContentDir(GameFolder game, string id) =>
        Path.GetFullPath(Path.Combine(game.Root, "..", "..", "workshop", "content", "730", id));

    public static string SubscribeUrl(string id) => $"steam://url/CommunityFilePage/{id}";

    /// <summary>The map's .vpk if Steam has downloaded it, else null.</summary>
    public static string? FindDownloaded(GameFolder game, string id)
    {
        var dir = ContentDir(game, id);
        if (!Directory.Exists(dir)) return null;
        // The map is the biggest .vpk (some items also ship small extra ones).
        return Directory.GetFiles(dir, "*.vpk", SearchOption.AllDirectories)
            .OrderByDescending(f => new FileInfo(f).Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Copies the downloaded map into csgo\maps and returns the name to load it with,
    /// or null when it isn't downloaded (then the server has to download it itself).
    /// </summary>
    public static string? Prepare(GameFolder game, string id)
    {
        var source = FindDownloaded(game, id);
        if (source == null) return null;

        var name = Path.GetFileNameWithoutExtension(source);
        if (name.All(char.IsDigit) || name.Equals("vpk", StringComparison.OrdinalIgnoreCase)) name = $"workshop_{id}";
        var mapsDir = Path.Combine(game.Csgo, "maps");
        Directory.CreateDirectory(mapsDir);
        var target = Path.Combine(mapsDir, name + ".vpk");

        var copies = ReadCopies(game);
        if (File.Exists(target) && !copies.Contains(name + ".vpk", StringComparer.OrdinalIgnoreCase))
        {
            // A real game file has that name (e.g. an official map): never overwrite it, use our own name.
            name = $"workshop_{id}";
            target = Path.Combine(mapsDir, name + ".vpk");
        }

        if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length ||
            File.GetLastWriteTimeUtc(target) < File.GetLastWriteTimeUtc(source))
            File.Copy(source, target, overwrite: true);

        if (!copies.Contains(name + ".vpk", StringComparer.OrdinalIgnoreCase))
        {
            copies.Add(name + ".vpk");
            File.WriteAllLines(Path.Combine(mapsDir, CopiesFile), copies);
        }
        return name;
    }

    /// <summary>Deletes the map copies made by <see cref="Prepare"/> (after CS2 has closed).</summary>
    public static void RemoveCopies(GameFolder game)
    {
        var mapsDir = Path.Combine(game.Csgo, "maps");
        var list = Path.Combine(mapsDir, CopiesFile);
        if (!File.Exists(list)) return;

        var left = new List<string>();
        foreach (var file in ReadCopies(game))
        {
            try
            {
                var path = Path.Combine(mapsDir, Path.GetFileName(file)); // only inside csgo\maps
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                left.Add(file); // still in use; next Stop
            }
            catch (UnauthorizedAccessException)
            {
                left.Add(file);
            }
        }
        if (left.Count == 0) File.Delete(list);
        else File.WriteAllLines(list, left);
    }

    private static List<string> ReadCopies(GameFolder game)
    {
        var list = Path.Combine(game.Csgo, "maps", CopiesFile);
        return File.Exists(list) ? File.ReadAllLines(list).Where(l => l.Trim() != "").ToList() : new List<string>();
    }
}
