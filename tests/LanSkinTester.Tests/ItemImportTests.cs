namespace LanSkinTester.Tests;

/// <summary>!gen codes and inspect links (chat !gen / !inspect, and the launcher's Import).</summary>
public class ItemImportTests
{
    private static SkinSettings Sample() => new()
    {
        Paint = 282,
        Seed = 661,
        Wear = 0.15f,
        StatTrak = true,
        StatTrakCount = 1337,
        Nametag = "LAN",
        Stickers = [new StickerSettings { Slot = 0, Id = 4682, Wear = 0.1f }, new StickerSettings { Slot = 3, Id = 5000 }],
        Keychain = new KeychainSettings { Id = 12, Seed = 7 },
    };

    [Fact]
    public void InspectLinkRoundTrips()
    {
        var link = ItemImport.ToInspectLink(7, Sample());
        Assert.StartsWith("steam://run/730//+csgo_econ_action_preview%20", link);

        Assert.True(ItemImport.TryParse(link, out var item, out var error), error);
        Assert.Equal(7, item.DefIndex);
        Assert.Equal(282, item.Skin.Paint);
        Assert.Equal(661, item.Skin.Seed);
        Assert.Equal(0.15f, item.Skin.Wear, 5);
        Assert.True(item.Skin.StatTrak);
        Assert.Equal(1337, item.Skin.StatTrakCount);
        Assert.Equal("LAN", item.Skin.Nametag);
        Assert.Equal([0, 3], item.Skin.Stickers.Select(s => s.Slot));
        Assert.Equal(12, item.Skin.Keychain?.Id);
        Assert.Equal(7, item.Skin.Keychain?.Seed);
    }

    [Fact]
    public void GenCodeRoundTrips()
    {
        var gen = ItemImport.ToGen(7, Sample());
        Assert.StartsWith("!gen 7 282 661 0.15", gen);
        Assert.True(ItemImport.TryParse(gen, out var item, out var error), error);
        Assert.Equal(7, item.DefIndex);
        Assert.Equal(282, item.Skin.Paint);
        Assert.Equal(661, item.Skin.Seed);
        Assert.Equal(2, item.Skin.Stickers.Count);
        Assert.Equal(12, item.Skin.Keychain?.Id);
    }

    [Theory]
    [InlineData("!gen 7 282 661 0.15")]
    [InlineData("gen 7 282 661 0,15")]
    [InlineData("7 282 661 0.15")]
    [InlineData("!g 7 282 661 0.15 0 0 0 0 0 0 0 0 0 0")]
    public void GenCodesInEveryCommonShape(string code)
    {
        Assert.True(ItemImport.TryParse(code, out var item, out var error), error);
        Assert.Equal(7, item.DefIndex);
        Assert.Equal(0.15f, item.Skin.Wear, 5);
    }

    [Theory]
    [InlineData("steam://rungame/730/76561202255233023/+csgo_econ_action_preview%20M123456A789D012")]
    [InlineData("csgo_econ_action_preview S76561198000000000A123D456")]
    public void OldMarketLinksExplainWhatToPaste(string link)
    {
        Assert.False(ItemImport.TryParse(link, out _, out var error));
        Assert.Equal(ItemImport.OldMarketLinkMessage, error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("!gen 7 282")]
    [InlineData("!gen a b c d")]
    [InlineData("csgo_econ_action_preview 00")]
    [InlineData("csgo_econ_action_preview 0018072A")]
    [InlineData("csgo_econ_action_preview 001807")]
    [InlineData("00000000000000000000")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    public void BadInputIsRefusedWithAReason(string text)
    {
        Assert.False(ItemImport.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void RandomInspectDataNeverThrows()
    {
        // Players paste anything into chat; a throw there only loses the reply, but the launcher's import
        // and the plugin both expect a clean "no".
        var random = new Random(1234);
        for (var i = 0; i < 20000; i++)
        {
            var bytes = new byte[random.Next(6, 64)];
            random.NextBytes(bytes);
            if (random.Next(2) == 0) bytes[0] = 0; // unmasked, so the protobuf reader gets random fields
            var text = "csgo_econ_action_preview " + Convert.ToHexString(bytes);
            var ex = Record.Exception(() => ItemImport.TryParse(text, out _, out _));
            Assert.True(ex == null, $"{text}: {ex}");
        }
    }

    [Fact]
    public void RandomGenCodesNeverThrow()
    {
        var random = new Random(99);
        string[] tokens = ["!gen", "7", "-1", "999999999999", "0.5", "NaN", "1e40", ",", "x", "65535", "65536", "0"];
        for (var i = 0; i < 20000; i++)
        {
            var text = string.Join(' ', Enumerable.Range(0, random.Next(1, 16)).Select(_ => tokens[random.Next(tokens.Length)]));
            var ex = Record.Exception(() => ItemImport.TryParse(text, out _, out _));
            Assert.True(ex == null, $"{text}: {ex}");
        }
    }
}
