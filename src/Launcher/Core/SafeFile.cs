namespace LanSkinTester.Launcher.Core;

public static class SafeFile
{
    /// <summary>Writes a temp file and swaps it in, so a crash or power cut mid-write leaves the old file whole.</summary>
    public static void WriteAllText(string path, string text)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Moves a file that can't be read aside ("inventory.broken-20261020-153000.json"), so the next save doesn't
    /// overwrite what may still be rescued by hand. Returns the new name, or null when it couldn't be moved.
    /// </summary>
    public static string? SetAside(string path)
    {
        var aside = Path.Combine(Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}.broken-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
        try
        {
            File.Move(path, aside);
            return aside;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
