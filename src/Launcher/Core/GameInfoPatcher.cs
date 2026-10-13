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

    public static bool IsEnabled(string gameInfoPath) =>
        File.ReadLines(gameInfoPath).Any(l => IsMetamodLine.IsMatch(l));

    public static void Enable(string gameInfoPath)
    {
        var lines = File.ReadAllLines(gameInfoPath).ToList();
        if (lines.Any(l => IsMetamodLine.IsMatch(l))) return;

        var index = lines.FindIndex(l => IsCsgoLine.IsMatch(l));
        if (index < 0)
            throw new InvalidDataException("gameinfo.gi has an unexpected format (no \"Game csgo\" line). Nothing was changed.");

        File.Copy(gameInfoPath, gameInfoPath + ".backup", overwrite: true);
        var indent = IsCsgoLine.Match(lines[index]).Groups[1].Value;
        lines.Insert(index, indent + MetamodLine);
        File.WriteAllLines(gameInfoPath, lines);
    }

    public static void Disable(string gameInfoPath)
    {
        var lines = File.ReadAllLines(gameInfoPath).ToList();
        if (lines.RemoveAll(l => IsMetamodLine.IsMatch(l)) > 0)
            File.WriteAllLines(gameInfoPath, lines);
    }
}
