using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>
/// Cases page: open CS2 containers like in the game (reel, real odds, floats, StatTrak), keep what you
/// unbox in an inventory and equip it on the local server. Every container opens for free. Without the
/// owner's admin code only what was unboxed here can be equipped; the code unlocks every skin.
/// Nothing here touches a Steam inventory.
/// </summary>
public partial class Api
{
    /// <summary>SHA-256 of "lst-cases-v1:" + the code in capitals. Only the owner knows the code.</summary>
    private const string UnlockHash = "3a42e485acc4d2705bd2f66f6a7fd3f2694eb41d0b94b1c149d9ed6c0d3b6aef";

    public record CaseRow2(string Id, string Name, string Image, string Type, int Count, bool Rare);
    public record CasesState(bool DbEmpty, bool Unlocked, int Opened, List<CaseRow2> Cases, int InventoryCount,
        string Currency, double InventoryValue);

    public record ContentItem(string Name, string Image, string Color, string? Phase, double? Price);
    public record ContentGroup(string Grade, string Color, double Chance, List<ContentItem> Items, string? Image = null);
    public record CaseContents(string Id, string Name, string Image, string Type, List<ContentGroup> Groups, string Currency);

    /// <summary>An unboxed item. Def/Paint for weapons, knives and gloves; CollectibleId for pins and music kits.</summary>
    public record InventoryItem(string Uid, string Kind, string Name, string Image, string Grade, string Color,
        ushort Def, int Paint, float Wear, int Seed, bool StatTrak, bool Souvenir, string? Phase, string CaseName,
        int CollectibleId, DateTime Opened, string? ItemId = null, string? Team = null, List<AppliedSticker>? Stickers = null);

    /// <summary>A sticker stuck on a gun: which one, where (slot, offset, rotation, scale) and how scraped.</summary>
    public record AppliedSticker(int Slot, int Id, string Name, string Image, float OffsetX, float OffsetY, float Rotation, float Scale, float Wear);

    public record ReelItem(string Name, string Image, string Color, bool Rare);
    public record OpenResult(List<ReelItem> Reel, int Win, InventoryView Item);

    /// <summary>An inventory item as the pages show it: its price now, whether it's equipped, and its name split like CS2 shows it.</summary>
    public record InventoryView(InventoryItem Item, double? Price, bool Equipped, string MarketName, string Weapon, string Finish, string Category,
        bool EquippedT, bool EquippedCT);
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

    private static string NewUid() => Guid.NewGuid().ToString("N")[..12];

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
        // Newest first, like the game's store.
        var rows = App.Db.Cases
            .Select((c, i) => (c, i))
            .OrderBy(x => x.c.Type == "Case" ? 0 : 1).ThenByDescending(x => x.i)
            .Select(x => new CaseRow2(x.c.Id, x.c.Name, x.c.Image, x.c.Type, x.c.Items.Count + x.c.Rare.Count, x.c.Rare.Count > 0))
            .ToList();
        var value = Inventory().Sum(i => PriceOf(i) ?? 0);
        return new CasesState(App.Db.Cases.Count == 0, unlocked, App.Settings.CasesOpened, rows, Inventory().Count,
            App.Settings.Currency, Math.Round(value, 2));
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
            return new ContentGroup(o.Grade == "Rare" ? "Rare Special Item" : o.Grade, color, o.Chance, shown, o.Grade == "Rare" ? box.RareImage : null);
        }).ToList();
        return new CaseContents(box.Id, box.Name, box.Image, box.Type, groups, App.Settings.Currency);
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
            ? new ReelItem("★ Rare Special Item ★", box.RareImage ?? "", "#e4ae39", true)
            : new ReelItem(item.Name, item.Image, item.Color, false);
        var reel = Enumerable.Range(0, length).Select(i =>
        {
            if (i == win) return Card(drop, rare);
            var (filler, fillerRare) = CaseRoller.Roll(box, random);
            return Card(filler, fillerRare);
        }).ToList();

        var item = MakeItem(box, drop, random);
        Inventory().Insert(0, item);
        if (Inventory().Count > 5000) Inventory().RemoveRange(5000, Inventory().Count - 5000);
        SaveInventory();
        App.Settings.CasesOpened++;
        App.SaveSettings();
        return new OpenResult(reel, win, View(item, ReadLoadout()));
    }

    public record OpenManyArgs(string Id, int Count);

    /// <summary>Open ×5 / ×10: all drops at once, with one save (a save per drop is slow on a big inventory).</summary>
    [Call("cases.openMany")]
    private List<InventoryView> OpenMany(OpenManyArgs a)
    {
        var box = FindCase(a.Id);
        var items = Enumerable.Range(0, Math.Clamp(a.Count, 1, 50))
            .Select(_ => MakeItem(box, CaseRoller.Roll(box, Random.Shared).Item, Random.Shared)).ToList();
        Inventory().InsertRange(0, items);
        if (Inventory().Count > 5000) Inventory().RemoveRange(5000, Inventory().Count - 5000);
        SaveInventory();
        App.Settings.CasesOpened += items.Count;
        App.SaveSettings();
        var loadout = ReadLoadout();
        return items.Select(i => View(i, loadout)).ToList();
    }

    private InventoryItem MakeItem(CaseInfo box, CaseItem drop, Random random)
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
        return new InventoryItem(NewUid(), kind, drop.Name.Replace("StatTrak™ ", ""), drop.Image, drop.Rarity, drop.Color,
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
        App.SaveSettings();
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
                loadout.SkinsT.Remove(item.Def);
                loadout.SkinsCT.Remove(item.Def);
                if (loadout.Knife == item.Def) loadout.Knife = 0;
                if (loadout.Gloves == item.Def) loadout.Gloves = 0;
                break;
            case "pin": loadout.Pin = 0; break;
            case "music": loadout.MusicKit = 0; break;
            case "agent":
                if (loadout.AgentCT == item.ItemId) loadout.AgentCT = null;
                if (loadout.AgentT == item.ItemId) loadout.AgentT = null;
                break;
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
            "skin" => Matches(loadout.Skins, i) && (!IsKnifeType(i.Def) || loadout.Knife == i.Def) && (!IsGloveType(i.Def) || loadout.Gloves == i.Def),
            "pin" => loadout.Pin == i.CollectibleId && i.CollectibleId > 0,
            "music" => loadout.MusicKit == i.CollectibleId && i.CollectibleId > 0,
            "agent" => i.ItemId != null && (loadout.AgentCT == i.ItemId || loadout.AgentT == i.ItemId),
            _ => false,
        };
        var name = i.Name.Replace("★", "").Trim();
        var bar = name.IndexOf('|');
        var weapon = bar < 0 ? (i.Kind == "skin" ? name : KindName(i.Kind)) : name[..bar].Trim();
        var finish = bar < 0 ? (i.Kind == "skin" ? "Vanilla" : name) : name[(bar + 1)..].Trim();
        var category = i.Kind != "skin" ? i.Kind switch { "sticker" or "patch" or "graffiti" => "graphics", _ => "other" }
            : IsKnifeType(i.Def) || IsGloveType(i.Def) ? "special" : "weapons";
        var market = PriceList.MarketName(i.Name, i.Kind == "skin", i.Paint != 0, i.Wear, i.StatTrak, i.Souvenir);
        // A side's own skin wins; otherwise the side wears the shared one.
        bool OnSide(Dictionary<ushort, SkinSettings> side) => i.Kind == "skin" && (side.ContainsKey(i.Def) ? Matches(side, i) : equipped);
        return new InventoryView(i, PricesNow().Price(market), equipped || OnSide(loadout.SkinsT) || OnSide(loadout.SkinsCT), market, weapon, finish, category,
            OnSide(loadout.SkinsT), OnSide(loadout.SkinsCT));
    }

    private static bool Matches(Dictionary<ushort, SkinSettings> skins, InventoryItem i) =>
        skins.TryGetValue(i.Def, out var s) && s.Paint == i.Paint && s.Seed == i.Seed && Math.Abs(s.Wear - (i.Wear > 0 ? i.Wear : 0.01f)) < 0.0000005f;

    public record SideArgs(string Uid, int Team);

    /// <summary>Puts a gun skin on one side only (2 = T, 3 = CT); the other side keeps what it had.</summary>
    [Call("cases.equipSide")]
    private void EquipSide(SideArgs a)
    {
        var item = Inventory().FirstOrDefault(i => i.Uid == a.Uid && i.Kind == "skin" && i.Def != 0) ?? throw new ApiException("That skin isn't in your inventory any more.");
        if (a.Team is not (2 or 3) || IsKnifeType(item.Def) || IsGloveType(item.Def)) { EquipUnboxed(a.Uid); return; }
        var store = new LoadoutStore(RequirePlugin().LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        // The other side keeps the shared skin it had before this one became side-specific.
        if (loadout.Skins.TryGetValue(item.Def, out var shared))
            foreach (var side in new[] { loadout.SkinsT, loadout.SkinsCT }) side.TryAdd(item.Def, shared);
        (a.Team == 2 ? loadout.SkinsT : loadout.SkinsCT)[item.Def] = SkinOf(item);
        store.Save();
        Toast(a.Team == 2 ? "Equipped for Terrorists." : "Equipped for Counter-Terrorists.", "good");
        App.NotifyChanged();
    }

    /// <summary>Loadout tab › Use Default on one side: that side goes back to the default gun, the other keeps what it wears.</summary>
    [Call("cases.unequipSide")]
    private void UnequipSide(SideArgs a)
    {
        var item = Inventory().FirstOrDefault(i => i.Uid == a.Uid && i.Kind == "skin" && i.Def != 0) ?? throw new ApiException("That skin isn't in your inventory any more.");
        if (a.Team is not (2 or 3) || IsKnifeType(item.Def) || IsGloveType(item.Def)) { UnequipUnboxed(a.Uid); return; }
        var store = new LoadoutStore(RequirePlugin().LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        var (side, other) = a.Team == 2 ? (loadout.SkinsT, loadout.SkinsCT) : (loadout.SkinsCT, loadout.SkinsT);
        // The shared skin becomes the other side's own, so it stays there.
        if (loadout.Skins.Remove(item.Def, out var shared)) other.TryAdd(item.Def, shared);
        // An empty (default) skin on this side wins over anything shared.
        side[item.Def] = new SkinSettings();
        store.Save();
        Toast("Default gun on that side.");
        App.NotifyChanged();
    }

    private static string KindName(string kind) => kind switch
    {
        "agent" => "Agent", "sticker" => "Sticker", "patch" => "Patch", "graffiti" => "Graffiti", "music" => "Music Kit", "pin" => "Pin", "charm" => "Charm", _ => "Item",
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
                var skin = SkinOf(item);
                if (skin.HasCustomization || IsKnifeType(item.Def)) loadout.Skins[item.Def] = skin;
                loadout.SkinsT.Remove(item.Def);
                loadout.SkinsCT.Remove(item.Def);
                break;
            case "pin" when item.CollectibleId > 0:
                loadout.Pin = (ushort)item.CollectibleId;
                break;
            case "music" when item.CollectibleId > 0:
                loadout.MusicKit = (ushort)item.CollectibleId;
                break;
            case "agent" when item.ItemId != null:
                if (item.Team == "CT") loadout.AgentCT = item.ItemId;
                else loadout.AgentT = item.ItemId;
                break;
            default:
                throw new ApiException(item.Kind == "sticker" || item.Kind == "patch"
                    ? "Stickers go on a gun: click the gun in your Inventory and press Stickers."
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

    /// <summary>Removes many items at once (Remove Duplicates): one save and one page reload.</summary>
    [Call("cases.removeMany")]
    private int RemoveManyUnboxed(List<string> uids)
    {
        var gone = uids.ToHashSet();
        var removed = Inventory().RemoveAll(i => gone.Contains(i.Uid));
        SaveInventory();
        Toast($"Removed {removed} duplicate{(removed == 1 ? "" : "s")}.", "good");
        App.NotifyChanged();
        return removed;
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
        App.SaveSettings();
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
        App.SaveSettings();
        if (App.Game != null)
        {
            App.Settings.Server.WriteCfg(App.Game);
            SendIfRunning("lst_skin_commands 0");
        }
        Toast("Back to unbox only.");
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Unbox only: what can be equipped without the admin code
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

    // ---------------------------------------------------------------------------------------------
    // Admin: put any item into the inventory (Inventory page › Add Item)
    // ---------------------------------------------------------------------------------------------

    public record CatalogItem(string Kind, string Key, string Name, string Image, string Grade, string Color,
        ushort Def, int Paint, float Min, float Max, bool CanStatTrak, int Id, string? Team, string? Model);

    public record CatalogQuery(string? Kind, string? Search);

    [Call("inventory.catalog")]
    private List<CatalogItem> Catalog(CatalogQuery q)
    {
        RequireAdmin("Adding items");
        var db = App.Db;
        var words = (q.Search ?? "").ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Match(string name) => words.All(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
        IEnumerable<CatalogItem> list = q.Kind switch
        {
            "sticker" => db.Stickers.Where(s => Match(s.Name)).OrderByDescending(s => s.Id)
                .Select(s => new CatalogItem("sticker", $"sticker-{s.Id}", "Sticker | " + s.Name, s.Image, s.Rarity, s.RarityColor, 0, 0, 0, 0, false, s.Id, null, null)),
            "agent" => db.Agents.Where(a => Match(a.Name))
                .Select(a => new CatalogItem("agent", a.Model, a.Name, a.Image, a.Rarity, a.RarityColor, 0, 0, 0, 0, false, 0, a.Team, a.Model)),
            "pin" => db.Pins.Where(p => Match(p.Name))
                .Select(p => new CatalogItem("pin", $"collectible-{p.Id}", p.Name, p.Image, p.Rarity, p.RarityColor, 0, 0, 0, 0, false, p.Id, null, null)),
            "music" => db.MusicKits.Where(m => Match(m.Name))
                .Select(m => new CatalogItem("music", $"music_kit-{m.Id}", "Music Kit | " + m.Name, m.Image, m.Rarity, m.RarityColor, 0, 0, 0, 0, true, m.Id, null, null)),
            _ => db.Skins.Where(s => Match(s.FullName))
                .OrderBy(s => s.Weapon).ThenBy(s => s.DisplayFinish)
                .Select(s => new CatalogItem("skin", $"skin-{s.DefIndex}-{s.Paint}", (IsKnifeType(s.DefIndex) || IsGloveType(s.DefIndex) ? "★ " : "") + s.FullName,
                    s.Image, s.Rarity, s.RarityColor, s.DefIndex, s.Paint, s.MinFloat, s.MaxFloat, s.CanStatTrak, 0, null, null))
                // Vanilla knives too.
                .Concat(db.Weapons.Where(w => w.Category == "Knives" && Match(w.Name))
                    .Select(w => new CatalogItem("skin", $"skin-{w.DefIndex}-0", "★ " + w.Name, w.Image, "Covert", "#eb4b4b", w.DefIndex, 0, 0, 0, true, 0, null, null))),
        };
        return list.Take(180).ToList();
    }

    /// <summary>One catalog entry by its key ("skin-7-44", "sticker-123", an agent model, "collectible-6110", "music_kit-32").</summary>
    private CatalogItem? ResolveCatalog(string kind, string key)
    {
        var db = App.Db;
        var parts = key.Split('-');
        int Num(int i) => int.TryParse(parts.ElementAtOrDefault(i), out var n) ? n : -1;
        switch (kind)
        {
            case "skin":
                if (parts.Length != 3 || !ushort.TryParse(parts[1], out var d)) return null;
                var paint = Num(2);
                var star = IsKnifeType(d) || IsGloveType(d) ? "★ " : "";
                if (paint == 0 && IsKnifeType(d) && db.Weapon(d) is { } knife)
                    return new CatalogItem("skin", key, "★ " + knife.Name, knife.Image, "Covert", "#eb4b4b", d, 0, 0, 0, true, 0, null, null);
                return db.Find(d, paint) is { } s
                    ? new CatalogItem("skin", key, star + s.FullName, s.Image, s.Rarity, s.RarityColor, d, paint, s.MinFloat, s.MaxFloat, s.CanStatTrak, 0, null, null)
                    : null;
            case "sticker":
                return db.Sticker(Num(1)) is { } st
                    ? new CatalogItem("sticker", key, "Sticker | " + st.Name, st.Image, st.Rarity, st.RarityColor, 0, 0, 0, 0, false, st.Id, null, null) : null;
            case "agent":
                return db.Agents.FirstOrDefault(x => x.Model == key) is { } ag
                    ? new CatalogItem("agent", key, ag.Name, ag.Image, ag.Rarity, ag.RarityColor, 0, 0, 0, 0, false, 0, ag.Team, ag.Model) : null;
            case "pin":
                return db.Pins.FirstOrDefault(x => x.Id == Num(1)) is { } pin
                    ? new CatalogItem("pin", key, pin.Name, pin.Image, pin.Rarity, pin.RarityColor, 0, 0, 0, 0, false, pin.Id, null, null) : null;
            case "music":
                return db.MusicKits.FirstOrDefault(x => x.Id == Num(1)) is { } kit
                    ? new CatalogItem("music", key, "Music Kit | " + kit.Name, kit.Image, kit.Rarity, kit.RarityColor, 0, 0, 0, 0, true, kit.Id, null, null) : null;
        }
        return null;
    }

    public record AddItemArgs(string Kind, string Key, float Wear, int Seed, bool StatTrak, bool Souvenir);

    [Call("inventory.add")]
    private InventoryView AddToInventory(AddItemArgs a)
    {
        RequireAdmin("Adding items");
        var c = ResolveCatalog(a.Kind, a.Key);
        if (c == null) throw new ApiException("Unknown item.");
        var wear = c.Kind == "skin" && c.Paint != 0 ? Math.Clamp(a.Wear, Math.Max(c.Min, 0.000001f), Math.Min(c.Max, 0.999999f)) : 0;
        var item = new InventoryItem(NewUid(), c.Kind, c.Name, c.Image, c.Grade, c.Color,
            c.Def, c.Paint, wear, Math.Clamp(a.Seed, 0, 1000), a.StatTrak && c.CanStatTrak, a.Souvenir && c.Kind == "skin", null, "Admin",
            c.Kind is "pin" or "music" ? c.Id : 0, DateTime.UtcNow, c.Kind == "agent" ? c.Model : c.Key, c.Team);
        Inventory().Insert(0, item);
        SaveInventory();
        App.NotifyChanged();
        return View(item, ReadLoadout());
    }

    // ---------------------------------------------------------------------------------------------
    // Stickers on guns (Inventory › a gun › Stickers): like CS2, applying uses up the sticker and
    // scraping it all the way removes it. Up to 5, anywhere on the gun.
    // ---------------------------------------------------------------------------------------------

    private static SkinSettings SkinOf(InventoryItem item) => new()
    {
        Paint = item.Paint,
        Wear = item.Wear > 0 ? item.Wear : 0.01f,
        Seed = item.Seed,
        StatTrak = item.StatTrak,
        Stickers = (item.Stickers ?? new List<AppliedSticker>()).Select(s => new StickerSettings
        {
            Slot = s.Slot, Id = s.Id, Wear = s.Wear, Scale = s.Scale, Rotation = s.Rotation, OffsetX = s.OffsetX, OffsetY = s.OffsetY,
        }).ToList(),
    };

    private static int StickerIdOf(InventoryItem i) =>
        int.TryParse(i.ItemId?.Split('-').ElementAtOrDefault(1), out var id) ? id : 0;

    public record StickerChoice(string Uid, int Id, string Name, string Image, string Color);

    /// <summary>The stickers in the inventory that can go on a gun.</summary>
    [Call("inventory.stickers")]
    private List<StickerChoice> StickerChoices() =>
        Inventory().Where(i => i.Kind == "sticker" && StickerIdOf(i) > 0)
            .Select(i => new StickerChoice(i.Uid, StickerIdOf(i), i.Name, i.Image, i.Color)).ToList();

    public record ApplyStickerArgs(string Gun, string Sticker, int Slot, float OffsetX, float OffsetY, float Rotation, float Scale);

    [Call("inventory.applySticker")]
    private void ApplySticker(ApplyStickerArgs a)
    {
        var gun = GunFor(a.Gun);
        var sticker = Inventory().FirstOrDefault(i => i.Uid == a.Sticker && i.Kind == "sticker") ?? throw new ApiException("That sticker isn't in your inventory any more.");
        var list = (gun.Stickers ?? new List<AppliedSticker>()).ToList();
        if (list.Count >= 5) throw new ApiException("A gun holds up to 5 stickers. Scrape one off first.");
        // The slot the page picked (nearest to where you clicked) when it is free, else the first free one.
        var slot = a.Slot is >= 0 and <= 4 && list.All(s => s.Slot != a.Slot) ? a.Slot : Enumerable.Range(0, 5).First(n => list.All(s => s.Slot != n));
        list.Add(new AppliedSticker(slot, StickerIdOf(sticker), sticker.Name, sticker.Image,
            Math.Clamp(a.OffsetX, -1f, 1f), Math.Clamp(a.OffsetY, -1f, 1f), Math.Clamp(a.Rotation, -180f, 180f), Math.Clamp(a.Scale, 0.5f, 2f), 0f));
        Inventory().Remove(sticker); // used up, like in the game
        ReplaceGun(gun, gun with { Stickers = list });
        Toast("Sticker applied.", "good");
    }

    public record PlaceStickerArgs(string Gun, int Slot, float OffsetX, float OffsetY, float Rotation, float Scale, float Wear);

    /// <summary>Moves, turns, resizes or scrapes a sticker already on the gun. Scraped to 100% it comes off for good.</summary>
    [Call("inventory.placeSticker")]
    private void PlaceSticker(PlaceStickerArgs a)
    {
        var gun = GunFor(a.Gun);
        var list = (gun.Stickers ?? new List<AppliedSticker>()).ToList();
        var at = list.FindIndex(s => s.Slot == a.Slot);
        if (at < 0) throw new ApiException("No sticker in that slot.");
        if (a.Wear >= 1f)
        {
            list.RemoveAt(at);
            Toast("Sticker scraped off.");
        }
        else
        {
            list[at] = list[at] with
            {
                OffsetX = Math.Clamp(a.OffsetX, -1f, 1f), OffsetY = Math.Clamp(a.OffsetY, -1f, 1f),
                Rotation = Math.Clamp(a.Rotation, -180f, 180f), Scale = Math.Clamp(a.Scale, 0.5f, 2f), Wear = Math.Clamp(a.Wear, 0f, 1f),
            };
        }
        ReplaceGun(gun, gun with { Stickers = list });
    }

    private InventoryItem GunFor(string uid)
    {
        var gun = Inventory().FirstOrDefault(i => i.Uid == uid) ?? throw new ApiException("That gun isn't in your inventory any more.");
        if (gun.Kind != "skin" || gun.Def == 0 || IsGloveType(gun.Def) || IsKnifeType(gun.Def))
            throw new ApiException("Stickers only go on guns.");
        return gun;
    }

    /// <summary>Saves the changed gun; when it is equipped, the game gets the new stickers too.</summary>
    private void ReplaceGun(InventoryItem old, InventoryItem updated)
    {
        var index = Inventory().FindIndex(i => i.Uid == old.Uid);
        if (index >= 0) Inventory()[index] = updated;
        SaveInventory();
        if (App.Game is { PluginInstalled: true } game)
        {
            var store = new LoadoutStore(game.LoadoutFile);
            var loadout = store.Get(LoadoutStore.LocalKey);
            // The gun may be on the shared slot and/or one side: update each place it is worn.
            var worn = false;
            foreach (var skins in new[] { loadout.Skins, loadout.SkinsT, loadout.SkinsCT })
                if (Matches(skins, old)) { skins[updated.Def] = SkinOf(updated); worn = true; }
            if (worn) store.Save();
        }
        App.NotifyChanged();
    }
}
