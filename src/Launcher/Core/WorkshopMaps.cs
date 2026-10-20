namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Workshop maps Steam has downloaded for CS2, and clean-up of the map copies older launchers put in
/// csgo\maps (they are listed in a small file there and removed on Stop).
/// </summary>
public static class WorkshopMaps
{
    private const string CopiesFile = ".lanskintester_workshop_copies";

    /// <summary>steamapps\workshop\content\730\&lt;id&gt; in the same Steam library as CS2.</summary>
    public static string ContentDir(GameFolder game, string id) => Path.Combine(ContentRoot(game), id);

    private static string ContentRoot(GameFolder game) => Path.GetFullPath(Path.Combine(game.Root, "..", "..", "workshop", "content", "730"));

    public static string SubscribeUrl(string id) => $"steam://url/CommunityFilePage/{id}";

    /// <summary>Every Workshop item Steam has downloaded for CS2 that contains a map (.vpk): id and folder.</summary>
    public static List<(string Id, string Vpk, long Bytes, DateTime Updated)> AllDownloaded(GameFolder game)
    {
        var root = ContentRoot(game);
        var list = new List<(string, string, long, DateTime)>();
        if (!Directory.Exists(root)) return list;
        foreach (var dir in Directory.GetDirectories(root))
        {
            var id = Path.GetFileName(dir);
            if (!id.All(char.IsDigit)) continue;
            try
            {
                var vpk = FindDownloaded(game, id);
                if (vpk == null) continue;
                var info = new FileInfo(vpk);
                list.Add((id, vpk, info.Length, info.LastWriteTimeUtc));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* being updated by Steam */ }
        }
        return list;
    }

    /// <summary>Steam has downloaded the map: any .vpk in its folder (cheap enough for the poll every second).</summary>
    public static bool IsDownloaded(GameFolder game, string id)
    {
        var dir = ContentDir(game, id);
        try { return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.vpk", SearchOption.AllDirectories).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; } // Steam is updating it
    }

    /// <summary>The map's .vpk if Steam has downloaded it, else null.</summary>
    public static string? FindDownloaded(GameFolder game, string id)
    {
        var dir = ContentDir(game, id);
        if (!Directory.Exists(dir)) return null;
        // The map is the biggest .vpk (some items also ship small extra ones).
        try
        {
            return Directory.GetFiles(dir, "*.vpk", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null; // Steam is updating it right now
        }
    }

    /// <summary>
    /// A map whose own script changes maps with map_workshop (Movement Hub and other single-player hubs).
    /// That command only exists in an offline game: on the dedicated server the map stays on its loader
    /// map, which has no spawns, so nobody gets past team select. Returns the map to start offline (the
    /// first in its addoninfo.txt), or null for a normal map or one that can't be read.
    /// </summary>
    public static string? OfflineOnlyMap(GameFolder game, string id)
    {
        try
        {
            var dir = ContentDir(game, id);
            if (!Directory.Exists(dir)) return null;
            var vpk = Directory.GetFiles(dir, "*_dir.vpk").FirstOrDefault() ?? FindDownloaded(game, id);
            if (vpk == null) return null;
            string Text(string folder, string name, string ext) =>
                Vpk.Read(vpk, folder, name, ext) is { } bytes ? System.Text.Encoding.UTF8.GetString(bytes) : "";
            if (!Vpk.Files(vpk).Any(f => f.Folder == "scripts" && f.Extension == "vjs_c"
                                         && Text(f.Folder, f.Name, f.Extension).Contains("map_workshop", StringComparison.Ordinal)))
                return null;
            var m = System.Text.RegularExpressions.Regex.Match(Text("", "addoninfo", "txt"), @"maps\s*=\s*\[\s*""([^""]+)""");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or IndexOutOfRangeException)
        {
            return null; // Steam is updating it, or not a VPK we understand: host it the normal way
        }
    }

    /// <summary>Deletes the map copies older launchers made (after CS2 has closed).</summary>
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
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                left.Add(file); // still in use; next Stop
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
