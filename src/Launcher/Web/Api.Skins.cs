using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// Skins page: pick weapons and finishes like in the game's inventory. Equipping writes the plugin's
/// loadouts.json ("local" loadout); the plugin re-skins your weapons within a second.
/// </summary>
public partial class Api
{
    private static readonly string[] SkinCategories = ["Knives", "Gloves", "Pistols", "Rifles", "SMGs", "Heavy"];

    private static readonly (string Name, float Min, float Max)[] WearRanges =
    [
        ("Factory New", 0f, 0.07f), ("Minimal Wear", 0.07f, 0.15f), ("Field-Tested", 0.15f, 0.38f),
        ("Well-Worn", 0.38f, 0.45f), ("Battle-Scarred", 0.45f, 1f),
    ];

    private static readonly Dictionary<string, int> RarityOrder = new()
    {
        ["Contraband"] = 0, ["Extraordinary"] = 1, ["Covert"] = 1, ["Classified"] = 2, ["Restricted"] = 3,
        ["Mil-Spec Grade"] = 4, ["Industrial Grade"] = 5, ["Consumer Grade"] = 6,
    };

    public record SkinWeaponRow(ushort Def, string Name, string Image, string Detail, string? Color, bool Equipped);
    public record SkinCategory(string Name, List<SkinWeaponRow> Weapons);

    /// <param name="DataKey">Changes when the CS2 folder or the item data changes (the page starts over).</param>
    public record SkinsState(string DataKey, bool HasGame, bool PluginInstalled, bool DbEmpty, ushort Knife, ushort Gloves,
        List<SkinCategory> Categories);

    public record FinishInfo(int Paint, string Name, string Rarity, string Color, string Image, float Min, float Max, bool StatTrak, bool Legacy);
    public record AttachmentInfo(int Id, string Name, string Rarity, string Color, string Image);

    public record SkinWeapon(ushort Def, string Name, string Image, string Category, bool IsKnife, bool IsGloves, bool Equipped,
        SkinSettings? Saved, List<FinishInfo> Finishes, List<AttachmentInfo> Stickers, List<AttachmentInfo> Charms);

    [Call("skins.state")]
    private SkinsState SkinsStateNow()
    {
        var db = App.Db;
        var loadout = ReadLoadout();
        var categories = SkinCategories.Select(category =>
        {
            IEnumerable<(ushort Def, string Name, string Image)> rows;
            if (!db.IsEmpty)
            {
                rows = db.Weapons
                    .Where(w => w.Category == category || (category == "Heavy" && w.Category == "Equipment"))
                    .Where(w => category is "Knives" or "Gloves" || ItemData.Weapons.ContainsKey(w.DefIndex))
                    .Select(w => (w.DefIndex, w.Name, w.Image));
            }
            else
            {
                // No item data yet: still allow picking by paint ID.
                rows = category switch
                {
                    "Knives" => ItemData.Knives.Select(k => (k.Value, Pretty(k.Key), "")),
                    "Gloves" => ItemData.Gloves.Select(k => (k.Value, Pretty(k.Key) + " Gloves", "")),
                    "Pistols" => ItemData.DisplayNames.Where(d => ItemData.Pistols.Contains(d.Key)).Select(d => (d.Key, d.Value, "")),
                    "Rifles" => ItemData.DisplayNames.Where(d => !ItemData.Pistols.Contains(d.Key)).Select(d => (d.Key, d.Value, "")),
                    _ => [],
                };
            }

            return new SkinCategory(category, rows.OrderBy(r => r.Name).Select(r =>
            {
                var skin = loadout.Skins.GetValueOrDefault(r.Def);
                var info = skin is { Paint: not 0 } ? db.Find(r.Def, skin.Paint) : null;
                var equippedType = IsKnifeType(r.Def) ? loadout.Knife == r.Def : IsGloveType(r.Def) && loadout.Gloves == r.Def;
                var detail = info != null ? info.DisplayFinish : skin is { Paint: not 0 } ? $"Paint #{skin.Paint}" : equippedType ? "Vanilla" : "Default";
                return new SkinWeaponRow(r.Def, r.Name, info?.Image ?? r.Image, detail, info?.RarityColor, equippedType);
            }).ToList());
        }).ToList();

        var g = App.Game;
        return new SkinsState($"{g?.Root}|{RuntimeHelpers.GetHashCode(db)}", g != null, g?.PluginInstalled == true, db.IsEmpty,
            loadout.Knife, loadout.Gloves, categories);
    }

    [Call("skins.weapon")]
    private SkinWeapon WeaponNow(ushort def)
    {
        var db = App.Db;
        var loadout = ReadLoadout();
        var saved = loadout.Skins.GetValueOrDefault(def);
        var finishes = db.SkinsFor(def)
            .OrderBy(s => RarityOrder.GetValueOrDefault(s.Rarity, 9))
            .ThenBy(s => s.DisplayFinish)
            .Select(s => new FinishInfo(s.Paint, s.DisplayFinish, s.Rarity, s.RarityColor, s.Image, s.MinFloat, s.MaxFloat, s.CanStatTrak, s.Legacy))
            .ToList();
        var equipped = loadout.Skins.ContainsKey(def) || (IsKnifeType(def) && loadout.Knife == def) || (IsGloveType(def) && loadout.Gloves == def);
        return new SkinWeapon(def, WeaponName(def), db.Weapon(def)?.Image ?? "", SkinCategories[CategoryOf(def)],
            IsKnifeType(def), IsGloveType(def), equipped, saved, finishes,
            saved?.Stickers.Select(s => Attachment(db.Sticker(s.Id), s.Id)).ToList() ?? [],
            saved?.Keychain is { } k ? [Attachment(db.Charm(k.Id), k.Id)] : []);
    }

    public record StickerQuery(string? Kind, string? Search, string? Effect, int Limit);
    public record StickerResults(int Total, List<AttachmentInfo> Items);

    /// <summary>Filters the 11,000 stickers (or the charms) here so the page only draws what it shows.</summary>
    [Call("skins.stickers")]
    private StickerResults FindStickers(StickerQuery q)
    {
        var words = (q.Search ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // The data calls paper stickers "Other".
        var effect = q.Effect is null or "All" ? null : q.Effect == "Paper" ? "Other" : q.Effect;
        var matches = (q.Kind == "charm" ? App.Db.Charms : App.Db.Stickers)
            .Where(s => effect == null || s.Effect.Equals(effect, StringComparison.OrdinalIgnoreCase))
            .Where(s => words.All(w => s.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        // Newest first: higher ids are newer releases.
        return new StickerResults(matches.Count, matches.OrderByDescending(s => s.Id).Take(Math.Clamp(q.Limit, 1, 2000))
            .Select(s => Attachment(s, s.Id)).ToList());
    }

    public record EquipArgs(ushort Def, SkinSettings Skin, bool Imported);

    [Call("skins.equip")]
    private void EquipSkin(EquipArgs a)
    {
        var game = RequirePlugin();
        if (a.Def == 0 || a.Skin == null) throw new ApiException("Pick a weapon first.");
        if (IsGloveType(a.Def) && a.Skin.Paint == 0) throw new ApiException("Pick a glove skin first - gloves can't be vanilla.");

        var store = new LoadoutStore(game.LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        if (IsKnifeType(a.Def)) loadout.Knife = a.Def;
        if (IsGloveType(a.Def)) loadout.Gloves = a.Def;
        a.Skin.Nametag = (a.Skin.Nametag ?? "").Trim();
        a.Skin.Stickers ??= new();
        if (a.Skin.HasCustomization) loadout.Skins[a.Def] = a.Skin;
        else loadout.Skins.Remove(a.Def);
        store.Save();

        Toast(a.Imported ? "Imported and equipped."
            : TestSession.Cs2Running() ? "Equipped. It changes in game in about a second (gloves may need a respawn)."
            : "Equipped. You'll have it next time you start.", "good");
        App.NotifyChanged();
    }

    [Call("skins.unequip")]
    private void UnequipSkin(ushort def)
    {
        var store = new LoadoutStore(RequirePlugin().LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        loadout.Skins.Remove(def);
        if (loadout.Knife == def) loadout.Knife = 0;
        if (loadout.Gloves == def) loadout.Gloves = 0;
        store.Save();
        Toast("Back to default.", "good");
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Import and sharing
    // ---------------------------------------------------------------------------------------------

    public record ImportPreview(bool Ok, string Error, string Note, ushort Def, SkinSettings? Skin, string Name, string Image,
        string Color, List<string> Lines);

    /// <summary>A !gen code, an inspect link with item data, or a Steam Market listing name / link.</summary>
    [Call("skins.parse")]
    private ImportPreview ParseItem(string text)
    {
        text = (text ?? "").Trim();
        if (text == "") return new ImportPreview(false, "", "", 0, null, "", "", "", []);
        var db = App.Db;
        if (!ItemImport.TryParse(text, out var item, out var error) && !TryMarketName(text, out item, ref error))
            return new ImportPreview(false, error, "", 0, null, "", "", "", []);

        var info = db.Find(item.DefIndex, item.Skin.Paint);
        var name = info?.FullName ?? $"{db.Weapon(item.DefIndex)?.Name ?? ItemData.NameOf(item.DefIndex)} | paint {item.Skin.Paint}";
        var lines = new List<string>
        {
            $"Float {item.Skin.Wear.ToString("0.00000000", CultureInfo.InvariantCulture)} ({ItemData.WearName(item.Skin.Wear)})   ·   Pattern {item.Skin.Seed}",
        };
        if (item.Skin.Stickers.Count > 0)
            lines.Add("Stickers: " + string.Join(", ", item.Skin.Stickers.OrderBy(s => s.Slot).Select(s => db.Sticker(s.Id)?.Name ?? $"#{s.Id}")));
        if (item.Skin.Keychain != null) lines.Add("Charm: " + (db.Charm(item.Skin.Keychain.Id)?.Name ?? $"#{item.Skin.Keychain.Id}"));
        if (item.Skin.Nametag != "") lines.Add($"Name tag: \"{item.Skin.Nametag}\"");
        return new ImportPreview(true, "", error, item.DefIndex, item.Skin, (item.Skin.StatTrak ? "StatTrak™ " : "") + name,
            info?.Image ?? db.Weapon(item.DefIndex)?.Image ?? "", info?.RarityColor ?? "", lines);
    }

    /// <summary>"StatTrak™ AK-47 | Redline (Field-Tested)" or a steamcommunity.com/market/listings/730/... link.</summary>
    private bool TryMarketName(string text, out ImportedItem item, ref string error)
    {
        item = new ImportedItem();
        var url = Regex.Match(text, @"/market/listings/730/([^?#\s]+)");
        var name = url.Success ? Uri.UnescapeDataString(url.Groups[1].Value) : text;
        if (!name.Contains('|')) return false;

        name = name.Replace("★", "").Trim();
        var statTrak = name.StartsWith("StatTrak™", StringComparison.OrdinalIgnoreCase);
        name = Regex.Replace(name, @"^(StatTrak™|Souvenir)\s*", "", RegexOptions.IgnoreCase).Trim();

        var condition = Regex.Match(name, @"\((Factory New|Minimal Wear|Field-Tested|Well-Worn|Battle-Scarred)\)\s*$");
        if (condition.Success) name = name[..condition.Index].Trim();

        var skin = App.Db.Skins.FirstOrDefault(s => $"{s.Weapon} | {s.Finish}".Equals(name, StringComparison.OrdinalIgnoreCase));
        if (skin == null)
        {
            error = App.Db.IsEmpty ? "Download the skin list first (Setup > Install Everything) to import by name." : $"No skin called \"{name}\" found.";
            return false;
        }

        var range = condition.Success ? WearRanges.First(c => c.Name == condition.Groups[1].Value) : WearRanges[0];
        var min = Math.Max(range.Min, skin.MinFloat);
        var max = Math.Min(range.Max, skin.MaxFloat);
        item.DefIndex = skin.DefIndex;
        item.Skin = new SkinSettings { Paint = skin.Paint, Wear = Math.Max(min + (max - min) * 0.4f, 0.00001f), StatTrak = statTrak };
        error = "Market names don't include the exact float or pattern - a typical value was used. " +
                "For an exact copy paste the listing's !gen code from CSFloat.";
        return true;
    }

    [Call("skins.clipboard")]
    private Task<string> ReadClipboard() => OnUi(() =>
    {
        try { return Clipboard.GetText(); }
        catch (Exception) { return ""; }
    });

    public record CopyItemArgs(ushort Def, SkinSettings Skin, string Kind);

    /// <summary>Copies the item being edited as a !gen code or an inspect link.</summary>
    [Call("skins.copy")]
    private async Task CopyItem(CopyItemArgs a)
    {
        var link = a.Kind == "link";
        var text = link ? ItemImport.ToInspectLink(a.Def, a.Skin) : ItemImport.ToGen(a.Def, a.Skin);
        var ok = await OnUi(() =>
        {
            try { Clipboard.SetText(text); return true; }
            catch (Exception) { return false; }
        });
        if (!ok) throw new ApiException("Could not use the clipboard.");
        Toast(link ? "Inspect link copied." : "!gen code copied.", "good");
    }

    // ---------------------------------------------------------------------------------------------
    // Whole-loadout actions: saved loadouts, randomize, reset
    // ---------------------------------------------------------------------------------------------

    public record LoadoutsInfo(List<string> Names, bool CanRandomize);

    [Call("skins.loadouts")]
    private LoadoutsInfo Loadouts()
    {
        RequirePlugin();
        return new LoadoutsInfo(LoadoutPresets.Names().ToList(), !App.Db.IsEmpty);
    }

    [Call("skins.loadoutSave")]
    private void SaveLoadout(string name)
    {
        RequirePlugin();
        if (LoadoutPresets.SafeName(name ?? "") == "") throw new ApiException("Type a name for the loadout.");
        LoadoutPresets.Save(name!.Trim(), ReadLoadout());
        Toast($"Saved \"{LoadoutPresets.SafeName(name)}\".", "good");
    }

    [Call("skins.loadoutLoad")]
    private void LoadLoadout(string name)
    {
        var game = RequirePlugin();
        var loadout = LoadoutPresets.Load(name) ?? throw new ApiException($"\"{name}\" isn't saved any more.");
        LoadoutPresets.Apply(game, loadout);
        AfterWholeLoadoutChange($"Loaded \"{name}\".");
    }

    [Call("skins.loadoutDelete")]
    private void DeleteLoadout(string name) => LoadoutPresets.Delete(name);

    [Call("skins.randomize")]
    private void RandomizeAll()
    {
        var game = RequirePlugin();
        if (App.Db.IsEmpty) throw new ApiException("Download the skin list first (Setup > Install Everything).");
        LoadoutPresets.Apply(game, LoadoutPresets.Random(App.Db));
        AfterWholeLoadoutChange("Random skins on everything! (Save it from Loadouts if you like it.)");
    }

    /// <summary>The page asks "are you sure" first.</summary>
    [Call("skins.reset")]
    private void ResetAll()
    {
        LoadoutPresets.Apply(RequirePlugin(), new PlayerLoadout());
        AfterWholeLoadoutChange("Everything is back to default.");
    }

    private void AfterWholeLoadoutChange(string message)
    {
        Toast(message + (TestSession.Cs2Running() ? " Changes in game in about a second." : ""), "good");
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------

    private PlayerLoadout ReadLoadout() =>
        App.Game is { } g && Directory.Exists(g.PluginDir)
            ? new LoadoutStore(g.LoadoutFile).Get(LoadoutStore.LocalKey)
            : new PlayerLoadout();

    private GameFolder RequirePlugin() =>
        App.Game is { PluginInstalled: true } g ? g : throw new ApiException("Install the plugin first (Setup page).");

    private static bool IsKnifeType(ushort def) => ItemData.Knives.ContainsValue(def);
    private static bool IsGloveType(ushort def) => ItemData.Gloves.ContainsValue(def);

    private string WeaponName(ushort def) =>
        App.Db.Weapon(def)?.Name ?? ItemData.DisplayNames.GetValueOrDefault(def) ?? Pretty(ItemData.NameOf(def));

    private int CategoryOf(ushort def)
    {
        if (IsKnifeType(def)) return 0;
        if (IsGloveType(def)) return 1;
        var info = App.Db.Weapon(def);
        if (info != null) return Math.Max(0, Array.IndexOf(SkinCategories, info.Category == "Equipment" ? "Heavy" : info.Category));
        return ItemData.Pistols.Contains(def) ? 2 : 3;
    }

    private static AttachmentInfo Attachment(StickerInfo? s, int id) =>
        s == null ? new AttachmentInfo(id, $"#{id}", "", "", "") : new AttachmentInfo(s.Id, s.Name, s.Rarity, s.RarityColor, s.Image);

    private static string Pretty(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
}
