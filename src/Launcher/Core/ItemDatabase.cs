using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LanSkinTester.Launcher.Core;

public record WeaponInfo(ushort DefIndex, string Name, string Category, string Image);

public record SkinInfo(
    ushort DefIndex, int Paint, string Weapon, string Finish, string? Phase, string Category,
    string Rarity, string RarityColor, float MinFloat, float MaxFloat, string Image, bool Legacy, bool CanStatTrak)
{
    public string DisplayFinish => Phase == null ? Finish : $"{Finish} ({Phase})";
    public string FullName => $"{Weapon} | {DisplayFinish}";
}

public record StickerInfo(int Id, string Name, string Rarity, string RarityColor, string Image, string Effect);

/// <summary>An agent: Team is "T" or "CT", Model the path the server sets on the player.</summary>
public record AgentInfo(string Name, string Team, string Model, string Rarity, string RarityColor, string Image);

public record PinInfo(int Id, string Name, string Rarity, string RarityColor, string Image);

public record MusicKitInfo(int Id, string Name, string Rarity, string RarityColor, string Image);

/// <summary>
/// A container: weapon case, sticker / autograph capsule, souvenir package, music kit box, pins, patches or
/// graffiti. Items are what it drops ("Weapon | Finish", paint kit, rarity); Rare are a case's knives / gloves.
/// </summary>
public record CaseInfo(string Id, string Name, string Image, string Type, List<CaseItem> Items, List<CaseItem> Rare, string? RareImage = null);

/// <summary>Id is the item data's id ("skin-…", "sticker-123", "music_kit-32", "collectible-6110"...).</summary>
public record CaseItem(string Id, string Name, int Paint, string Rarity, string Color, string Image, string? Phase);

/// <summary>
/// Skin, sticker and charm data from ByMykel's CSGO-API (MIT). Downloaded once, then kept as a small
/// cache in %LocalAppData%\LanSkinTester\data. Also writes the plugin's skins\skins.json (names + legacy flags).
/// </summary>
public class ItemDatabase
{
    private const string Api = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/";

    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSkinTester", "data");

    public List<WeaponInfo> Weapons { get; private set; } = new();
    public List<SkinInfo> Skins { get; private set; } = new();
    public List<StickerInfo> Stickers { get; private set; } = new();
    public List<StickerInfo> Charms { get; private set; } = new();
    public List<AgentInfo> Agents { get; private set; } = new();
    public List<PinInfo> Pins { get; private set; } = new();
    public List<MusicKitInfo> MusicKits { get; private set; } = new();
    public List<CaseInfo> Cases { get; private set; } = new();

    public bool IsEmpty => Skins.Count == 0;

    private Dictionary<(ushort, int), SkinInfo> _byPaint = new();
    private Dictionary<int, StickerInfo> _stickersById = new();
    private Dictionary<int, StickerInfo> _charmsById = new();

    public SkinInfo? Find(ushort defIndex, int paint) => _byPaint.GetValueOrDefault((defIndex, paint));
    public StickerInfo? Sticker(int id) => _stickersById.GetValueOrDefault(id);
    public StickerInfo? Charm(int id) => _charmsById.GetValueOrDefault(id);
    public WeaponInfo? Weapon(ushort defIndex) => Weapons.FirstOrDefault(w => w.DefIndex == defIndex);
    public IEnumerable<SkinInfo> SkinsFor(ushort defIndex) => Skins.Where(s => s.DefIndex == defIndex);

    public static ItemDatabase LoadCached(string dir)
    {
        var db = new ItemDatabase();
        try
        {
            db.Weapons = Read<List<WeaponInfo>>(Path.Combine(dir, "weapons.json")) ?? new();
            db.Skins = Read<List<SkinInfo>>(Path.Combine(dir, "skins.json")) ?? new();
            db.Stickers = Read<List<StickerInfo>>(Path.Combine(dir, "stickers.json")) ?? new();
            db.Charms = Read<List<StickerInfo>>(Path.Combine(dir, "charms.json")) ?? new();
            db.Agents = Read<List<AgentInfo>>(Path.Combine(dir, "agents.json")) ?? new();
            db.Pins = Read<List<PinInfo>>(Path.Combine(dir, "pins.json")) ?? new();
            db.MusicKits = Read<List<MusicKitInfo>>(Path.Combine(dir, "musickits.json")) ?? new();
            db.Cases = Read<List<CaseInfo>>(Path.Combine(dir, "cases.json")) ?? new();
            // An older launcher saved cases without their type and pictures: download again to get them.
            if (db.Cases.Any(c => c.Type == null || c.Items.Any(i => i.Id == null))) db.Cases = new();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new ItemDatabase();
        }
        db.Index();
        return db;
    }

    /// <summary>Downloads the full lists and rewrites the cache. Takes ~25 MB of downloads.</summary>
    public static async Task DownloadAsync(HttpClient http, string dir, Action<string> log)
    {
        Directory.CreateDirectory(dir);

        log("Downloading weapon list...");
        var weapons = Convert(await GetArray(http, "base_weapons.json"), ParseWeapon);
        log("Downloading skin list...");
        var skins = Convert(await GetArray(http, "skins.json"), ParseSkin);
        log("Downloading sticker list (big, ~20 MB)...");
        var stickers = Convert(await GetArray(http, "stickers.json"), ParseSticker);
        log("Downloading charm list...");
        var charms = Convert(await GetArray(http, "keychains.json"), ParseSticker);
        log("Downloading agents and pins...");
        var agents = Convert(await GetArray(http, "agents.json"), ParseAgent);
        var pins = Convert(await GetArray(http, "collectibles.json"), ParsePin);
        log("Downloading music kits...");
        var musicKits = Convert(await GetArray(http, "music_kits.json"), ParseMusicKit);
        log("Downloading weapon cases...");
        var cases = Convert(await GetArray(http, "crates.json"), ParseCase);

        Write(Path.Combine(dir, "weapons.json"), weapons);
        Write(Path.Combine(dir, "skins.json"), skins);
        Write(Path.Combine(dir, "stickers.json"), stickers);
        Write(Path.Combine(dir, "charms.json"), charms);
        Write(Path.Combine(dir, "agents.json"), agents);
        Write(Path.Combine(dir, "pins.json"), pins);
        Write(Path.Combine(dir, "musickits.json"), musicKits);
        Write(Path.Combine(dir, "cases.json"), cases);
        log($"Item data ready: {skins.Count} skins, {stickers.Count} stickers, {charms.Count} charms, {agents.Count} agents, " +
            $"{pins.Count} pins, {musicKits.Count} music kits.");
    }

    /// <summary>The plugin reads names + legacy-model flags from its skins folder (cs2-WeaponPaints format).</summary>
    public void WritePluginSkinNames(string skinNamesDir)
    {
        Directory.CreateDirectory(skinNamesDir);
        // Older launcher versions put WeaponPaints files here; one source avoids duplicates.
        foreach (var old in new[] { "skins_en.json", "gloves_en.json" })
            File.Delete(Path.Combine(skinNamesDir, old));

        var list = Skins.Select(s => new
        {
            weapon_defindex = s.DefIndex,
            paint = s.Paint,
            paint_name = s.FullName,
            legacy_model = s.Legacy,
        });
        File.WriteAllText(Path.Combine(skinNamesDir, "skins.json"), JsonSerializer.Serialize(list));
    }

    private void Index()
    {
        _byPaint = new();
        foreach (var s in Skins) _byPaint.TryAdd((s.DefIndex, s.Paint), s);
        _stickersById = Stickers.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        _charmsById = Charms.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
    }

    // ---- ByMykel JSON -> compact records ----

    private static WeaponInfo? ParseWeapon(JsonNode n)
    {
        var def = Int(n["def_index"]);
        var category = n["category"]?["name"]?.GetValue<string>() ?? "";
        if (def == 0 || category is not ("Pistols" or "Rifles" or "SMGs" or "Heavy" or "Knives" or "Gloves" or "Equipment")) return null;
        if (def is 42 or 59 or 5028 or 5029) return null; // default team knives/gloves
        return new WeaponInfo((ushort)def, Str(n["name"]), category, Str(n["image"]));
    }

    private static AgentInfo? ParseAgent(JsonNode n)
    {
        var model = Str(n["model_player"]);
        var team = Str(n["team"]?["id"]) switch { "terrorists" => "T", "counter-terrorists" => "CT", _ => "" };
        if (!model.StartsWith("agents/models/") || team == "") return null;
        return new AgentInfo(Str(n["name"]), team, model, Str(n["rarity"]?["name"]), Str(n["rarity"]?["color"]), Str(n["image"]));
    }

    private static PinInfo? ParsePin(JsonNode n)
    {
        var id = Int(n["def_index"]);
        if (Str(n["type"]) != "Pin" || id is <= 0 or > ushort.MaxValue) return null;
        return new PinInfo(id, Str(n["name"]), Str(n["rarity"]?["name"]), Str(n["rarity"]?["color"]), Str(n["image"]));
    }

    private static CaseInfo? ParseCase(JsonNode n)
    {
        var type = Str(n["type"]);
        if (type == "" || n["contains"] is not JsonArray contains || contains.Count == 0) return null;
        static List<CaseItem> Items(JsonNode? list) => list is JsonArray a
            ? a.Where(x => x != null).Select(x => new CaseItem(Str(x!["id"]), Str(x["name"]), Int(x["paint_index"]), Str(x["rarity"]?["name"]),
                Str(x["rarity"]?["color"]), Str(x["image"]), x["phase"]?.GetValueKind() == JsonValueKind.String ? x["phase"]!.GetValue<string>() : null))
                .Where(i => i.Name != "").ToList()
            : new List<CaseItem>();
        // The case's own "Rare Special Item" picture (the gold card the game shows in the reel).
        var rareImage = Str(n["loot_list"]?["image"]);
        return new CaseInfo(Str(n["id"]), Str(n["name"]), Str(n["image"]), type, Items(contains), Items(n["contains_rare"]), rareImage == "" ? null : rareImage);
    }

    /// <summary>
    /// The plugin's case list (cases\cases.json): each skin with its weapon's definition index and
    /// float range, so !case can open real cases in game.
    /// </summary>
    public void WritePluginCases(string casesDir)
    {
        if (Cases.Count == 0) return;
        Directory.CreateDirectory(casesDir);
        object? Item(CaseItem item)
        {
            if (WeaponOfItem(item.Name) is not { } def) return null;
            var skin = item.Paint != 0 ? Find(def, item.Paint) : null;
            return new { def, paint = item.Paint, name = item.Name.Replace("★", "").Trim(), rarity = item.Rarity, min = skin?.MinFloat ?? 0f, max = skin?.MaxFloat ?? 1f };
        }
        var list = Cases.Where(c => c.Type == "Case").Select(c => new
        {
            name = c.Name,
            items = c.Items.Select(Item).Where(i => i != null).ToList(),
            rare = c.Rare.Select(Item).Where(i => i != null).ToList(),
        }).Where(c => c.items.Count > 0).ToList();
        File.WriteAllText(Path.Combine(casesDir, "cases.json"), JsonSerializer.Serialize(list));
    }

    /// <summary>
    /// Every agent model (agents.txt next to the plugin), so the server precaches them when a map loads.
    /// Switching to a model the server didn't precache can crash the game of whoever sees it.
    /// </summary>
    public void WritePluginAgents(string pluginDir)
    {
        if (Agents.Count == 0) return;
        Directory.CreateDirectory(pluginDir);
        File.WriteAllLines(Path.Combine(pluginDir, "agents.txt"), Agents.Select(a => a.Model).Distinct());
    }

    private Dictionary<string, ushort>? _weaponsByName;

    /// <summary>The weapon (or knife / gloves) a "★ Karambit | Fade" style name is for, or null for stickers and such.</summary>
    public ushort? WeaponOfItem(string itemName)
    {
        _weaponsByName ??= Weapons.GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DefIndex, StringComparer.OrdinalIgnoreCase);
        var name = itemName.Replace("★", "").Replace("StatTrak™", "").Replace("Souvenir", "").Trim();
        var bar = name.IndexOf('|');
        return _weaponsByName.TryGetValue((bar < 0 ? name : name[..bar]).Trim(), out var def) ? def : null;
    }

    /// <summary>Each kit is listed twice, plain and StatTrak ("music_kit-3_st"); the plain one is enough.</summary>
    private static MusicKitInfo? ParseMusicKit(JsonNode n)
    {
        var id = Int(n["def_index"]);
        if (id is <= 0 or > ushort.MaxValue || Str(n["id"]).EndsWith("_st")) return null;
        var name = Str(n["name"]).Replace("StatTrak™ ", "").Replace("Music Kit | ", "");
        return new MusicKitInfo(id, name, Str(n["rarity"]?["name"]), Str(n["rarity"]?["color"]), Str(n["image"]));
    }

    private static SkinInfo? ParseSkin(JsonNode n)
    {
        var def = Int(n["weapon"]?["weapon_id"]);
        var paint = Int(n["paint_index"]);
        if (def == 0 || paint == 0) return null;

        var name = Str(n["name"]).Replace("★ ", "");
        var bar = name.IndexOf('|');
        var weapon = Str(n["weapon"]?["name"]);
        var finish = bar < 0 ? Str(n["pattern"]?["name"]) : name[(bar + 1)..].Trim();
        var phase = n["phase"]?.GetValue<string>();

        return new SkinInfo((ushort)def, paint, weapon, finish, string.IsNullOrEmpty(phase) ? null : phase,
            Str(n["category"]?["name"]), Str(n["rarity"]?["name"]), Str(n["rarity"]?["color"]),
            Float(n["min_float"], 0), Float(n["max_float"], 1), Str(n["image"]),
            n["legacy_model"]?.GetValue<bool>() ?? false, n["stattrak"]?.GetValue<bool>() ?? false);
    }

    private static StickerInfo? ParseSticker(JsonNode n)
    {
        var id = Int(n["def_index"]);
        if (id == 0) return null;
        var name = Str(n["name"]);
        foreach (var prefix in new[] { "Sticker | ", "Charm | " })
            if (name.StartsWith(prefix)) name = name[prefix.Length..];
        return new StickerInfo(id, name, Str(n["rarity"]?["name"]), Str(n["rarity"]?["color"]), Str(n["image"]), Str(n["effect"]));
    }

    private static async Task<JsonArray> GetArray(HttpClient http, string file)
    {
        await using var stream = await http.GetStreamAsync(Api + file);
        return (JsonArray)(await JsonNode.ParseAsync(stream))!;
    }

    private static List<T> Convert<T>(JsonArray array, Func<JsonNode, T?> parse) where T : class =>
        array.Where(n => n != null).Select(n => parse(n!)).Where(x => x != null).Select(x => x!).ToList();

    private static T? Read<T>(string path) => File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;

    private static void Write<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value));

    private static string Str(JsonNode? n) => n?.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : "";

    private static int Int(JsonNode? n) => n?.GetValueKind() switch
    {
        JsonValueKind.Number => n.GetValue<int>(),
        JsonValueKind.String => int.TryParse(n.GetValue<string>(), out var v) ? v : 0,
        _ => 0,
    };

    private static float Float(JsonNode? n, float fallback) => n?.GetValueKind() switch
    {
        JsonValueKind.Number => (float)n.GetValue<double>(),
        JsonValueKind.String => float.TryParse(n.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback,
        _ => fallback,
    };
}
