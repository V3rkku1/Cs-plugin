using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>Play page: quick starts, start / apply / stop the local server.</summary>
public partial class Api
{
    // What the running server was started with (null when this launcher didn't start it).
    private string? _runningMode, _runningMap, _runningRetakeKey;

    /// <summary>CS2 and the server were closed without Stop: restore quietly.</summary>
    private void AutoRestore()
    {
        if (App.Game == null) return;
        new TestSession(App.Game).Stop();
        _runningMode = _runningMap = _runningRetakeKey = null;
        try { File.Delete(Path.Combine(App.Game.PluginDir, ServerStatus.FileName)); }
        catch (IOException) { /* stale file is ignored anyway */ }
        App.Log("CS2 was closed - restored gameinfo.gi automatically. You can play online again.");
        App.NotifyChanged();
    }

    /// <summary>Called when the window closes while test mode is on.</summary>
    private void StopFromClose()
    {
        if (App.Game == null) return;
        new TestSession(App.Game).Stop();
        App.NotifyChanged();
    }
}
