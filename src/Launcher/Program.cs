namespace LanSkinTester.Launcher;

internal static class Program
{
    /// <summary>Optional <c>--page play|live|skins|hacker|friends|setup</c> so a test run can open one screen.</summary>
    internal static string? StartPage { get; private set; }

    /// <summary>
    /// <c>--serve PORT [--token T]</c> runs without a window and only serves the UI (open it in a browser);
    /// <c>--devtools</c> allows the WebView2 developer tools; <c>--web-dir DIR</c> serves the UI from DIR.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        int? serve = null;
        string? token = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--page" && i + 1 < args.Length) StartPage = args[++i];
            else if (args[i] == "--serve" && i + 1 < args.Length && int.TryParse(args[i + 1], out var port)) { serve = port; i++; }
            else if (args[i] == "--token" && i + 1 < args.Length) token = args[++i];
            else if (args[i] == "--web-dir" && i + 1 < args.Length) Web.WebFiles.DevDir = args[++i];
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new Web.ShellForm(serve ?? 0, token, serveOnly: serve != null));
    }
}
