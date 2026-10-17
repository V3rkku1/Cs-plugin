using System.Reflection;
using LanSkinTester.Launcher.Core;
using Plugin = global::LanSkinTester.LanSkinTester;

namespace LanSkinTester.Tests;

/// <summary>The Hacker Mode page's settings must mean the same thing in the plugin.</summary>
public class HackerModeTests
{
    private static readonly Type PowerType = typeof(Plugin).GetNestedType("Power", BindingFlags.NonPublic)!;

    private static object? Parse(string method, string text) =>
        typeof(Plugin).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [text]);

    private static ServerSettings Everything() => new()
    {
        HackerMode = 1, HackWallhack = true, HackAimbot = true, HackOneTap = true, HackAmmo = true, HackSpeed = true,
        HackBhop = true, HackNoFlash = true, HackEspInfo = true, HackAimWalls = true, HackNoRecoil = true, HackRadar = true,
        HackBoxEsp = true, HackBoneEsp = true, HackTags = true, HackTrigger = true,
    };

    private static string Arg(IEnumerable<string> commands, string name) =>
        commands.Single(c => c.StartsWith(name + " ")).Substring(name.Length + 1);

    [Fact]
    public void EveryPowerNameIsAPluginPower()
    {
        var names = Arg(Everything().HackerCommands(), "lst_hack_powers").Split(',');
        Assert.Equal(15, names.Length);
        var all = 0;
        foreach (var name in names)
        {
            Assert.True(Enum.TryParse(PowerType, name, ignoreCase: true, out var power), $"{name} is not a power in the plugin");
            all |= (int)power!;
        }
        Assert.Equal((int)Enum.Parse(PowerType, "All"), all); // and together they are every power
    }

    [Fact]
    public void NoPowersMeansNone()
    {
        var s = new ServerSettings { HackerMode = 1 };
        s.HackWallhack = s.HackAimbot = s.HackAmmo = false;
        Assert.Equal("none", Arg(s.HackerCommands(), "lst_hack_powers"));
        Assert.True(Enum.TryParse(PowerType, "none", ignoreCase: true, out var power));
        Assert.Equal(0, (int)power!); // the plugin reads it as no powers
    }

    [Fact]
    public void EveryAimChoiceParses()
    {
        var s = Everything();
        for (var i = 0; i < ServerSettings.AimKeys.Length; i++)
        {
            s.HackAimKey = i;
            Assert.Equal(i, Convert.ToInt32(Parse("ParseAimKey", Arg(s.HackerCommands(), "lst_aim_key"))));
        }
        for (var i = 0; i < ServerSettings.AimStyles.Length; i++)
        {
            s.HackAimStyle = i;
            Assert.Equal(i, Convert.ToInt32(Parse("ParseAimStyle", Arg(s.HackerCommands(), "lst_aim_style"))));
        }
        for (var i = 0; i < ServerSettings.AimBones.Length; i++)
        {
            s.HackAimBone = i;
            Assert.Equal(i, Convert.ToInt32(Parse("ParseAimBone", Arg(s.HackerCommands(), "lst_aim_bone"))));
        }
        for (var i = 0; i < ServerSettings.AimTargets.Length; i++)
        {
            s.HackAimTarget = i;
            Assert.Equal(i, Convert.ToInt32(Parse("ParseAimTarget", Arg(s.HackerCommands(), "lst_aim_target"))));
        }
    }

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "all")]
    [InlineData(2, "one")]
    public void ModeCommand(int mode, string arg)
    {
        Assert.Equal(arg, Arg(new ServerSettings { HackerMode = mode }.HackerCommands(), "lst_hack"));
    }

    [Fact]
    public void LiveChangesKeepCheatsAsTheCfgDoes()
    {
        // No recoil / no spread are cheat cvars: they are set inside sv_cheats 1, and cheats stay on for them.
        var s = new ServerSettings { AllowCheats = false, HackerMode = 1, HackNoRecoil = true, HackNoSpread = true };
        var lines = s.HackerCommands().ToList();
        Assert.Equal("sv_cheats 1", lines[0]);
        Assert.Contains("weapon_accuracy_nospread 1", lines);
        Assert.Contains("weapon_recoil_scale 0", lines);
        Assert.Equal("sv_cheats 1", lines.Last(l => l.StartsWith("sv_cheats")));

        s.HackerMode = 0; // hacker mode off: the server-wide cvars go back to normal
        lines = s.HackerCommands().ToList();
        Assert.Contains("weapon_accuracy_nospread 0", lines);
        Assert.Contains("weapon_recoil_scale 2", lines);
        Assert.Equal("sv_cheats 0", lines.Last(l => l.StartsWith("sv_cheats")));
    }
}
