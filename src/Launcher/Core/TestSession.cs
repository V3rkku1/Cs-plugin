using System.Diagnostics;

namespace LanSkinTester.Launcher.Core;

/// <summary>Starts/stops "skin test mode": Metamod enabled, a local sv_lan server and CS2 with -insecure.</summary>
public class TestSession
{
    private readonly GameFolder _game;

    public TestSession(GameFolder game) => _game = game;

    public bool IsOn => GameInfoPatcher.IsEnabled(_game.GameInfo);

    public static bool Cs2Running() => Process.GetProcessesByName("cs2").Length > 0;

    /// <summary>
    /// Starts the server, and CS2 too unless the server first has to download a Workshop map: joining
    /// before it's loaded fails with "unable to establish a connection", so then the caller starts the
    /// game once the server reports it's up. Returns whether CS2 was started.
    /// </summary>
    public bool Start(ServerSettings settings, bool forFriends = false)
    {
        if (Cs2Running())
            throw new InvalidOperationException("Close CS2 first (it must restart with -insecure).");

        settings.PrepareWorkshopMap(_game, forFriends);
        GameInfoPatcher.Enable(_game.GameInfo);
        settings.WriteCfg(_game);

        try { File.Delete(ConsoleLog); } catch (IOException) { /* still open */ }
        Server = Process.Start(new ProcessStartInfo(_game.Exe)
        {
            Arguments = settings.StartArguments(),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_game.Exe)!,
        });
        StoppedOnPurpose = false;
        if (!settings.AutoStartGame || settings.WorkshopNeedsDownload) return false;
        StartGame(settings);
        return true;
    }

    /// <summary>Starts CS2 (VAC off) and joins the local server. Only valid while test mode is on.</summary>
    public void StartGame(ServerSettings settings)
    {
        Process.Start(new ProcessStartInfo(_game.Exe)
        {
            Arguments = settings.GameArguments(),
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
        Upnp.RemoveMapping();
        try { WorkshopMaps.RemoveCopies(_game); }
        catch (Exception) { /* removed on the next Stop */ } // close the router port again (internet mode)
    }
}
