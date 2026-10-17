namespace LanSkinTester.Launcher;

/// <summary>Stand-in for the launcher's AppState: only the data folder TestSession uses.</summary>
public class AppState
{
    public static string LauncherDataDir { get; set; } = Path.Combine(Path.GetTempPath(), "lst-tests-" + Environment.ProcessId);
}
