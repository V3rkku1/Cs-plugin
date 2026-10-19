using Microsoft.Data.Sqlite;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Puts the launcher's loadout into kgns' CS:GO plugins: their SQLite tables (SourceMod's "storage-local",
/// csgo\addons\sourcemod\data\sqlite\sourcemod-local.sq3), row = the player's Steam2 id.
/// weapons: knife (an index into the class list below, 0 = default) and per class: paint, float, StatTrak,
/// StatTrak count, name tag, seed (one skin for both sides). gloves: group (glove type) / paint / float per side.
/// The tables are made by the plugins on their first start; until then there is nothing to write into.
/// </summary>
public static class KgnsSync
{
    /// <summary>kgns' g_WeaponClasses and g_iWeaponDefIndex (weapons/globals.sp), in their order: the knife column is an index into it.</summary>
    internal static readonly (string Class, ushort Def)[] Classes =
    [
        ("awp", 9), ("ak47", 7), ("m4a1", 16), ("m4a1_silencer", 60), ("deagle", 1), ("usp_silencer", 61), ("hkp2000", 32), ("glock", 4), ("elite", 2),
        ("p250", 36), ("cz75a", 63), ("fiveseven", 3), ("tec9", 30), ("revolver", 64), ("nova", 35), ("xm1014", 25), ("mag7", 27), ("sawedoff", 29),
        ("m249", 14), ("negev", 28), ("mp9", 34), ("mac10", 17), ("mp7", 33), ("ump45", 24), ("p90", 19), ("bizon", 26), ("famas", 10), ("galilar", 13),
        ("ssg08", 40), ("aug", 8), ("sg556", 39), ("scar20", 38), ("g3sg1", 11), ("knife_karambit", 507), ("knife_m9_bayonet", 508), ("bayonet", 500),
        ("knife_survival_bowie", 514), ("knife_butterfly", 515), ("knife_flip", 505), ("knife_push", 516), ("knife_tactical", 509), ("knife_falchion", 512), ("knife_gut", 506),
        ("knife_ursus", 519), ("knife_gypsy_jackknife", 520), ("knife_stiletto", 522), ("knife_widowmaker", 523), ("mp5sd", 23), ("knife_css", 503), ("knife_cord", 517),
        ("knife_canis", 518), ("knife_outdoor", 521), ("knife_skeleton", 525),
    ];

    /// <summary>"STEAM_1:Y:Z", what GetClientAuthId(AuthId_Steam2) gives in CS:GO, from a Steam account id.</summary>
    public static string Steam2(uint accountId) => $"STEAM_1:{accountId & 1}:{accountId >> 1}";

    public static string DatabaseFile(LegacyGame game) => Path.Combine(game.SourceMod, "data", "sqlite", "sourcemod-local.sq3");

    /// <summary>Writes the loadout. Returns which tables were there to write into (weapons, gloves).</summary>
    public static (bool Weapons, bool Gloves) Write(string databaseFile, string steamId, PlayerLoadout l)
    {
        if (!File.Exists(databaseFile)) return (false, false);
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databaseFile, Mode = SqliteOpenMode.ReadWrite, DefaultTimeout = 5 }.ToString());
        db.Open();
        bool Has(string table)
        {
            using var c = db.CreateCommand();
            c.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $t";
            c.Parameters.AddWithValue("$t", table);
            return c.ExecuteScalar() != null;
        }
        var weapons = Has("weapons");
        var gloves = Has("gloves");
        using var tx = db.BeginTransaction();
        if (weapons)
        {
            using var c = db.CreateCommand();
            var sets = new List<string>();
            var knife = l.KnifeFor(2);
            var knifeIndex = Array.FindIndex(Classes, x => x.Def == knife);
            sets.Add("knife = $knife");
            c.Parameters.AddWithValue("$knife", knife != 0 && knifeIndex >= 0 ? knifeIndex : 0);
            for (var i = 0; i < Classes.Length; i++)
            {
                var (name, def) = Classes[i];
                // One skin for both sides in kgns: a side's own wins (T first), else the shared one.
                var s = l.SkinFor(def, 2) ?? l.SkinFor(def, 3);
                var on = s is { Paint: not 0 };
                sets.Add($"{name} = $p{i}, {name}_float = $f{i}, {name}_trak = $t{i}, {name}_trak_count = $c{i}, {name}_tag = $n{i}, {name}_seed = $s{i}");
                c.Parameters.AddWithValue($"$p{i}", on ? s!.Paint : 0);
                c.Parameters.AddWithValue($"$f{i}", on ? Math.Clamp(s!.Wear, 0.000001f, 0.999999f) : 0f);
                c.Parameters.AddWithValue($"$t{i}", on && s!.StatTrak ? 1 : 0);
                c.Parameters.AddWithValue($"$c{i}", on ? s!.StatTrakCount : 0);
                c.Parameters.AddWithValue($"$n{i}", s?.Nametag ?? "");
                c.Parameters.AddWithValue($"$s{i}", on ? s!.Seed : -1);
            }
            c.Parameters.AddWithValue("$id", steamId);
            c.CommandText = "INSERT OR IGNORE INTO weapons (steamid) VALUES ($id); UPDATE weapons SET " + string.Join(", ", sets) + " WHERE steamid = $id;";
            c.ExecuteNonQuery();
        }
        if (gloves)
        {
            using var c = db.CreateCommand();
            foreach (var (side, team) in new[] { ("t", 2), ("ct", 3) })
            {
                var g = l.GlovesFor(team);
                var s = g != 0 ? l.SkinFor(g, team) : null;
                var on = s is { Paint: not 0 };
                c.Parameters.AddWithValue($"${side}g", on ? g : 0);
                c.Parameters.AddWithValue($"${side}p", on ? s!.Paint : 0);
                c.Parameters.AddWithValue($"${side}f", on ? Math.Clamp(s!.Wear, 0.000001f, 0.999999f) : 0f);
            }
            c.Parameters.AddWithValue("$id", steamId);
            c.CommandText = "INSERT OR IGNORE INTO gloves (steamid) VALUES ($id); " +
                            "UPDATE gloves SET t_group = $tg, t_glove = $tp, t_float = $tf, ct_group = $ctg, ct_glove = $ctp, ct_float = $ctf WHERE steamid = $id;";
            c.ExecuteNonQuery();
        }
        tx.Commit();
        return (weapons, gloves);
    }
}
