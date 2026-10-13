using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>Named copies of a whole loadout, kept in %LocalAppData%\LanSkinTester\loadouts\NAME.json.</summary>
public static class LoadoutPresets
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSkinTester", "loadouts");

    public static IEnumerable<string> Names() =>
        Directory.Exists(Dir)
            ? Directory.GetFiles(Dir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n).Select(n => n!)
            : [];

    public static void Save(string name, PlayerLoadout loadout)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(PathFor(name), JsonSerializer.Serialize(loadout, Json));
    }

    public static PlayerLoadout? Load(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? JsonSerializer.Deserialize<PlayerLoadout>(File.ReadAllText(path)) : null;
    }

    public static void Delete(string name) => File.Delete(PathFor(name));

    /// <summary>Copies a loadout into the plugin's "local" loadout (what you get in game).</summary>
    public static void Apply(GameFolder game, PlayerLoadout source)
    {
        var store = new LoadoutStore(game.LoadoutFile);
        var target = store.Get(LoadoutStore.LocalKey);
        // Round-trip through JSON so the preset file and the live loadout never share objects.
        var copy = JsonSerializer.Deserialize<PlayerLoadout>(JsonSerializer.Serialize(source))!;
        target.Skins = copy.Skins;
        target.Knife = copy.Knife;
        target.Gloves = copy.Gloves;
        store.Save();
    }

    public static string SafeName(string name)
    {
        // Windows' forbidden characters, on every system, so names behave the same everywhere.
        foreach (var c in Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*")) name = name.Replace(c, '_');
        return name.Trim();
    }

    private static string PathFor(string name) => Path.Combine(Dir, SafeName(name) + ".json");

    /// <summary>A random skin for every weapon, plus a random knife and gloves, with wear inside each skin's range.</summary>
    public static PlayerLoadout Random(ItemDatabase db)
    {
        var rng = System.Random.Shared;
        var loadout = new PlayerLoadout();

        SkinSettings Pick(SkinInfo s) => new()
        {
            Paint = s.Paint,
            Wear = Math.Max(s.MinFloat + (float)rng.NextDouble() * (s.MaxFloat - s.MinFloat), 0.00001f),
            Seed = rng.Next(0, 1001),
        };

        foreach (var weapon in db.Weapons)
        {
            var skins = db.SkinsFor(weapon.DefIndex).ToList();
            if (skins.Count == 0 || weapon.Category is "Knives" or "Gloves" || !ItemData.Weapons.ContainsKey(weapon.DefIndex)) continue;
            loadout.Skins[weapon.DefIndex] = Pick(skins[rng.Next(skins.Count)]);
        }

        foreach (var category in new[] { "Knives", "Gloves" })
        {
            var types = db.Weapons.Where(w => w.Category == category && db.SkinsFor(w.DefIndex).Any()).ToList();
            if (types.Count == 0) continue;
            var type = types[rng.Next(types.Count)];
            var skins = db.SkinsFor(type.DefIndex).ToList();
            loadout.Skins[type.DefIndex] = Pick(skins[rng.Next(skins.Count)]);
            if (category == "Knives") loadout.Knife = type.DefIndex;
            else loadout.Gloves = type.DefIndex;
        }
        return loadout;
    }
}
