namespace LanSkinTester;

/// <summary>
/// Static item definition tables (CS2 item definition indexes from items_game.txt).
/// </summary>
public static class ItemData
{
    /// <summary>Weapon definition index -> classname used with GiveNamedItem.</summary>
    public static readonly Dictionary<ushort, string> Weapons = new()
    {
        [1] = "weapon_deagle",
        [2] = "weapon_elite",
        [3] = "weapon_fiveseven",
        [4] = "weapon_glock",
        [7] = "weapon_ak47",
        [8] = "weapon_aug",
        [9] = "weapon_awp",
        [10] = "weapon_famas",
        [11] = "weapon_g3sg1",
        [13] = "weapon_galilar",
        [14] = "weapon_m249",
        [16] = "weapon_m4a1",
        [17] = "weapon_mac10",
        [19] = "weapon_p90",
        [23] = "weapon_mp5sd",
        [24] = "weapon_ump45",
        [25] = "weapon_xm1014",
        [26] = "weapon_bizon",
        [27] = "weapon_mag7",
        [28] = "weapon_negev",
        [29] = "weapon_sawedoff",
        [30] = "weapon_tec9",
        [31] = "weapon_taser",
        [32] = "weapon_hkp2000",
        [33] = "weapon_mp7",
        [34] = "weapon_mp9",
        [35] = "weapon_nova",
        [36] = "weapon_p250",
        [38] = "weapon_scar20",
        [39] = "weapon_sg556",
        [40] = "weapon_ssg08",
        [60] = "weapon_m4a1_silencer",
        [61] = "weapon_usp_silencer",
        [63] = "weapon_cz75a",
        [64] = "weapon_revolver",
    };

    /// <summary>Knife name (as typed in chat) -> definition index.</summary>
    public static readonly Dictionary<string, ushort> Knives = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bayonet"] = 500,
        ["classic"] = 503,
        ["flip"] = 505,
        ["gut"] = 506,
        ["karambit"] = 507,
        ["m9"] = 508,
        ["huntsman"] = 509,
        ["falchion"] = 512,
        ["bowie"] = 514,
        ["butterfly"] = 515,
        ["daggers"] = 516,
        ["paracord"] = 517,
        ["survival"] = 518,
        ["ursus"] = 519,
        ["navaja"] = 520,
        ["nomad"] = 521,
        ["stiletto"] = 522,
        ["talon"] = 523,
        ["skeleton"] = 525,
        ["kukri"] = 526,
    };

    /// <summary>Glove name (as typed in chat) -> definition index.</summary>
    public static readonly Dictionary<string, ushort> Gloves = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brokenfang"] = 4725,
        ["bloodhound"] = 5027,
        ["sport"] = 5030,
        ["driver"] = 5031,
        ["wraps"] = 5032,
        ["moto"] = 5033,
        ["specialist"] = 5034,
        ["hydra"] = 5035,
    };

    public static readonly HashSet<ushort> Pistols = [1, 2, 3, 4, 30, 32, 36, 61, 63, 64];

    /// <summary>Default team knives (CT = 42, T = 59).</summary>
    public const ushort DefaultKnifeCt = 42;
    public const ushort DefaultKnifeT = 59;

    public static bool IsKnife(ushort defIndex) =>
        defIndex is DefaultKnifeCt or DefaultKnifeT || Knives.ContainsValue(defIndex);

    public static bool IsKnifeClassname(string? designerName) =>
        designerName != null && (designerName.Contains("knife") || designerName.Contains("bayonet"));

    /// <summary>Weapon definition index -> name as shown in game.</summary>
    public static readonly Dictionary<ushort, string> DisplayNames = new()
    {
        [1] = "Desert Eagle", [2] = "Dual Berettas", [3] = "Five-SeveN", [4] = "Glock-18", [7] = "AK-47",
        [8] = "AUG", [9] = "AWP", [10] = "FAMAS", [11] = "G3SG1", [13] = "Galil AR", [14] = "M249",
        [16] = "M4A4", [17] = "MAC-10", [19] = "P90", [23] = "MP5-SD", [24] = "UMP-45", [25] = "XM1014",
        [26] = "PP-Bizon", [27] = "MAG-7", [28] = "Negev", [29] = "Sawed-Off", [30] = "Tec-9", [31] = "Zeus x27",
        [32] = "P2000", [33] = "MP7", [34] = "MP9", [35] = "Nova", [36] = "P250", [38] = "SCAR-20",
        [39] = "SG 553", [40] = "SSG 08", [60] = "M4A1-S", [61] = "USP-S", [63] = "CZ75-Auto", [64] = "R8 Revolver",
    };

    public static string WearName(float wear) => wear switch
    {
        < 0.07f => "Factory New",
        < 0.15f => "Minimal Wear",
        < 0.38f => "Field-Tested",
        < 0.45f => "Well-Worn",
        _ => "Battle-Scarred",
    };

    public static string NameOf(ushort defIndex)
    {
        if (Weapons.TryGetValue(defIndex, out var cls)) return cls.Replace("weapon_", "");
        foreach (var (name, idx) in Knives) if (idx == defIndex) return name;
        foreach (var (name, idx) in Gloves) if (idx == defIndex) return name + " gloves";
        return defIndex is DefaultKnifeCt or DefaultKnifeT ? "default knife" : $"item {defIndex}";
    }
}
