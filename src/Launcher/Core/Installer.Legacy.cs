namespace LanSkinTester.Launcher.Core;

/// <summary>CS:GO legacy: Metamod 1.12, SourceMod 1.12, the PTaH extension and kgns' weapons/gloves plugins.</summary>
public partial class Installer
{
    public const string SkinPluginPage = "https://github.com/kgns/weapons";

    private const string LegacyMetamodDrop = "https://mms.alliedmods.net/mmsdrop/1.12/";
    private const string SourceModDrop = "https://sm.alliedmods.net/smdrop/1.12/";
    private const string PtahApi = "https://api.github.com/repos/komashchenko/PTaH/releases/latest";
    private const string WeaponsApi = "https://api.github.com/repos/kgns/weapons/releases/latest";
    private const string GlovesApi = "https://api.github.com/repos/kgns/gloves/releases/latest";

    /// <summary>One install step by id: metamod, sourcemod, ptah, weapons or gloves.</summary>
    public async Task InstallLegacyAsync(LegacyGame game, string id)
    {
        var zip = id switch
        {
            "metamod" => await LatestDropAsync(LegacyMetamodDrop, "mmsource", "Metamod 1.12"),
            "sourcemod" => await LatestDropAsync(SourceModDrop, "sourcemod", "SourceMod 1.12"),
            "ptah" => await ReleaseAssetAsync(PtahApi, "PTaH", n => n.Contains("windows", StringComparison.OrdinalIgnoreCase)),
            "weapons" => await ReleaseAssetAsync(WeaponsApi, "Weapons", n => n.StartsWith("weapons") && n.EndsWith(".zip")),
            "gloves" => await ReleaseAssetAsync(GlovesApi, "Gloves", n => n.StartsWith("gloves") && n.EndsWith(".zip")),
            _ => throw new ArgumentException("Unknown step: " + id),
        };
        await Task.Run(() => ExtractAddons(zip, game.Csgo));
        if (id == "sourcemod") game.AllowSkins();
    }

    private async Task<string> LatestDropAsync(string drop, string prefix, string label)
    {
        _log($"Looking up the latest {label} build...");
        var file = (await _http.GetStringAsync($"{drop}{prefix}-latest-windows")).Trim();
        _log($"Downloading {file}...");
        return await DownloadAsync(drop + file);
    }
}
