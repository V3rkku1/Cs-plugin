using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// Cases page: open CS2 containers like in the game (reel, real odds, floats, StatTrak), keep what you
/// unbox in an inventory and equip it on the local server. Every container can be opened. Without the
/// owner's admin code the Skins page only offers what was unboxed here; the code unlocks every skin.
/// Nothing here touches a Steam inventory.
/// </summary>
public partial class Api
{
    /// <summary>SHA-256 of "lst-cases-v1:" + the code in capitals. Only the owner knows the code.</summary>
    private const string UnlockHash = "3a42e485acc4d2705bd2f66f6a7fd3f2694eb41d0b94b1c149d9ed6c0d3b6aef";

    public record CaseRow(string Id, string Name, string Image, string Type, int Count, bool Rare);
    public record CaseRow2(string Id, string Name, string Image, string Type, int Count, bool Rare, double? Price);
    public record CasesState(bool DbEmpty, bool Unlocked, int Opened, List<CaseRow2> Cases, int InventoryCount,
        string Currency, double KeyPrice, double Spent, double InventoryValue);

    public record ContentItem(string Name, string Image, string Color, string? Phase, double? Price);
    public record ContentGroup(string Grade, string Color, double Chance, List<ContentItem> Items);
    public record CaseContents(string Id, string Name, string Image, string Type, List<ContentGroup> Groups, double? CasePrice, double KeyPrice, bool NeedsKey, string Currency);

    /// <summary>An unboxed item. Def/Paint for weapons, knives and gloves; CollectibleId for pins and music kits.</summary>
    public record InventoryItem(string Uid, string Kind, string Name, string Image, string Grade, string Color,
        ushort Def, int Paint, float Wear, int Seed, bool StatTrak, bool Souvenir, string? Phase, string CaseName,
        int CollectibleId, DateTime Opened, string? ItemId = null);

    public record ReelItem(string Name, string Image, string Color, bool Rare);
    public record OpenResult(List<ReelItem> Reel, int Win, InventoryView Item, double Cost);

    /// <summary>An inventory item as the pages show it: its price now, whether it's equipped, and its name split like CS2 shows it.</summary>
    public record InventoryView(InventoryItem Item, double? Price, bool Equipped, string MarketName, string Weapon, string Finish, string Category);
    public record InventoryState(List<InventoryView> Items, double Total, string Currency, bool Admin);

    private static string InventoryFile => Path.Combine(AppState.LauncherDataDir, "inventory.json");
    private List<InventoryItem>? _inventory;

    private List<InventoryItem> Inventory()
    {
        if (_inventory != null) return _inventory;
        try { _inventory = File.Exists(InventoryFile) ? JsonSerializer.Deserialize<List<InventoryItem>>(File.ReadAllText(InventoryFile)) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException) { _inventory = new(); }
        return _inventory;
    }

    private void SaveInventory()
    {
        try
        {
            Directory.CreateDirectory(AppState.LauncherDataDir);
            File.WriteAllText(InventoryFile, JsonSerializer.Serialize(Inventory()));
        }
        catch (IOException ex) { App.Log("Could not save the inventory: " + ex.Message); }
    }


    [Call("cases.state")]
    private CasesState CasesStateNow()
    {
        var unlocked = App.Settings.AdminUnlocked;
        var prices = PricesNow();
        // Newest first, like the game's store.
        var rows = App.Db.Cases
            .Select((c, i) => (c, i))
            .OrderBy(x => x.c.Type == "Case" ? 0 : 1).ThenByDescending(x => x.i)
            .Select(x => new CaseRow2(x.c.Id, x.c.Name, x.c.Image, x.c.Type, x.c.Items.Count + x.c.Rare.Count, x.c.Rare.Count > 0, prices.Price(x.c.Name)))
            .ToList();
        var value = Inventory().Sum(i => PriceOf(i) ?? 0);
        return new CasesState(App.Db.Cases.Count == 0, unlocked, App.Settings.CasesOpened, rows, Inventory().Count,
            App.Settings.Currency, PriceList.KeyPrice(App.Settings.Currency), Math.Round(App.Settings.CasesSpent, 2), Math.Round(value, 2));
    }

    private CaseInfo FindCase(string id) =>
        App.Db.Cases.FirstOrDefault(c => c.Id == id) ?? throw new ApiException("That case isn't available.");

    [Call("cases.contents")]
    private CaseContents Contents(string id)
    {
        var box = FindCase(id);
        var groups = CaseRoller.Odds(box).OrderByDescending(o => o.Grade == "Rare" ? 99 : CaseRoller.Rank(o.Grade)).Select(o =>
        {
            var items = o.Grade == "Rare" ? box.Rare : box.Items.Where(i => i.Rarity == o.Grade).ToList();
            // Doppler phases are one finish in the list.
            var shown = items.GroupBy(i => i.Name).Select(g => g.First())
                .Select(i => new ContentItem(i.Name, i.Image, i.Color, null, o.Grade == "Rare" ? null : CheapestPrice(i.Name, box.Type.StartsWith("Souvenir")))).ToList();
            var color = o.Grade == "Rare" ? "#e4ae39" : items.FirstOrDefault()?.Color ?? "#b0c3d9";
            return new ContentGroup(o.Grade == "Rare" ? "Rare Special Item" : o.Grade, color, o.Chance, shown);
        }).ToList();
        return new CaseContents(box.Id, box.Name, box.Image, box.Type, groups, PricesNow().Price(box.Name),
            PriceList.KeyPrice(App.Settings.Currency), NeedsKey(box), App.Settings.Currency);
    }

    [Call("cases.open")]
    private OpenResult OpenCase(string id)
    {
        var box = FindCase(id);
        var random = Random.Shared;
        var (drop, rare) = CaseRoller.Roll(box, random);

        // The reel: what else could have come out, by the same odds. Rare items show as the gold
        // "Rare Special Item" card, like in the game.
        const int length = 56, win = 48;
        ReelItem Card(CaseItem item, bool isRare) => isRare
            ? new ReelItem("★ Rare Special Item ★", box.Image, "#e4ae39", true)
            : new ReelItem(item.Name, item.Image, item.Color, false);
        var reel = Enumerable.Range(0, length).Select(i =>
        {
            if (i == win) return Card(drop, rare);
            var (filler, fillerRare) = CaseRoller.Roll(box, random);
            return Card(filler, fillerRare);
        }).ToList();

        var item = MakeItem(box, drop, rare, random);
        Inventory().Insert(0, item);
        if (Inventory().Count > 5000) Inventory().RemoveRange(5000, Inventory().Count - 5000);
        SaveInventory();
        // What it cost in the game: the container from the market, plus a key for cases.
        var cost = (PricesNow().Price(box.Name) ?? 0) + (NeedsKey(box) ? PriceList.KeyPrice(App.Settings.Currency) : 0);
        App.Settings.CasesOpened++;
        App.Settings.CasesSpent += cost;
        SaveSettingsQuietly();
        return new OpenResult(reel, win, View(item, ReadLoadout()), Math.Round(cost, 2));
    }

    private InventoryItem MakeItem(CaseInfo box, CaseItem drop, bool rare, Random random)
    {
        var kind = drop.Id.Split('-')[0] switch
        {
            "skin" => "skin", "sticker" => "sticker", "music_kit" => "music", "collectible" => "pin",
            "patch" => "patch", "graffiti" => "graffiti", "agent" => "agent", "keychain" => "charm", var other => other,
        };
        ushort def = 0;
        float wear = 0;
        var seed = 0;
        var statTrak = false;
        var souvenir = box.Type.StartsWith("Souvenir");
        if (kind == "skin" && App.Db.WeaponOfItem(drop.Name) is { } weapon)
        {
            def = weapon;
            var skin = drop.Paint != 0 ? App.Db.Find(def, drop.Paint) : null;
            if (drop.Paint != 0) wear = CaseRoller.RollWear(skin?.MinFloat ?? 0f, skin?.MaxFloat ?? 1f, random);
            seed = random.Next(0, 1001);
            var gloves = drop.Name.Contains("Gloves") || drop.Name.Contains("Wraps");
            statTrak = box.Type == "Case" && !gloves && random.NextDouble() < 0.10;
        }
        var collectible = 0;
        if (kind is "pin" or "music")
            int.TryParse(drop.Id.Split('-').ElementAtOrDefault(1)?.Split('_')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out collectible);
        if (kind == "music") statTrak = drop.Id.EndsWith("_st");
        var grade = rare ? drop.Rarity : drop.Rarity;
        return new InventoryItem(Guid.NewGuid().ToString("N")[..12], kind, drop.Name.Replace("StatTrak™ ", ""), drop.Image, grade, drop.Color,
            def, drop.Paint, wear, seed, statTrak, souvenir, drop.Phase, box.Name, collectible, DateTime.UtcNow, drop.Id);
    }

    [Call("cases.inventory")]
    private InventoryState InventoryNow()
    {
        var loadout = ReadLoadout();
        var items = Inventory().Select(i => View(i, loadout)).ToList();
        return new InventoryState(items, Math.Round(items.Sum(v => v.Price ?? 0), 2), App.Settings.Currency, App.Settings.AdminUnlocked);
    }

    [Call("cases.currency")]
    private void SetCurrency(string currency)
    {
        if (!PriceList.Currencies.Contains(currency)) return;
        App.Settings.Currency = currency;
        SaveSettingsQuietly();
        App.NotifyChanged();
    }

    [Call("cases.resetStats")]
    private void ResetCaseStats()
    {
        App.Settings.CasesSpent = 0;
        App.Settings.CasesOpened = 0;
        SaveSettingsQuietly();
        App.NotifyChanged();
    }

    /// <summary>Takes an equipped item off: the gun goes back to default, or the knife / gloves / pin / music kit.</summary>
    [Call("cases.unequip")]
    private void UnequipUnboxed(string uid)
    {
        var item = Inventory().FirstOrDefault(i => i.Uid == uid) ?? throw new ApiException("That item isn't in your inventory any more.");
        var store = new LoadoutStore(RequirePlugin().LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        switch (item.Kind)
        {
            case "skin":
                loadout.Skins.Remove(item.Def);
                if (loadout.Knife == item.Def) loadout.Knife = 0;
                if (loadout.Gloves == item.Def) loadout.Gloves = 0;
                break;
            case "pin": loadout.Pin = 0; break;
            case "music": loadout.MusicKit = 0; break;
        }
        store.Save();
        Toast("Unequipped.");
        App.NotifyChanged();
    }

    private PriceList PricesNow()
    {
        App.Prices.Ensure(App.Settings.Currency);
        return App.Prices;
    }

    /// <summary>Cases need a key; capsules, souvenir packages and boxes open without one.</summary>
    private static bool NeedsKey(CaseInfo box) => box.Type == "Case";

    private double? PriceOf(InventoryItem i) => PricesNow().Price(
        PriceList.MarketName(i.Name, i.Kind == "skin", i.Paint != 0, i.Wear, i.StatTrak, i.Souvenir));

    /// <summary>The cheapest wear of a skin (what the game's case preview would make you think of), or the item's price.</summary>
    private double? CheapestPrice(string name, bool souvenir)
    {
        var prices = PricesNow();
        if (!name.Contains('|') || name.StartsWith("Sticker") || name.StartsWith("Patch") || name.StartsWith("Music Kit"))
            return prices.Price(name);
        var known = new[] { 0.03f, 0.1f, 0.25f, 0.4f, 0.6f }
            .Select(w => prices.Price(PriceList.MarketName(name, true, true, w, false, souvenir)))
            .Where(p => p != null).ToList();
        return known.Count > 0 ? known.Min() : null;
    }

    private InventoryView View(InventoryItem i, PlayerLoadout loadout)
    {
        var equipped = i.Kind switch
        {
            "skin" => loadout.Skins.TryGetValue(i.Def, out var s) && s.Paint == i.Paint && s.Seed == i.Seed && Math.Abs(s.Wear - (i.Wear > 0 ? i.Wear : 0.01f)) < 0.0000005f
                      && (!IsKnifeType(i.Def) || loadout.Knife == i.Def) && (!IsGloveType(i.Def) || loadout.Gloves == i.Def),
            "pin" => loadout.Pin == i.CollectibleId && i.CollectibleId > 0,
            "music" => loadout.MusicKit == i.CollectibleId && i.CollectibleId > 0,
            _ => false,
        };
        var name = i.Name.Replace("★", "").Trim();
        var bar = name.IndexOf('|');
        var weapon = bar < 0 ? (i.Kind == "skin" ? name : KindName(i.Kind)) : name[..bar].Trim();
        var finish = bar < 0 ? (i.Kind == "skin" ? "Vanilla" : name) : name[(bar + 1)..].Trim();
        var category = i.Kind != "skin" ? i.Kind switch { "sticker" or "patch" or "graffiti" => "graphics", _ => "other" }
            : IsKnifeType(i.Def) || IsGloveType(i.Def) ? "special" : "weapons";
        var market = PriceList.MarketName(i.Name, i.Kind == "skin", i.Paint != 0, i.Wear, i.StatTrak, i.Souvenir);
        return new InventoryView(i, PricesNow().Price(market), equipped, market, weapon, finish, category);
    }

    private static string KindName(string kind) => kind switch
    {
        "sticker" => "Sticker", "patch" => "Patch", "graffiti" => "Graffiti", "music" => "Music Kit", "pin" => "Pin", "charm" => "Charm", _ => "Item",
    };

    /// <summary>Puts an unboxed item on: weapon skin, knife or gloves into the loadout; pin or music kit too.</summary>
    [Call("cases.equip")]
    private void EquipUnboxed(string uid)
    {
        var item = Inventory().FirstOrDefault(i => i.Uid == uid) ?? throw new ApiException("That item isn't in your inventory any more.");
        var store = new LoadoutStore(RequirePlugin().LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        switch (item.Kind)
        {
            case "skin" when item.Def != 0:
                if (IsKnifeType(item.Def)) loadout.Knife = item.Def;
                if (IsGloveType(item.Def)) loadout.Gloves = item.Def;
                var skin = new SkinSettings { Paint = item.Paint, Wear = item.Wear > 0 ? item.Wear : 0.01f, Seed = item.Seed, StatTrak = item.StatTrak };
                if (skin.HasCustomization || IsKnifeType(item.Def)) loadout.Skins[item.Def] = skin;
                break;
            case "pin" when item.CollectibleId > 0:
                loadout.Pin = (ushort)item.CollectibleId;
                break;
            case "music" when item.CollectibleId > 0:
                loadout.MusicKit = (ushort)item.CollectibleId;
                break;
            default:
                throw new ApiException(item.Kind == "sticker" || item.Kind == "patch"
                    ? "Stickers go on a gun: open the gun on the Skins page and add it in Stickers and Charm."
                    : "This item can't be equipped.");
        }
        store.Save();
        Toast(TestSession.Cs2Running() ? "Equipped. It changes in game in about a second." : "Equipped. You'll have it next time you play.", "good");
        App.NotifyChanged();
    }

    [Call("cases.remove")]
    private void RemoveUnboxed(string uid)
    {
        Inventory().RemoveAll(i => i.Uid == uid);
        SaveInventory();
        App.NotifyChanged();
    }

    /// <summary>The owner's admin code unlocks every skin on this PC. Wrong codes get no hint.</summary>
    [Call("cases.unlock")]
    private bool UnlockCases(string code)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("lst-cases-v1:" + (code ?? "").Trim().ToUpperInvariant()))).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(UnlockHash)))
        {
            Toast("That code doesn't work.", "bad");
            return false;
        }
        App.Settings.AdminUnlocked = true;
        ServerSettings.SkinCommands = true;
        SaveSettingsQuietly();
        if (App.Game != null)
        {
            App.Settings.Server.WriteCfg(App.Game);
            SendIfRunning("lst_skin_commands 1");
        }
        Toast("Admin: every skin, knife, glove, sticker and agent is unlocked on this PC.", "good");
        App.NotifyChanged();
        return true;
    }

    [Call("cases.lock")]
    private void LockAdmin()
    {
        App.Settings.AdminUnlocked = false;
        ServerSettings.SkinCommands = false;
        SaveSettingsQuietly();
        if (App.Game != null)
        {
            App.Settings.Server.WriteCfg(App.Game);
            SendIfRunning("lst_skin_commands 0");
        }
        Toast("Back to unbox only.");
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Unbox only: what the Skins page may offer without the admin code
    // ---------------------------------------------------------------------------------------------

    private bool UnboxOnly => !App.Settings.AdminUnlocked;

    /// <summary>Weapon / knife / glove finishes unboxed (def, paint). Paint 0 = a vanilla knife.</summary>
    private HashSet<(ushort Def, int Paint)> OwnedSkins() =>
        Inventory().Where(i => i.Kind == "skin" && i.Def != 0).Select(i => (i.Def, i.Paint)).ToHashSet();

    /// <summary>Numbers of unboxed items of a kind ("sticker-123" gives 123; pins and music kits by their id).</summary>
    private HashSet<int> OwnedIds(string kind) =>
        Inventory().Where(i => i.Kind == kind)
            .Select(i => i.CollectibleId > 0 ? i.CollectibleId
                : int.TryParse(i.ItemId?.Split('-').ElementAtOrDefault(1)?.Split('_')[0], out var n) ? n : 0)
            .Where(n => n > 0).ToHashSet();

    private void RequireAdmin(string what)
    {
        if (UnboxOnly) throw new ApiException($"{what} needs the admin code (Cases page). Without it, skins come from opening cases.");
    }
}
