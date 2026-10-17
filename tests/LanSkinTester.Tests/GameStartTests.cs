using LanSkinTester.Launcher;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Tests;

/// <summary>
/// Starting the server and the game: gameinfo.gi gets the Metamod line for the server, the game must not
/// load Metamod (the bot plugins patch the engine and crashed CS2 on joining), and test mode stays on
/// until Stop has cleaned up.
/// </summary>
public sealed class GameStartTests : IDisposable
{
    private const string GameInfo = """
        "GameInfo"
        {
        	FileSystem
        	{
        		SearchPaths
        		{
        			Game_LowViolence	csgo_lv
        			Game	csgo
        			Game	csgo_imported
        			Mod	csgo
        		}
        	}
        }
        """;

    private readonly string _root = Directory.CreateTempSubdirectory("lst-game").FullName;
    private readonly GameFolder _game;

    public GameStartTests()
    {
        _game = new GameFolder(_root);
        Directory.CreateDirectory(_game.Csgo);
        File.WriteAllText(_game.GameInfo, GameInfo);
        AppState.LauncherDataDir = Path.Combine(_root, "data");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void MetamodLineGoesInAboveCsgoAndComesOutAgain()
    {
        GameInfoPatcher.Enable(_game.GameInfo);
        var lines = File.ReadAllLines(_game.GameInfo);
        var metamod = Array.FindIndex(lines, l => l.Trim() == "Game\tcsgo/addons/metamod");
        var csgo = Array.FindIndex(lines, l => l.Trim() == "Game\tcsgo");
        Assert.True(metamod >= 0 && metamod == csgo - 1, "Metamod must be the search path right above csgo");
        Assert.True(File.Exists(_game.GameInfo + ".backup"));

        GameInfoPatcher.Enable(_game.GameInfo); // twice: still one line
        Assert.Single(File.ReadAllLines(_game.GameInfo), l => l.Contains("addons/metamod"));

        GameInfoPatcher.Disable(_game.GameInfo);
        Assert.Equal(GameInfo.Replace("\r", "").TrimEnd(), File.ReadAllText(_game.GameInfo).Replace("\r", "").TrimEnd());
    }

    [Fact]
    public void UnexpectedGameInfoIsLeftAlone()
    {
        File.WriteAllText(_game.GameInfo, "\"GameInfo\" { }");
        Assert.Throws<InvalidDataException>(() => GameInfoPatcher.Enable(_game.GameInfo));
        Assert.Equal("\"GameInfo\" { }", File.ReadAllText(_game.GameInfo));
    }

    [Fact]
    public void OnlyAStatusFromTheNewServerCounts()
    {
        var started = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        var startedUnix = new DateTimeOffset(started).ToUnixTimeSeconds();
        // A restart leaves the old server's status file behind for a few seconds: the new server is still
        // loading gameinfo.gi then, and taking the Metamod line out would start it without plugins.
        Assert.False(TestSession.StatusFromThisServer(startedUnix - 5, started));
        Assert.False(TestSession.StatusFromThisServer(startedUnix, started));
        Assert.True(TestSession.StatusFromThisServer(startedUnix + 20, started));
    }

    [Fact]
    public void OnlyAPluginLoadByTheNewServerStartsTheGameEarly()
    {
        var started = new DateTime(2026, 10, 17, 12, 0, 0, 500, DateTimeKind.Utc);
        var startedUnix = new DateTimeOffset(started).ToUnixTimeSeconds();
        Assert.False(TestSession.LoadedByThisServer(startedUnix - 30, started));
        Assert.True(TestSession.LoadedByThisServer(startedUnix, started));
        Assert.True(TestSession.LoadedByThisServer(startedUnix + 4, started));
    }

    [Fact]
    public void ClientIsNotIsolatedEarlyWithoutOurServer()
    {
        GameInfoPatcher.Enable(_game.GameInfo);
        Directory.CreateDirectory(_game.PluginDir);
        File.WriteAllText(Path.Combine(_game.PluginDir, ServerStatus.LoadedFileName), DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
        Assert.False(new TestSession(_game).IsolateClientEarly()); // no server process: nothing has read gameinfo.gi
        Assert.True(GameInfoPatcher.IsEnabled(_game.GameInfo));
    }

    [Fact]
    public void ClientIsNotIsolatedWithoutOurServer()
    {
        GameInfoPatcher.Enable(_game.GameInfo);
        var status = new ServerStatus { Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        new TestSession(_game).IsolateClient(status); // no server process: nothing to protect yet
        Assert.True(GameInfoPatcher.IsEnabled(_game.GameInfo));
        new TestSession(_game).IsolateClient(null);
        Assert.True(GameInfoPatcher.IsEnabled(_game.GameInfo));
    }

    [Fact]
    public void TestModeStaysOnUntilStop()
    {
        var session = new TestSession(_game);
        Assert.False(session.IsOn);

        // The server started: the marker is written even into a data folder that didn't exist yet.
        TestSession.MarkSession();
        Assert.True(File.Exists(TestSession.SessionFile));
        // gameinfo.gi is clean (the client was isolated, or the server crashed): still test mode, so the
        // launcher's auto-restore runs Stop (router port, Workshop copies).
        Assert.False(GameInfoPatcher.IsEnabled(_game.GameInfo));
        Assert.True(session.IsOn);

        session.Stop();
        Assert.False(File.Exists(TestSession.SessionFile));
        Assert.False(session.IsOn);
    }

    [Fact]
    public void StopPutsGameInfoBack()
    {
        GameInfoPatcher.Enable(_game.GameInfo);
        TestSession.MarkSession();
        new TestSession(_game).Stop();
        Assert.False(GameInfoPatcher.IsEnabled(_game.GameInfo));
    }
}
