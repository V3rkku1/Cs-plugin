using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Tests;

public class LauncherLogicTests
{
    [Theory]
    [InlineData("$1,234.56", 1234.56)]
    [InlineData("1.234,56€", 1234.56)]
    [InlineData("0,03€", 0.03)]
    [InlineData("£12.50", 12.5)]
    public void SteamPrices(string text, double expected) => Assert.Equal(expected, PriceList.ParseSteamPrice(text)!.Value, 6);

    [Fact]
    public void NoSteamPrice() => Assert.Null(PriceList.ParseSteamPrice("--"));

    [Fact]
    public void MarketNames()
    {
        Assert.Equal("StatTrak™ AK-47 | Redline (Field-Tested)", PriceList.MarketName("AK-47 | Redline", true, true, 0.2f, true, false));
        Assert.Equal("★ StatTrak™ Karambit | Fade (Factory New)", PriceList.MarketName("★ Karambit | Fade", true, true, 0.01f, true, true));
        Assert.Equal("★ Karambit", PriceList.MarketName("★ Karambit", true, false, 0, false, false));
    }

    private static CaseItem Item(string rarity, string name = "x") => new("skin-" + name, name, 1, rarity, "", "", null);

    [Fact]
    public void WeaponCaseOddsAreValves()
    {
        var box = new CaseInfo("c", "Case", "", "Case",
            [Item("Mil-Spec Grade"), Item("Restricted"), Item("Classified"), Item("Covert")], [Item("Covert", "knife")]);
        var odds = CaseRoller.Odds(box).ToDictionary(o => o.Grade, o => o.Chance);
        Assert.Equal(1.0, odds.Values.Sum(), 9);
        Assert.Equal(0.0026, odds["Rare"], 4);
        Assert.True(odds["Mil-Spec Grade"] > 0.79);
    }

    [Fact]
    public void CapsuleGradesAreFiveTimesRarer()
    {
        var box = new CaseInfo("c", "Capsule", "", "Sticker Capsule", [Item("High Grade"), Item("Remarkable")], []);
        var odds = CaseRoller.Odds(box).ToDictionary(o => o.Grade, o => o.Chance);
        Assert.Equal(5.0, odds["High Grade"] / odds["Remarkable"], 9);
    }

    [Fact]
    public void WearStaysInTheSkinsRange()
    {
        var random = new Random(1);
        for (var i = 0; i < 1000; i++) Assert.InRange(CaseRoller.RollWear(0.06f, 0.8f, random), 0.06f, 0.8f);
    }

    [Theory]
    [InlineData("aim_redline", "Deathmatch")]
    [InlineData("de_cache remake", "Casual")]
    [InlineData("Arms Race Pool", "Arms Race")]
    [InlineData("surf_utopia", null)]
    [InlineData("kz_bhop_hub", null)]
    public void WorkshopModeFromTitle(string title, string? mode) => Assert.Equal(mode, ServerSettings.GuessMode(title, []));

    [Fact]
    public void PasswordsLoseQuotesAndLineBreaks() => Assert.Equal("abquit", ServerSettings.CleanPassword(" a\"b\nquit "));

    [Fact]
    public void SafeWriteReplacesTheFile()
    {
        var file = Path.Combine(Path.GetTempPath(), $"lst-safe-{Guid.NewGuid():N}.json");
        try
        {
            SafeFile.WriteAllText(file, "one");
            SafeFile.WriteAllText(file, "two");
            Assert.Equal("two", File.ReadAllText(file));
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void TradeUpTicketsFollowTheCollections()
    {
        // 7 inputs from a collection with 2 next-grade items, 3 from one with 4: 14 vs 12 tickets.
        var perInput = Enumerable.Repeat<IReadOnlyList<string>>(["a1", "a2"], 7)
            .Concat(Enumerable.Repeat<IReadOnlyList<string>>(["b1", "b2", "b3", "b4"], 3));
        var chance = TradeUpMath.Tickets(perInput).GroupBy(t => t.Item[0]).ToDictionary(g => g.Key, g => g.Sum(t => t.Chance));
        Assert.Equal(14.0 / 26, chance['a'], 9);
        Assert.Equal(12.0 / 26, chance['b'], 9);
    }

    [Fact]
    public void TradeUpFloatUsesEachInputsRange()
    {
        // Both inputs sit halfway in their own ranges: the result is halfway in the new skin's range.
        var wear = TradeUpMath.Float([(0.5f, 0f, 1f), (0.2f, 0.1f, 0.3f)], 0.06f, 0.8f);
        Assert.Equal(0.43f, wear, 4);
    }

    [Fact]
    public void DamagedFileIsSetAside()
    {
        var dir = Directory.CreateTempSubdirectory("lst-aside").FullName;
        try
        {
            var file = Path.Combine(dir, "inventory.json");
            File.WriteAllText(file, "{broken");
            var aside = SafeFile.SetAside(file);
            Assert.False(File.Exists(file));
            Assert.Equal("{broken", File.ReadAllText(aside!));
            Assert.StartsWith("inventory.broken-", Path.GetFileName(aside));
        }
        finally { Directory.Delete(dir, true); }
    }
}
