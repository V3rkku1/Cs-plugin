using System.Text.Json;

namespace LanSkinTester;

public record CatalogEntry(ushort DefIndex, int Paint, string Name, bool Legacy);

/// <summary>
/// Optional list of skin names. Drop JSON files in the cs2-WeaponPaints website/data format
/// (skins_en.json, gloves_en.json) into the plugin's skins/ folder to enable name search and
/// automatic legacy-model detection. Without them, skins are applied by paint kit ID only.
/// </summary>
public class SkinCatalog
{
    private readonly List<CatalogEntry> _entries = new();

    public int Count => _entries.Count;

    public SkinCatalog(string directory)
    {
        if (!Directory.Exists(directory)) return;

        foreach (var file in Directory.GetFiles(directory, "*.json"))
            LoadFile(file);
    }

    private void LoadFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!TryGetInt(el, "weapon_defindex", out var def) || !TryGetInt(el, "paint", out var paint) || paint == 0)
                continue;

            var name = el.TryGetProperty("paint_name", out var n) ? n.GetString() ?? "" : "";
            var legacy = el.TryGetProperty("legacy_model", out var l) && l.ValueKind == JsonValueKind.True;
            _entries.Add(new CatalogEntry((ushort)def, paint, name, legacy));
        }
    }

    public CatalogEntry? Find(ushort defIndex, int paint) =>
        _entries.FirstOrDefault(e => e.DefIndex == defIndex && e.Paint == paint);

    /// <summary>
    /// All words in <paramref name="query"/> must appear in the skin name. If the query is exactly
    /// a finish name ("fade"), only those entries are returned so "Marble Fade" doesn't get in the way.
    /// </summary>
    public List<CatalogEntry> Search(string query, ushort? defIndex = null)
    {
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = _entries.Where(e =>
            (defIndex == null || e.DefIndex == defIndex) &&
            words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();

        var exact = matches.Where(e => FinishName(e.Name).Equals(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return exact.Count > 0 ? exact : matches;
    }

    // "★ Karambit | Fade" -> "Fade"
    private static string FinishName(string name)
    {
        var bar = name.IndexOf('|');
        return bar < 0 ? name.Trim() : name[(bar + 1)..].Trim();
    }

    // The upstream file stores paint as a number for some entries and a string for others.
    private static bool TryGetInt(JsonElement el, string prop, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(prop, out var p)) return false;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(p.GetString(), out value),
            _ => false,
        };
    }
}
