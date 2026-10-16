namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Case opening odds as close to CS2 as is publicly known:
/// <list type="bullet">
/// <item>Weapon cases: Mil-Spec 79.92%, Restricted 15.98%, Classified 3.20%, Covert 0.64%, rare special item
/// (knife or gloves) 0.26% - Valve's published odds. The grade is picked first, then an item of that grade.</item>
/// <item>Other containers (capsules, souvenir packages...): each grade five times rarer than the one below,
/// the same steps Valve uses.</item>
/// <item>Knife finishes are equally likely; Doppler and Gamma Doppler phases follow the community-measured
/// split (Phase 1-4 common, Ruby / Sapphire / Black Pearl / Emerald rare).</item>
/// <item>Float: CS2 first picks a wear band (Factory New 3%, Minimal Wear 24%, Field-Tested 33%, Well-Worn
/// 24%, Battle-Scarred 16%), a uniform value in it, then squeezes that into the skin's own float range.
/// Pattern 0-1000. StatTrak 10% in weapon cases (not gloves).</item>
/// </list>
/// </summary>
public static class CaseRoller
{
    public static readonly string[] Grades =
    [
        "Consumer Grade", "Base Grade", "Industrial Grade", "Mil-Spec Grade", "High Grade", "Restricted", "Remarkable",
        "Classified", "Exotic", "Covert", "Extraordinary", "Contraband",
    ];

    private static readonly Dictionary<string, double> WeaponCaseOdds = new()
    {
        ["Mil-Spec Grade"] = 0.7992, ["Restricted"] = 0.1598, ["Classified"] = 0.0320, ["Covert"] = 0.0064,
    };
    public const double RareChance = 0.0026;

    private static readonly (double Low, double High, double Chance)[] WearBands =
        [(0.00, 0.07, 0.03), (0.07, 0.15, 0.24), (0.15, 0.38, 0.33), (0.38, 0.45, 0.24), (0.45, 1.00, 0.16)];

    private static readonly Dictionary<string, double> PhaseWeights = new()
    {
        ["Phase 1"] = 24.25, ["Phase 2"] = 24.25, ["Phase 3"] = 24.25, ["Phase 4"] = 24.25,
        ["Ruby"] = 1.0, ["Sapphire"] = 1.0, ["Black Pearl"] = 1.0, ["Emerald"] = 3.0,
    };

    public static int Rank(string grade) => Math.Max(0, Array.IndexOf(Grades, grade));

    /// <summary>Each grade in the container with its chance (and the rare special item's, last).</summary>
    public static List<(string Grade, double Chance)> Odds(CaseInfo box)
    {
        var grades = box.Items.Select(i => i.Rarity).Distinct().OrderBy(Rank).ToList();
        var rare = box.Rare.Count > 0;
        List<(string, double)> list;
        if (box.Type == "Case" && grades.All(WeaponCaseOdds.ContainsKey))
        {
            list = grades.Select(g => (g, WeaponCaseOdds[g])).ToList();
            if (rare) list.Add(("Rare", RareChance));
        }
        else
        {
            // Five times rarer per grade up; a rare pool counts as one more grade.
            var steps = grades.Count + (rare ? 1 : 0);
            list = grades.Select((g, i) => (g, Math.Pow(5, steps - 1 - i))).ToList();
            if (rare) list.Add(("Rare", 1));
        }
        var total = list.Sum(x => x.Item2);
        return list.Select(x => (x.Item1, x.Item2 / total)).ToList();
    }

    /// <summary>One drop: the item, and whether it came from the rare pool.</summary>
    public static (CaseItem Item, bool Rare) Roll(CaseInfo box, Random random)
    {
        var roll = random.NextDouble();
        var odds = Odds(box);
        var grade = odds[^1].Grade;
        foreach (var (g, chance) in odds)
        {
            roll -= chance;
            if (roll < 0) { grade = g; break; }
        }
        if (grade == "Rare") return (RollRare(box.Rare, random), true);
        var pool = box.Items.Where(i => i.Rarity == grade).ToList();
        return (pool[random.Next(pool.Count)], false);
    }

    /// <summary>Knife / glove finishes are equally likely; then the Doppler phase by its own odds.</summary>
    private static CaseItem RollRare(List<CaseItem> rare, Random random)
    {
        var finishes = rare.GroupBy(i => i.Name).ToList();
        var finish = finishes[random.Next(finishes.Count)].ToList();
        if (finish.Count == 1) return finish[0];
        var weights = finish.Select(i => i.Phase != null && PhaseWeights.TryGetValue(i.Phase, out var w) ? w : 24.25).ToList();
        var pick = random.NextDouble() * weights.Sum();
        for (var i = 0; i < finish.Count; i++)
        {
            pick -= weights[i];
            if (pick < 0) return finish[i];
        }
        return finish[^1];
    }

    /// <summary>A float like CS2 makes one: wear band first, then into the skin's range.</summary>
    public static float RollWear(float min, float max, Random random)
    {
        var roll = random.NextDouble();
        var band = WearBands[^1];
        foreach (var b in WearBands)
        {
            roll -= b.Chance;
            if (roll < 0) { band = b; break; }
        }
        var value = band.Low + random.NextDouble() * (band.High - band.Low);
        return (float)Math.Clamp(min + value * (max - min), 0.000001, 0.999999);
    }
}
