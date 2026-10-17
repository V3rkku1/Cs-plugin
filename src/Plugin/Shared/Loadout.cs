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

    /// <summary>Up to 5 stickers (slot 0-4).</summary>
    public List<StickerSettings> Stickers { get; set; } = new();

    /// <summary>Weapon charm, or null for none.</summary>
    public KeychainSettings? Keychain { get; set; }

    /// <summary>True when there is anything to put on the weapon besides the plain default look.</summary>
    [JsonIgnore]
    public bool HasCustomization =>
        Paint != 0 || Stickers.Count > 0 || Keychain != null || StatTrak || Nametag != "";
}

public class StickerSettings
{
    public int Slot { get; set; }
    public int Id { get; set; }

    /// <summary>0 = brand new, 1 = scraped away.</summary>
    public float Wear { get; set; }

    public float Scale { get; set; } = 1f;
    public float Rotation { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
}

public class KeychainSettings
{
    public int Id { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float OffsetZ { get; set; }

    /// <summary>Charm pattern ("Highlight" charms use it to pick the clip).</summary>
    public int Seed { get; set; }
}

public class PlayerLoadout
{
    /// <summary>Weapon/knife/glove definition index -> skin.</summary>
    public Dictionary<ushort, SkinSettings> Skins { get; set; } = new();

    /// <summary>Per-side skins: they win over <see cref="Skins"/> for players on that side (2 = T, 3 = CT).</summary>
    public Dictionary<ushort, SkinSettings> SkinsT { get; set; } = new();
    public Dictionary<ushort, SkinSettings> SkinsCT { get; set; } = new();

    public SkinSettings? SkinFor(ushort def, int team) =>
        (team == 2 ? SkinsT : team == 3 ? SkinsCT : null)?.GetValueOrDefault(def) ?? Skins.GetValueOrDefault(def);

    /// <summary>Selected knife definition index (0 = default team knife).</summary>
    public ushort Knife { get; set; }

    /// <summary>Selected gloves definition index (0 = default gloves).</summary>
    public ushort Gloves { get; set; }

    /// <summary>Agent model per side ("agents/models/....vmdl"), or null for the default agent.</summary>
    public string? AgentT { get; set; }
    public string? AgentCT { get; set; }

    /// <summary>Collectible pin shown on the scoreboard (its definition index, 0 = none).</summary>
    public ushort Pin { get; set; }

    /// <summary>Music kit (its definition index, 0 = the game's default): round music and your MVP anthem.</summary>
    public ushort MusicKit { get; set; }

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

    /// <summary>Key used for everyone when the plugin runs in single-loadout mode (also what the launcher edits).</summary>
    public const string LocalKey = "local";

    private readonly string _path;
    private Dictionary<string, PlayerLoadout> _loadouts = new();
    private DateTime _lastWriteUtc;

    public LoadoutStore(string path)
    {
        _path = path;
        if (!File.Exists(_path)) return;
        if (!TryLoad())
            File.Move(_path, _path + ".broken", overwrite: true);
    }

    /// <summary>True when another program (the launcher) changed the file since we last read or wrote it.</summary>
    public bool ChangedOnDisk() => File.Exists(_path) && File.GetLastWriteTimeUtc(_path) != _lastWriteUtc;

    /// <summary>Re-reads the file. Keeps the current loadouts if the file can't be parsed.</summary>
    public bool TryLoad()
    {
        try
        {
            var stamp = File.GetLastWriteTimeUtc(_path);
            _loadouts = JsonSerializer.Deserialize<Dictionary<string, PlayerLoadout>>(File.ReadAllText(_path), JsonOptions) ?? new();
            _lastWriteUtc = stamp;
            return true;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return false;
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

    // Write to a temp file and swap it in, so the other side never reads a half-written file.
    public void Save()
    {
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_loadouts, JsonOptions));
        File.Move(temp, _path, overwrite: true);
        _lastWriteUtc = File.GetLastWriteTimeUtc(_path);
    }
}
