using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>
/// Trade Up Contracts, with CS2's rules:
/// <list type="bullet">
/// <item>10 guns of the same grade (Consumer to Classified), all StatTrak or all normal, no souvenirs.
/// You get one item of the next grade.</item>
/// <item>Or 5 Covert guns for a knife or gloves (October 2024): the rare items of their cases.</item>
/// <item>Odds: every input adds an equal share to its own collection; within a collection each item of
/// the next grade is equally likely. So 7 inputs from one collection make it 70% likely.</item>
/// <item>Float (CS2's current formula): each input's float is put on its own skin's range (0 = its
/// best, 1 = its worst), those are averaged, and the result lands at that point of the new skin's range.</item>
/// </list>
/// A collection here is a container's contents (cases and souvenir packages), which is what the item data has.
/// </summary>
public partial class Api
{
    private static readonly string[] TradeLadder =
        ["Consumer Grade", "Industrial Grade", "Mil-Spec Grade", "Restricted", "Classified", "Covert"];

    public record TradeOutcome(string Name, string Image, string Color, string Grade, double Chance, float Float, string Wear, double? Price, string Collection);
    public record TradeCheck(bool Ok, string Message, int Needed, string? Grade, bool? StatTrak, List<TradeOutcome> Outcomes,
        double? InputValue, double? ExpectedValue);
    public record TradeArgs(List<string> Uids);
    public record TradeResult(InventoryView Item, List<TradeOutcome> Outcomes);

    private Dictionary<(ushort, int), CaseInfo>? _collectionOf;
    private ItemDatabase? _collectionDb;

    /// <summary>The container (collection) a skin comes from: a weapon case first, else a souvenir package.</summary>
    private CaseInfo? CollectionOf(ushort def, int paint)
    {
        // Rebuilt when the item lists were updated.
        if (_collectionOf == null || !ReferenceEquals(_collectionDb, App.Db))
        {
            _collectionDb = App.Db;
            var map = new Dictionary<(ushort, int), CaseInfo>();
            foreach (var box in App.Db.Cases.Where(c => c.Type == "Case").Concat(App.Db.Cases.Where(c => c.Type.StartsWith("Souvenir"))))
                foreach (var item in box.Items)
                    if (App.Db.WeaponOfItem(item.Name) is { } d) map.TryAdd((d, item.Paint), box);
            _collectionOf = map;
        }
        return _collectionOf.GetValueOrDefault((def, paint));
    }

    private (TradeCheck Check, List<(CaseItem Item, CaseInfo Box, double Chance, bool Rare)> Pool, List<InventoryItem> Inputs) PlanTrade(List<string> uids)
    {
        var inv = Inventory();
        var inputs = uids.Distinct().Select(u => inv.FirstOrDefault(i => i.Uid == u)).OfType<InventoryItem>().ToList();
        TradeCheck Fail(string msg, int needed = 10) => new(false, msg, needed, inputs.FirstOrDefault()?.Grade, inputs.FirstOrDefault()?.StatTrak, [], null, null);
        var empty = new List<(CaseItem, CaseInfo, double, bool)>();
        if (inputs.Count == 0) return (Fail("Add 10 guns of the same grade (or 5 Covert guns for a knife or gloves)."), empty, inputs);

        var grade = inputs[0].Grade;
        var covert = grade == "Covert";
        var needed = covert ? 5 : 10;
        if (inputs.Any(i => i.Kind != "skin" || i.Def == 0 || i.Paint == 0 || IsKnifeType(i.Def) || IsGloveType(i.Def)))
            return (Fail("Only guns with a finish can go in a contract (no knives, gloves, stickers or vanilla items).", needed), empty, inputs);
        if (inputs.Any(i => i.Souvenir)) return (Fail("Souvenir items can't be traded up.", needed), empty, inputs);
        if (inputs.Any(i => i.Grade != grade)) return (Fail("Every item must be the same grade.", needed), empty, inputs);
        if (inputs.Any(i => i.StatTrak != inputs[0].StatTrak)) return (Fail("Use all StatTrak™ items or none.", needed), empty, inputs);
        if (Array.IndexOf(TradeLadder, grade) < 0) return (Fail($"{grade} items can't be traded up.", needed), empty, inputs);

        // Each input's share goes to its own collection's next grade (or, for Coverts, its case's knives / gloves).
        var pool = new List<(CaseItem Item, CaseInfo Box, double Chance, bool Rare)>();
        var next = covert ? "Rare" : TradeLadder[Array.IndexOf(TradeLadder, grade) + 1];
        foreach (var input in inputs)
        {
            var box = CollectionOf(input.Def, input.Paint);
            if (box == null) return (Fail($"{input.Name}: its collection isn't known.", needed), empty, inputs);
            var outs = covert ? box.Rare.GroupBy(r => r.Name).Select(g => g.First()).ToList() : box.Items.Where(x => x.Rarity == next).ToList();
            if (outs.Count == 0)
                return (Fail(covert ? $"{input.Name}: its collection has no knives or gloves." : $"{input.Name}: its collection has no {next} item, so it can't go higher.", needed), empty, inputs);
            foreach (var o in outs) pool.Add((o, box, 1.0 / inputs.Count / outs.Count, covert));
        }

        var outcomes = Outcomes(pool, inputs);
        double? inputValue = inputs.All(i => PriceOf(i) != null) ? inputs.Sum(i => PriceOf(i) ?? 0) : null;
        double? expected = outcomes.All(o => o.Price != null) ? outcomes.Sum(o => o.Chance * (o.Price ?? 0)) : null;
        var ok = inputs.Count == needed;
        var message = ok ? "Ready to sign." : $"{inputs.Count}/{needed} items.";
        return (new TradeCheck(ok, message, needed, grade, inputs[0].StatTrak, outcomes,
            inputValue is { } iv ? Math.Round(iv, 2) : null, expected is { } ev ? Math.Round(ev, 2) : null), pool, inputs);
    }

    /// <summary>CS2's float: the average of where each input sits in its own range, put on the new skin's range.</summary>
    private float TradeFloat(List<InventoryItem> inputs, ushort def, int paint)
    {
        var normalized = inputs.Average(i =>
        {
            var s = App.Db.Find(i.Def, i.Paint);
            var min = s?.MinFloat ?? 0f;
            var max = s?.MaxFloat ?? 1f;
            return max > min ? Math.Clamp((i.Wear - min) / (max - min), 0f, 1f) : 0f;
        });
        var target = App.Db.Find(def, paint);
        var tMin = target?.MinFloat ?? 0f;
        var tMax = target?.MaxFloat ?? 1f;
        return (float)Math.Clamp(tMin + normalized * (tMax - tMin), 0.000001, 0.999999);
    }

    private List<TradeOutcome> Outcomes(List<(CaseItem Item, CaseInfo Box, double Chance, bool Rare)> pool, List<InventoryItem> inputs)
    {
        var st = inputs.Count > 0 && inputs[0].StatTrak;
        return pool.GroupBy(p => p.Item.Name)
            .Select(g =>
            {
                var first = g.First();
                var def = App.Db.WeaponOfItem(first.Item.Name) ?? 0;
                var wear = first.Item.Paint != 0 ? TradeFloat(inputs, def, first.Item.Paint) : 0f;
                var market = PriceList.MarketName(first.Item.Name, true, first.Item.Paint != 0, wear, st, false);
                return new TradeOutcome(first.Item.Name, first.Item.Image, first.Item.Color, first.Item.Rarity, g.Sum(x => x.Chance),
                    wear, first.Item.Paint != 0 ? PriceList.WearName(wear) : "", PricesNow().Price(market), first.Box.Name);
            })
            .OrderByDescending(o => o.Chance).ThenBy(o => o.Name)
            .ToList();
    }

    [Call("tradeup.check")]
    private TradeCheck CheckTrade(TradeArgs a) => PlanTrade(a.Uids ?? new List<string>()).Check;

    /// <summary>Signs the contract: the inputs (and any stickers on them) are gone, the new item is in the inventory.</summary>
    [Call("tradeup.sign")]
    private TradeResult SignTrade(TradeArgs a)
    {
        var (check, pool, inputs) = PlanTrade(a.Uids ?? new List<string>());
        if (!check.Ok) throw new ApiException(check.Message);

        var pick = CaseRoller.Pick(pool, p => p.Chance, Random.Shared.NextDouble());
        // Knives: a Doppler finish rolls its phase like a case does.
        var drop = pick.Rare ? CaseRoller.Roll(pick.Box with { Items = [], Rare = pick.Box.Rare.Where(r => r.Name == pick.Item.Name).ToList() }, Random.Shared).Item : pick.Item;
        var def = App.Db.WeaponOfItem(drop.Name) ?? 0;
        var wear = drop.Paint != 0 ? TradeFloat(inputs, def, drop.Paint) : 0f;
        var gloves = drop.Name.Contains("Gloves") || drop.Name.Contains("Wraps");
        var item = new InventoryItem(NewUid(), "skin", drop.Name.Replace("StatTrak™ ", ""), drop.Image, drop.Rarity, drop.Color,
            def, drop.Paint, wear, Random.Shared.Next(0, 1001), inputs[0].StatTrak && !gloves, false, drop.Phase, "Trade Up Contract", 0, DateTime.UtcNow, drop.Id);

        var used = inputs.Select(i => i.Uid).ToHashSet();
        Inventory().RemoveAll(i => used.Contains(i.Uid));
        Inventory().Insert(0, item);
        SaveInventory();
        App.NotifyChanged();
        return new TradeResult(View(item, ReadLoadout()), check.Outcomes);
    }
}
