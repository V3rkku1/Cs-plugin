namespace LanSkinTester;

/// <summary>What the plugin reports to the launcher every 2 seconds (plugins/LanSkinTester/status.json).</summary>
public class ServerStatus
{
    public string Version { get; set; } = "";
    public string Map { get; set; } = "";
    public int GameType { get; set; }
    public int GameMode { get; set; }
    public int Humans { get; set; }
    public int Bots { get; set; }
    /// <summary>The server's player slots (0 from plugins older than this field).</summary>
    public int MaxPlayers { get; set; }
    public List<string> Players { get; set; } = new();
    public bool SharedLoadout { get; set; } = true;
    public bool PrivateServer { get; set; }
    public bool SkinsAllowed { get; set; }
    public string DisabledReason { get; set; } = "";

    /// <summary>Kills, deaths, headshots and damage of everyone (bots too) since the map started or lst_stats_reset.</summary>
    public List<PlayerStats> Stats { get; set; } = new();

    /// <summary>Unix time (seconds) of the last write; the launcher treats old files as "server not running".</summary>
    public long Time { get; set; }

    public const string FileName = "status.json";

    /// <summary>Written (unix seconds) when Metamod loads the plugin: the server has read gameinfo.gi by then.</summary>
    public const string LoadedFileName = "loaded.txt";
}

/// <summary>One player's session stats (status.json); Rounds is the rounds played, for ADR.</summary>
public class PlayerStats
{
    public string Name { get; set; } = "";
    public bool Bot { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Headshots { get; set; }
    public int Damage { get; set; }
    public int Rounds { get; set; }
}
