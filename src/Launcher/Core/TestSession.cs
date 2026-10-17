using System.Diagnostics;

namespace LanSkinTester.Launcher.Core;

/// <summary>Starts/stops "skin test mode": Metamod enabled, a local sv_lan server and CS2 with -insecure.</summary>
public class TestSession
{
    private readonly GameFolder _game;

    public TestSession(GameFolder game) => _game = game;

    /// <summary>
    /// Test mode: Metamod is in gameinfo.gi, or our server is running (it has read gameinfo.gi already,
    /// and the line is taken out again so the game doesn't load the server plugins, see <see cref="IsolateClient"/>).
    /// </summary>
    public bool IsOn => GameInfoPatcher.IsEnabled(_game.GameInfo) || File.Exists(SessionFile) || FindServer() != null;

    /// <summary>
    /// Written when a server starts, removed by Stop. gameinfo.gi is already clean while the server runs,
    /// so without it a server that closed (or crashed) would skip Stop's clean-up: the router port and
    /// Workshop map copies.
    /// </summary>
    internal static string SessionFile => Path.Combine(AppState.LauncherDataDir, "test-session.on");

    internal static void MarkSession()
    {
        try
        {
            Directory.CreateDirectory(AppState.LauncherDataDir);
            File.WriteAllText(SessionFile, "");
        }
        catch (Exception)
        {
            // Test mode still shows while the server runs.
        }
    }

    /// <summary>
    /// The game reads the same gameinfo.gi as the server. With the Metamod line in it, CS2 itself loads
    /// Metamod and every server plugin (Bot Hider, Bot Controller, BotVision and BotAI patch the engine),
    /// which crashed the game when it joined. The server reads gameinfo.gi only when it starts, so once
    /// it is up the line comes out; every server start puts it back first.
    /// </summary>
    /// <param name="status">The status the plugin wrote. Only one written after this server process
    /// started counts: a restart leaves the old server's file behind for a few seconds.</param>
    public void IsolateClient(ServerStatus? status)
    {
        if (status == null || FindServer() is not { } server || !GameInfoPatcher.IsEnabled(_game.GameInfo)) return;
        try
        {
            if (!StatusFromThisServer(status.Time, server.StartTime.ToUniversalTime())) return;
        }
        catch (Exception)
        {
            return; // no access to the process: leave gameinfo.gi as it is
        }
        GameInfoPatcher.Disable(_game.GameInfo);
    }

    /// <summary>The plugin wrote this status (unix seconds) after the server process started (UTC).</summary>
    public static bool StatusFromThisServer(long statusTime, DateTime serverStartUtc) =>
        statusTime > new DateTimeOffset(DateTime.SpecifyKind(serverStartUtc, DateTimeKind.Utc)).ToUnixTimeSeconds() + 2;

    public static bool Cs2Running() => Process.GetProcessesByName("cs2").Length > 0;

    /// <summary>The server's process id and start time, so a restarted launcher still knows which cs2.exe is the server.</summary>
    private static string PidFile => Path.Combine(AppState.LauncherDataDir, "server.pid");

    /// <summary>The running dedicated server (this launcher's, or the one a previous launcher started), or null.</summary>
    public static Process? FindServer()
    {
        try
        {
            if (Server is { HasExited: false } running) return running;
        }
        catch (Exception) { /* no access: look it up below */ }
        try
        {
            var parts = File.ReadAllText(PidFile).Split(' ');
            var p = Process.GetProcessById(int.Parse(parts[0]));
            if (p.ProcessName.Equals("cs2", StringComparison.OrdinalIgnoreCase) && p.StartTime.ToUniversalTime().Ticks.ToString() == parts[1])
                return p;
        }
        catch (Exception) { /* no file, or that process is gone */ }
        return null;
    }

    /// <summary>A CS2 game window is open (any cs2.exe that isn't the server).</summary>
    public static bool ClientRunning()
    {
        var server = FindServer()?.Id;
        return Process.GetProcessesByName("cs2").Any(p => p.Id != server);
    }

    /// <summary>
    /// Starts the server. Returns whether the caller should start CS2 once the server reports it's up:
    /// starting both at once made the game join (and sometimes crash) while the server was still loading,
    /// and a Workshop map has to be downloaded first anyway (the caller handles that case itself).
    /// </summary>
    public bool Start(ServerSettings settings, bool forFriends = false)
    {
        if (Cs2Running())
            throw new InvalidOperationException("Close CS2 first (it must restart with -insecure).");

        settings.PrepareWorkshopMap(_game, forFriends);
        GameInfoPatcher.Enable(_game.GameInfo);
        LaunchServer(settings);
        return settings.AutoStartGame && !settings.WorkshopNeedsDownload;
    }

    /// <summary>Restarts only the server: an open CS2 stays open (it reconnects when the new server is up).</summary>
    public void RestartServer(ServerSettings settings, bool forFriends)
    {
        StopServerOnly();
        settings.PrepareWorkshopMap(_game, forFriends);
        GameInfoPatcher.Enable(_game.GameInfo);
        LaunchServer(settings);
    }

    /// <summary>Closes the server process only.</summary>
    public static void StopServerOnly()
    {
        StoppedOnPurpose = true;
        if (FindServer() is not { } server) return;
        try
        {
            server.Kill();
            server.WaitForExit(10_000);
        }
        catch (Exception) { /* already gone */ }
    }

    private void LaunchServer(ServerSettings settings)
    {
        settings.WriteCfg(_game);
        MarkSession();
        try { File.Delete(ConsoleLog); } catch (IOException) { /* still open */ }
        Server = Process.Start(new ProcessStartInfo(_game.Exe)
        {
            Arguments = settings.StartArguments(),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_game.Exe)!,
        });
        try
        {
            if (Server != null)
                File.WriteAllText(PidFile, $"{Server.Id} {Server.StartTime.ToUniversalTime().Ticks}");
        }
        catch (Exception) { /* only needed after a launcher restart */ }
        StoppedOnPurpose = false;
    }

    /// <summary>Makes the open CS2 join the local server again (Steam hands the connect to the running game).</summary>
    public static void ReconnectClient(string? password)
    {
        var pass = string.IsNullOrWhiteSpace(password) ? "" : "/" + Uri.EscapeDataString(password.Trim());
        Process.Start(new ProcessStartInfo($"steam://connect/127.0.0.1:{ServerSettings.Port}{pass}") { UseShellExecute = true });
    }

    /// <summary>Starts CS2 (VAC off) and joins the local server. Only valid while test mode is on.</summary>
    public void StartGame(ServerSettings settings)
    {
        Process.Start(new ProcessStartInfo(_game.Exe)
        {
            Arguments = settings.GameArguments(_game),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_game.Exe)!,
        });
    }

    /// <summary>The dedicated server process started by this launcher (null after a launcher restart).</summary>
    public static Process? Server { get; private set; }

    /// <summary>True after Stop, so a closed server isn't reported as a crash.</summary>
    public static bool StoppedOnPurpose { get; private set; } = true;

    /// <summary>The server's console output (it runs with -condebug).</summary>
    public string ConsoleLog => Path.Combine(_game.Csgo, "console.log");

    /// <summary>The last lines the server wrote, to show why it stopped.</summary>
    public string[] ConsoleTail(int lines = 12)
    {
        try
        {
            using var stream = new FileStream(ConsoleLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim() != "").TakeLast(lines).ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Closes the test server and game, then puts gameinfo.gi back to normal.</summary>
    public void Stop()
    {
        StoppedOnPurpose = true;
        foreach (var p in Process.GetProcessesByName("cs2"))
        {
            try
            {
                p.Kill();
                p.WaitForExit(10_000);
            }
            catch (Exception) { /* already gone */ }
        }
        GameInfoPatcher.Disable(_game.GameInfo);
        try { File.Delete(SessionFile); } catch (Exception) { /* removed on the next Stop */ }
        Upnp.RemoveMapping();
        try { WorkshopMaps.RemoveCopies(_game); }
        catch (Exception) { /* removed on the next Stop */ } // close the router port again (internet mode)
    }
}
