using LanSkinTester.Launcher.Core;
using Microsoft.Data.Sqlite;

namespace LanSkinTester.Tests;

/// <summary>The launcher's loadout written into tables laid out like kgns' weapons / gloves plugins make them.</summary>
public class KgnsSyncTests
{
    private static string NewDatabase()
    {
        var file = Path.Combine(Path.GetTempPath(), $"kgns-{Guid.NewGuid():N}.sq3");
        using var db = new SqliteConnection($"Data Source={file}");
        db.Open();
        var cols = string.Concat(KgnsSync.Classes.Select(c =>
            $", {c.Class} int NOT NULL DEFAULT 0, {c.Class}_float decimal(3,2) NOT NULL DEFAULT 0.0, {c.Class}_trak int NOT NULL DEFAULT 0, " +
            $"{c.Class}_trak_count int NOT NULL DEFAULT 0, {c.Class}_tag varchar(256) NOT NULL DEFAULT '', {c.Class}_seed int NOT NULL DEFAULT -1"));
        using var c = db.CreateCommand();
        c.CommandText = $"CREATE TABLE weapons (steamid varchar(32) NOT NULL PRIMARY KEY, knife int NOT NULL DEFAULT 0{cols});" +
                        "CREATE TABLE gloves (steamid varchar(32) NOT NULL PRIMARY KEY, t_group int NOT NULL DEFAULT 0, t_glove int NOT NULL DEFAULT 0, " +
                        "t_float decimal(3,2) NOT NULL DEFAULT 0.0, ct_group int NOT NULL DEFAULT 0, ct_glove int NOT NULL DEFAULT 0, ct_float decimal(3,2) NOT NULL DEFAULT 0.0);";
        c.ExecuteNonQuery();
        return file;
    }

    private static object? Read(string file, string sql)
    {
        using var db = new SqliteConnection($"Data Source={file}");
        db.Open();
        using var c = db.CreateCommand();
        c.CommandText = sql;
        return c.ExecuteScalar();
    }

    [Fact]
    public void LoadoutLandsInTheirColumns()
    {
        var file = NewDatabase();
        var l = new PlayerLoadout { Knife = 515, GlovesT = 5030, GlovesCT = 5027 };
        l.Skins[7] = new SkinSettings { Paint = 44, Wear = 0.07f, Seed = 661, StatTrak = true, Nametag = "Mine" };
        l.Skins[515] = new SkinSettings { Paint = 38, Wear = 0.01f, Seed = 12 };
        l.SkinsT[5030] = new SkinSettings { Paint = 10037, Wear = 0.2f };
        l.SkinsCT[5027] = new SkinSettings { Paint = 10006, Wear = 0.3f };
        var id = KgnsSync.Steam2(123457);
        Assert.Equal("STEAM_1:1:61728", id);

        Assert.Equal((true, true), KgnsSync.Write(file, id, l));
        Assert.Equal(37L, Read(file, $"SELECT knife FROM weapons WHERE steamid = '{id}'")); // knife_butterfly is class 37
        Assert.Equal(44L, Read(file, $"SELECT ak47 FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal(661L, Read(file, $"SELECT ak47_seed FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal(1L, Read(file, $"SELECT ak47_trak FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal("Mine", Read(file, $"SELECT ak47_tag FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal(38L, Read(file, $"SELECT knife_butterfly FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal(0L, Read(file, $"SELECT awp FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal(5030L, Read(file, $"SELECT t_group FROM gloves WHERE steamid = '{id}'"));
        Assert.Equal(10006L, Read(file, $"SELECT ct_glove FROM gloves WHERE steamid = '{id}'"));

        // Taking the AK off puts the column back to the default.
        l.Skins.Remove(7);
        KgnsSync.Write(file, id, l);
        Assert.Equal(0L, Read(file, $"SELECT ak47 FROM weapons WHERE steamid = '{id}'"));
        Assert.Equal(-1L, Read(file, $"SELECT ak47_seed FROM weapons WHERE steamid = '{id}'"));
    }

    [Fact]
    public void NothingToWriteBeforeThePluginsMadeTheirTables()
    {
        Assert.Equal((false, false), KgnsSync.Write(Path.Combine(Path.GetTempPath(), "no-such.sq3"), "STEAM_1:0:1", new PlayerLoadout()));
    }
}
