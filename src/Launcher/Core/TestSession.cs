using System.Diagnostics;

namespace LanSkinTester.Launcher.Core;

/// <summary>Starts/stops "skin test mode": Metamod enabled, a local sv_lan server and CS2 with -insecure.</summary>
public class TestSession
{
    private readonly GameFolder _game;

    public TestSession(GameFolder game) => _game = game;

    public bool IsOn => GameInfoPatcher.IsEnabled(_game.GameInfo);

    public static bool Cs2Running() => Process.GetProcessesByName("cs2").Length > 0;

    public void Start(ServerSettings settings)
    {
        if (Cs2Running())
            throw new InvalidOperationException("Close CS2 first (it must restart with -insecure).");

        GameInfoPatcher.Enable(_game.GameInfo);
        settings.WriteCfg(_game);

        Process.Start(new ProcessStartInfo(_game.Exe)
        {
            Arguments = settings.StartArguments(),
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_game.Exe)!,
        });
        Process.Start(new ProcessStartInfo(_game.Exe)
        {
            Arguments = "-insecure +connect localhost",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(_game.Exe)!,
        });
    }

    /// <summary>Closes the test server and game, then puts gameinfo.gi back to normal.</summary>
    public void Stop()
    {
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
    }
}
