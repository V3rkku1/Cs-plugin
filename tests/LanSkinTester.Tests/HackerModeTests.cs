using System.Reflection;
using Plugin = global::LanSkinTester.LanSkinTester;

namespace LanSkinTester.Tests;

/// <summary>The Hacker Mode page's settings must mean the same thing in the plugin.</summary>
public class HackerModeTests
{
    private static readonly Type PowerType = typeof(Plugin).GetNestedType("Power", BindingFlags.NonPublic)!;

    private static object? Parse(string method, string text) =>
        typeof(Plugin).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [text]);

    [Fact]
    public void EveryPowerNameIsAPluginPower()
    {
        var names = LauncherCommands.Load().AllPowers.Split(',');
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
        Assert.Contains("lst_hack_powers none", LauncherCommands.Load().Commands);
        Assert.True(Enum.TryParse(PowerType, "none", ignoreCase: true, out var power));
        Assert.Equal(0, (int)power!); // the plugin reads it as no powers
    }

    [Fact]
    public void EveryAimChoiceParses()
    {
        var c = LauncherCommands.Load();
        foreach (var (method, names) in new[] { ("ParseAimKey", c.AimKeys), ("ParseAimStyle", c.AimStyles), ("ParseAimBone", c.AimBones), ("ParseAimTarget", c.AimTargets) })
        {
            Assert.NotEmpty(names);
            for (var i = 0; i < names.Count; i++) Assert.Equal(i, Convert.ToInt32(Parse(method, names[i])));
        }
    }

    [Theory]
    [InlineData("off")]
    [InlineData("all")]
    [InlineData("one")]
    public void ModeCommandIsWritten(string arg) => Assert.Contains($"lst_hack {arg}", LauncherCommands.Load().Commands);
}
