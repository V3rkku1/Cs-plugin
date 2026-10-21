namespace LanSkinTester.Tests;

public class BotSkinsTests
{
    [Fact]
    public void BotGetsASkinOnEveryGunInTheCatalog()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        // AK-47 Redline, Karambit Fade, Sport Gloves Vice; no other guns in the catalog.
        File.WriteAllText(Path.Combine(dir, "skins.json"),
            """[{"weapon_defindex":7,"paint":282},{"weapon_defindex":507,"paint":38},{"weapon_defindex":5030,"paint":10048}]""");
        var loadout = global::LanSkinTester.LanSkinTester.RandomLoadout(new SkinCatalog(dir), new Random(1));

        Assert.Equal(282, loadout.Skins[7].Paint);
        Assert.InRange(loadout.Skins[7].Wear, 0.01f, 0.8f);
        Assert.Contains(loadout.Knife, ItemData.Knives.Values);
        Assert.Contains(loadout.Gloves, ItemData.Gloves.Values);
        Assert.False(loadout.Skins.ContainsKey(1)); // no Deagle skins in the catalog: stays default
    }
}
