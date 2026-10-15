namespace LanSkinTester.Launcher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = LauncherSettings.Load();
        Ui.TextStyle.Configure(settings.TextFont, settings.TextRendering, settings.TextSize);
        Application.Run(new MainForm());
    }
}
