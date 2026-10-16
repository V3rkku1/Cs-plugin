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
    public List<string> Players { get; set; } = new();
    public bool SharedLoadout { get; set; } = true;
    public bool PrivateServer { get; set; }
    public bool SkinsAllowed { get; set; }
    public string DisabledReason { get; set; } = "";

    /// <summary>Unix time (seconds) of the last write; the launcher treats old files as "server not running".</summary>
    public long Time { get; set; }

    public const string FileName = "status.json";
}
