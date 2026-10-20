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
}
