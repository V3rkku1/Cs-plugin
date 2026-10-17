using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Tests;

/// <summary>Downloaded bot plugins: which release file is taken, and loading / parking them together.</summary>
public sealed class InstallerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("lst-game").FullName;
    private readonly GameFolder _game;

    public InstallerTests() => _game = new GameFolder(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void BotImproverTakesTheWindowsPackageWhenRulesUnchangedIsMissing()
    {
        // ed0ard/CS2-Bot-Improver v1.4.6 (the latest release) ships only these.
        string[] v146 = ["CS2BotImprover.zip", "CS2BotImprover_for_Linux.zip", "Panel.v1.4.6.exe"];
        Assert.Equal("CS2BotImprover.zip", Installer.PickAsset(v146, Installer.BotImproverAssets));
    }

    [Fact]
    public void BotImproverPrefersRulesUnchanged()
    {
        string[] names = ["CS2BotImprover.zip", "CS2BotImprover_for_Linux.zip", "CS2BotImprover_rules_unchanged.zip"];
        Assert.Equal("CS2BotImprover_rules_unchanged.zip", Installer.PickAsset(names, Installer.BotImproverAssets));
    }

    [Fact]
    public void BotImproverNeverTakesTheLinuxPackage()
    {
        Assert.Null(Installer.PickAsset(["CS2BotImprover_for_Linux.zip", "Commands.txt"], Installer.BotImproverAssets));
    }

    private string Css(params string[] parts) => Path.Combine([_game.Addons, "counterstrikesharp", .. parts]);

    private void InstallBotImprover()
    {
        // What the Bot Improver package puts in addons/ (Metamod halves + CounterStrikeSharp halves).
        foreach (var vdf in new[] { "BotHider", "BotController", "BotVision" })
        {
            Directory.CreateDirectory(Path.Combine(_game.Addons, "metamod"));
            File.WriteAllText(Path.Combine(_game.Addons, "metamod", vdf + ".vdf"), "\"Metamod Plugin\" {}");
        }
        foreach (var plugin in new[] { "BotHiderImpl", "BotControllerImpl", "BotAI", "BotAimImprover", "NadeSystem", "BotBuy", "BotState", "RoundDamageRecap", "BotRandomizer" })
        {
            Directory.CreateDirectory(Css("plugins", plugin));
            File.WriteAllText(Css("plugins", plugin, plugin + ".dll"), "");
        }
        Directory.CreateDirectory(Path.Combine(_game.Addons, "BotHider"));
        File.WriteAllText(Path.Combine(_game.Addons, "BotHider", "config.json"), """{ "identity_mode": "player", "fake_ping": { "enabled": true } }""");
    }

    [Fact]
    public void BotImproverOffParksBothHalvesOfEveryBotPlugin()
    {
        InstallBotImprover();
        var off = new ServerSettings { BotImprover = false };
        Assert.True(Installer.ExtraPluginsMismatch(_game, off));

        Installer.SetExtraPlugins(_game, off);
        Assert.False(Installer.ExtraPluginsMismatch(_game, off));
        foreach (var vdf in new[] { "BotHider", "BotController", "BotVision" })
            Assert.False(File.Exists(Path.Combine(_game.Addons, "metamod", vdf + ".vdf")), $"{vdf}.vdf still loads");
        // A CounterStrikeSharp half left loaded without its Metamod half calls into nothing.
        foreach (var plugin in new[] { "BotHiderImpl", "BotControllerImpl", "BotAI", "BotState" })
            Assert.False(Directory.Exists(Css("plugins", plugin)), $"{plugin} still loads");
    }

    [Fact]
    public void BotImproverOnLoadsEverythingButSkinsUntilAsked()
    {
        InstallBotImprover();
        Installer.SetExtraPlugins(_game, new ServerSettings { BotImprover = false }); // parked first
        var on = new ServerSettings { BotImprover = true };
        Assert.True(Installer.ExtraPluginsMismatch(_game, on));

        Installer.SetExtraPlugins(_game, on);
        Assert.False(Installer.ExtraPluginsMismatch(_game, on));
        foreach (var plugin in new[] { "BotHiderImpl", "BotControllerImpl", "BotAI", "BotAimImprover", "NadeSystem", "BotBuy", "BotState", "RoundDamageRecap" })
            Assert.True(Directory.Exists(Css("plugins", plugin)), $"{plugin} isn't loaded");
        Assert.False(Directory.Exists(Css("plugins", "BotRandomizer")), "random bot skins load without Bot Skins");

        on.BotSkins = true;
        Assert.True(Installer.ExtraPluginsMismatch(_game, on));
        Installer.SetExtraPlugins(_game, on);
        Assert.True(Directory.Exists(Css("plugins", "BotRandomizer")));
    }

    [Fact]
    public void FreshDownloadWinsOverAnOlderParkedCopy()
    {
        InstallBotImprover();
        Installer.SetExtraPlugins(_game, new ServerSettings { BotImprover = false }); // park the old copy
        // A new download lands in plugins/ while the old copy is still parked.
        Directory.CreateDirectory(Css("plugins", "BotState"));
        File.WriteAllText(Css("plugins", "BotState", "BotState.dll"), "new");

        Installer.SetExtraPlugins(_game, new ServerSettings { BotImprover = true });
        Assert.Equal("new", File.ReadAllText(Css("plugins", "BotState", "BotState.dll")));
        Assert.False(Directory.Exists(Css("lst-parked", "plugins", "BotState")));
    }

    [Fact]
    public void RetakesKeepTheBotFlagForTheRetakesPlugin()
    {
        InstallBotImprover();
        var config = Path.Combine(_game.Addons, "BotHider", "config.json");

        Installer.SetExtraPlugins(_game, new ServerSettings { BotImprover = true, Mode = ServerSettings.RetakesMode, RetakeStyle = 1 });
        Assert.Contains("\"identity_mode\": \"bot\"", File.ReadAllText(config));
        Assert.Contains("fake_ping", File.ReadAllText(config)); // other settings are kept

        Installer.SetExtraPlugins(_game, new ServerSettings { BotImprover = true, Mode = "Deathmatch" });
        Assert.Contains("\"identity_mode\": \"player\"", File.ReadAllText(config));
    }
}
