using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanSkinTester;

public class SkinSettings
{
    public int Paint { get; set; }
    public float Wear { get; set; } = 0.01f;
    public int Seed { get; set; }
    public bool StatTrak { get; set; }
    public int StatTrakCount { get; set; }
    public string Nametag { get; set; } = "";

    /// <summary>
    /// Older finishes are made for the "legacy" weapon mesh. Null = use the skins catalog (or false if unknown).
    /// </summary>
    public bool? Legacy { get; set; }
}

public class PlayerLoadout
{
    /// <summary>Weapon/knife/glove definition index -> skin.</summary>
    public Dictionary<ushort, SkinSettings> Skins { get; set; } = new();

    /// <summary>Selected knife definition index (0 = default team knife).</summary>
    public ushort Knife { get; set; }

    /// <summary>Selected gloves definition index (0 = default gloves).</summary>
    public ushort Gloves { get; set; }

    public SkinSettings GetOrCreate(ushort defIndex)
    {
        if (!Skins.TryGetValue(defIndex, out var skin))
        {
            skin = new SkinSettings();
            Skins[defIndex] = skin;
        }
        return skin;
    }
}

/// <summary>
/// Keeps every player's loadout in a single JSON file so skins survive map changes and restarts.
/// </summary>
public class LoadoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private Dictionary<string, PlayerLoadout> _loadouts = new();

    public LoadoutStore(string path)
    {
        _path = path;
        if (!File.Exists(_path)) return;
        try
        {
            _loadouts = JsonSerializer.Deserialize<Dictionary<string, PlayerLoadout>>(File.ReadAllText(_path), JsonOptions) ?? new();
        }
        catch (JsonException)
        {
            File.Move(_path, _path + ".broken", overwrite: true);
        }
    }

    public PlayerLoadout Get(string key)
    {
        if (!_loadouts.TryGetValue(key, out var loadout))
        {
            loadout = new PlayerLoadout();
            _loadouts[key] = loadout;
        }
        return loadout;
    }

    public void Save() => File.WriteAllText(_path, JsonSerializer.Serialize(_loadouts, JsonOptions));
}
