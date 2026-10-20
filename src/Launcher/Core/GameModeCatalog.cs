using System.Text;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Reads CS2's own game mode list (gamemodes.txt, loose or inside pak01_dir.vpk) to find modes the
/// launcher doesn't know by number, like Rush. Returns null when the game files don't have the mode.
/// </summary>
public static class GameModeCatalog
{
    /// <summary>Finds a mode whose name contains <paramref name="name"/> (e.g. "rush"), with its maps.</summary>
    public static GameMode? Find(GameFolder game, string name, string displayName)
    {
        try
        {
            if (ReadGameModes(game) is not { } text) return null;
            var root = KeyValues.Parse(text);
            foreach (var type in root.Child("gameTypes")?.Children ?? [])
            {
                if (!int.TryParse(type.Child("value")?.Value, out var typeValue)) continue;
                foreach (var mode in type.Child("gameModes")?.Children ?? [])
                {
                    if (!mode.Key.Contains(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!int.TryParse(mode.Child("value")?.Value, out var modeValue)) continue;
                    var maps = MapsOf(root, mode, game);
                    if (maps.Length > 0) return new GameMode(displayName, typeValue, modeValue, maps);
                }
            }
        }
        catch (Exception)
        {
            // Unknown file layout: the mode just isn't offered.
        }
        return null;
    }

    private static string[] MapsOf(KeyValues root, KeyValues mode, GameFolder game)
    {
        var maps = new List<string>();
        foreach (var key in new[] { "maplist", "maps" })
            if (mode.Child(key) is { } list) maps.AddRange(list.Children.Select(c => c.Key));

        if (root.Child("mapgroups") is { } groups)
            foreach (var key in new[] { "mapgroupsMP", "mapgroupsSP", "mapgroups" })
                foreach (var group in mode.Child(key)?.Children ?? [])
                    if (groups.Child(group.Key)?.Child("maps") is { } groupMaps)
                        maps.AddRange(groupMaps.Children.Select(c => c.Key));

        // Prefer the maps that are installed as maps\<name>.vpk (all of them if none are found there).
        var all = maps.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var installed = all.Where(m => File.Exists(Path.Combine(game.Csgo, "maps", m + ".vpk"))).ToArray();
        return installed.Length > 0 ? installed : all;
    }

    private static string? ReadGameModes(GameFolder game)
    {
        foreach (var loose in new[] { "gamemodes.txt", "gamemodes_server.txt" })
        {
            var path = Path.Combine(game.Csgo, loose);
            if (File.Exists(path) && File.ReadAllText(path) is var text && text.Contains("gameTypes", StringComparison.OrdinalIgnoreCase))
                return text;
        }
        var dir = Path.Combine(game.Csgo, "pak01_dir.vpk");
        return File.Exists(dir) && Vpk.Read(dir, "", "gamemodes", "txt") is { } bytes ? Encoding.UTF8.GetString(bytes) : null;
    }
}

/// <summary>Reads one file out of a Valve VPK (version 1 or 2) directory archive.</summary>
public static class Vpk
{
    private const uint Signature = 0x55AA1234;

    /// <param name="folder">Folder inside the archive, "" for the root.</param>
    public static byte[]? Read(string dirFile, string folder, string name, string extension)
    {
        using var stream = File.OpenRead(dirFile);
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != Signature) return null;
        var version = reader.ReadUInt32();
        var treeSize = reader.ReadUInt32();
        var headerSize = version switch { 1 => 12, 2 => 28, _ => -1 };
        if (headerSize < 0) return null;
        if (version == 2) reader.ReadBytes(16); // section sizes

        var wantFolder = folder == "" ? " " : folder.Replace('\\', '/');
        bool Same(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);
        // The tree: extension, then folder, then file name; each level ends with an empty string.
        for (var ext = ReadString(reader); ext != ""; ext = ReadString(reader))
        for (var path = ReadString(reader); path != ""; path = ReadString(reader))
        for (var file = ReadString(reader); file != ""; file = ReadString(reader))
        {
            reader.ReadUInt32(); // CRC
            var preloadBytes = reader.ReadUInt16();
            var archiveIndex = reader.ReadUInt16();
            var offset = reader.ReadUInt32();
            var length = reader.ReadUInt32();
            reader.ReadUInt16(); // terminator
            var preload = reader.ReadBytes(preloadBytes);
            if (!Same(ext, extension) || !Same(path, wantFolder) || !Same(file, name)) continue;

            if (length == 0) return preload;
            byte[] data;
            if (archiveIndex == 0x7FFF)
            {
                stream.Position = headerSize + treeSize + offset;
                data = reader.ReadBytes((int)length);
            }
            else
            {
                data = new byte[length];
                using var part = File.OpenRead(dirFile.Replace("_dir.vpk", $"_{archiveIndex:000}.vpk"));
                part.Position = offset;
                part.ReadExactly(data);
            }
            return [.. preload, .. data];
        }
        return null;
    }

    /// <summary>Every file in the archive as (folder, name, extension); the root folder is "".</summary>
    public static List<(string Folder, string Name, string Extension)> Files(string dirFile)
    {
        using var reader = new BinaryReader(File.OpenRead(dirFile));
        var list = new List<(string, string, string)>();
        if (reader.ReadUInt32() != Signature) return list;
        var version = reader.ReadUInt32();
        reader.ReadUInt32(); // tree size
        if (version == 2) reader.ReadBytes(16);
        else if (version != 1) return list;
        for (var ext = ReadString(reader); ext != ""; ext = ReadString(reader))
        for (var path = ReadString(reader); path != ""; path = ReadString(reader))
        for (var file = ReadString(reader); file != ""; file = ReadString(reader))
        {
            reader.ReadBytes(4); // CRC
            var preloadBytes = reader.ReadUInt16();
            reader.ReadBytes(12); // archive index, offset, length, terminator
            reader.ReadBytes(preloadBytes);
            list.Add((path == " " ? "" : path, file, ext));
        }
        return list;
    }

    private static string ReadString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        for (byte b; (b = reader.ReadByte()) != 0;) bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}

/// <summary>Minimal Valve KeyValues text reader ("key" "value" and "key" { ... }).</summary>
public class KeyValues
{
    public string Key { get; }
    public string? Value { get; private init; }
    public List<KeyValues> Children { get; } = new();

    private KeyValues(string key) => Key = key;

    public KeyValues? Child(string key) => Children.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static KeyValues Parse(string text)
    {
        var root = new KeyValues("");
        var stack = new Stack<KeyValues>([root]);
        string? pendingKey = null;
        foreach (var token in Tokenize(text))
        {
            if (token == "{")
            {
                var node = new KeyValues(pendingKey ?? "");
                stack.Peek().Children.Add(node);
                stack.Push(node);
            }
            else if (token == "}")
            {
                if (stack.Count > 1) stack.Pop();
            }
            else if (pendingKey == null)
            {
                pendingKey = token;
                continue;
            }
            else stack.Peek().Children.Add(new KeyValues(pendingKey) { Value = token });
            pendingKey = null;
        }
        // The file's own top-level block ("GameModes.txt" { ... }) holds everything.
        return root.Children is [{ Value: null } only] ? only : root;
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) i++;
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
                while (i < text.Length && text[i] != '\n') i++;
            else if (c is '{' or '}')
            {
                i++;
                yield return c.ToString();
            }
            else if (c == '[') // platform conditionals like [$WIN32]
            {
                while (i < text.Length && text[i] != ']') i++;
                i++;
            }
            else
            {
                var sb = new StringBuilder();
                if (c == '"')
                {
                    for (i++; i < text.Length && text[i] != '"'; i++)
                    {
                        if (text[i] == '\\' && i + 1 < text.Length) i++;
                        sb.Append(text[i]);
                    }
                    i++;
                }
                else
                    while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}' or '"')) sb.Append(text[i++]);
                yield return sb.ToString();
            }
        }
    }
}
