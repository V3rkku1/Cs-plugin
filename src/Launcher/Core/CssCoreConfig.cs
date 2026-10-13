using System.Text.Json;
using System.Text.Json.Nodes;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// CounterStrikeSharp's "FollowCS2ServerGuidelines" option (on by default) blocks writing the item ID
/// fields skins need, so only the knife model would change. It must be off on the local test server.
/// </summary>
public static class CssCoreConfig
{
    private const string Key = "FollowCS2ServerGuidelines";

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string PathFor(GameFolder game) =>
        Path.Combine(game.Addons, "counterstrikesharp", "configs", "core.json");

    /// <summary>True when skins are blocked (also when core.json doesn't exist yet: the default is on).</summary>
    public static bool BlocksSkins(GameFolder game)
    {
        var path = PathFor(game);
        if (!File.Exists(path)) return true;
        try
        {
            return JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions)?[Key]?.GetValue<bool>() ?? true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>Turns the option off, keeping every other setting. Returns true if the file changed.</summary>
    public static bool AllowSkins(GameFolder game)
    {
        if (!BlocksSkins(game)) return false;

        var path = PathFor(game);
        JsonObject config;
        try
        {
            config = File.Exists(path)
                ? JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException)
        {
            File.Copy(path, path + ".broken", overwrite: true);
            config = new JsonObject();
        }

        config[Key] = false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
