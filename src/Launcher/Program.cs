namespace LanSkinTester.Launcher;

internal static class Program
{
    /// <summary>Optional <c>--page play|live|skins|hacker|friends|setup</c> so a test run can open one screen.</summary>
    internal static string? StartPage { get; private set; }

    [STAThread]
    private static void Main(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
            if (args[i] == "--page" && i + 1 < args.Length) StartPage = args[++i];
        ApplicationConfiguration.Initialize();
        var settings = LauncherSettings.Load();
        Ui.TextStyle.Configure(settings.TextFont, settings.TextRendering, settings.TextSize);
        Application.Run(new MainForm());
    }
}
