using System.Text;

namespace LanSkinTester.Launcher.Core;

/// <summary>Just enough of Valve's VPK (version 2) format to list a Workshop item's files and read small ones.</summary>
public static class Vpk
{
    public record Entry(string Path, ushort Archive, uint Offset, uint Length, byte[] Preload);

    private const ushort InDirFile = 0x7fff;

    public static List<Entry> List(string dirVpk)
    {
        var b = File.ReadAllBytes(dirVpk);
        if (BitConverter.ToUInt32(b, 0) != 0x55aa1234) throw new ArgumentException("Not a VPK file.");
        var headerSize = BitConverter.ToUInt32(b, 4) == 1 ? 12 : 28;
        var pos = headerSize;
        string Str()
        {
            var start = pos;
            while (b[pos] != 0) pos++;
            return Encoding.UTF8.GetString(b, start, pos++ - start);
        }

        var list = new List<Entry>();
        for (var ext = Str(); ext != ""; ext = Str())
        for (var dir = Str(); dir != ""; dir = Str())
        for (var name = Str(); name != ""; name = Str())
        {
            var preload = BitConverter.ToUInt16(b, pos + 4);
            var archive = BitConverter.ToUInt16(b, pos + 6);
            var offset = BitConverter.ToUInt32(b, pos + 8);
            var length = BitConverter.ToUInt32(b, pos + 12);
            var data = b.AsSpan(pos + 18, preload).ToArray();
            pos += 18 + preload;
            var folder = dir.Trim() is "" or " " ? "" : dir.Trim() + "/";
            list.Add(new Entry($"{folder}{name}.{ext}", archive, offset, length, data));
        }
        // Data stored in the _dir file itself starts right after the tree.
        TreeEnd[dirVpk] = pos;
        return list;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> TreeEnd = new();

    /// <summary>The file's contents as text (binary files come out garbled, which is fine for searching).</summary>
    public static string ReadText(string dirVpk, Entry e)
    {
        var bytes = new byte[e.Preload.Length + e.Length];
        e.Preload.CopyTo(bytes, 0);
        if (e.Length > 0)
        {
            var archive = e.Archive == InDirFile ? dirVpk : dirVpk.Replace("_dir.vpk", $"_{e.Archive:D3}.vpk");
            var start = e.Archive == InDirFile ? TreeEnd.GetValueOrDefault(dirVpk) : 0;
            using var fs = File.OpenRead(archive);
            fs.Position = start + e.Offset;
            fs.ReadExactly(bytes, e.Preload.Length, (int)e.Length);
        }
        return Encoding.UTF8.GetString(bytes);
    }
}
