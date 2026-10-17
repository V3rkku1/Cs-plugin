using System.Text.RegularExpressions;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Adds/removes the Metamod search path in gameinfo.gi. While it's there, CS2 must run with -insecure.
/// </summary>
public static class GameInfoPatcher
{
    private const string MetamodLine = "Game\tcsgo/addons/metamod";
    private static readonly Regex IsMetamodLine = new(@"^\s*Game\s+csgo/addons/metamod\s*$");
    private static readonly Regex IsCsgoLine = new(@"^(\s*)Game\s+csgo\s*$");

    public static bool IsEnabled(string gameInfoPath) => File.ReadLines(gameInfoPath).Any(IsMetamodLine.IsMatch);

    public static void Enable(string gameInfoPath)
    {
        var lines = File.ReadAllLines(gameInfoPath).ToList();
        if (lines.Any(IsMetamodLine.IsMatch)) return;

        var index = lines.FindIndex(IsCsgoLine.IsMatch);
        if (index < 0)
            throw new InvalidDataException("gameinfo.gi has an unexpected format (no \"Game csgo\" line). Nothing was changed.");

        File.Copy(gameInfoPath, gameInfoPath + ".backup", overwrite: true);
        lines.Insert(index, IsCsgoLine.Match(lines[index]).Groups[1].Value + MetamodLine);
        File.WriteAllLines(gameInfoPath, lines);
    }

    public static void Disable(string gameInfoPath)
    {
        var lines = File.ReadAllLines(gameInfoPath).ToList();
        if (lines.RemoveAll(IsMetamodLine.IsMatch) > 0) File.WriteAllLines(gameInfoPath, lines);
    }
}
