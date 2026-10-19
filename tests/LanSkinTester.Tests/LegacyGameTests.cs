using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Tests;

/// <summary>CS:GO legacy: folder check, SourceMod's skin guideline switch and the listen server command line.</summary>
public sealed class LegacyGameTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lst-csgo").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void ValidNeedsCsgoExeAndGameInfo()
    {
        Assert.False(LegacyGame.IsValid(_root));
        Directory.CreateDirectory(Path.Combine(_root, "csgo"));
        File.WriteAllText(Path.Combine(_root, "csgo.exe"), "");
        File.WriteAllText(Path.Combine(_root, "csgo", "gameinfo.txt"), "");
        Assert.True(LegacyGame.IsValid(_root));
    }

    [Fact]
    public void AllowSkinsTurnsOnlyTheGuidelineOff()
    {
        var cfg = "\"Core\"\n{\n\t\"FollowCSGOServerGuidelines\"\t\"yes\"\n\t\"DisableAutoUpdate\"\t\"yes\"\n}";
        var fixedCfg = LegacyGame.AllowSkins(cfg);
        Assert.Contains("\"FollowCSGOServerGuidelines\"\t\"no\"", fixedCfg);
        Assert.Contains("\"DisableAutoUpdate\"\t\"yes\"", fixedCfg);
    }

    [Fact]
    public void LaunchArgumentsAreLanAndInsecure()
    {
        Assert.Equal($"-insecure -novid -console -netconport {ClientConsole.Port} -netconpassword {ClientConsole.Password} +sv_lan 1 +game_type 1 +game_mode 2 +bot_quota 20 +map de_dust2",
            LegacyGame.LaunchArguments("deathmatch", "de_dust2", 99));
        Assert.Throws<ArgumentException>(() => LegacyGame.LaunchArguments("deathmatch", "de_dust2; quit", 0));
        Assert.Throws<ArgumentException>(() => LegacyGame.LaunchArguments("nope", "de_dust2", 0));
    }
}
