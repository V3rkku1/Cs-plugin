using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// The launcher window: one WebView2 showing the UI from <see cref="WebHost"/>. Without the WebView2
/// runtime the UI opens in the default browser instead and this window just says so.
/// With <c>--serve PORT</c> it runs hidden and only serves the UI (for testing in a normal browser).
/// </summary>
public sealed class ShellForm : Form
{
    private readonly AppState _app = new();
    private readonly int _port;
    private readonly string? _token;
    private readonly bool _serveOnly;
    private readonly System.Windows.Forms.Timer _watch = new() { Interval = 5000 };
    private Api _api = null!;
    private WebHost _host = null!;
    private bool _closing;

    public ShellForm(int port = 0, string? token = null, bool serveOnly = false)
    {
        _port = port;
        _token = token;
        _serveOnly = serveOnly;
        Text = "LAN Skin Tester";
        Size = new Size(1400, 900);
        MinimumSize = new Size(1080, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(8, 10, 18);
        ForeColor = Color.White;
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); }
        catch (Exception) { /* default icon */ }
        if (serveOnly)
        {
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-32000, -32000);
            Opacity = 0;
        }
        else RestoreWindow();

        HandleCreated += (_, _) => DarkTitleBar();
        Load += async (_, _) => await StartAsync();
        FormClosing += OnClosing;
    }

    private async Task StartAsync()
    {
        _api = new Api(_app, this);
        _host = new WebHost(_api, _token);
        await _host.StartAsync(_port);
        _api.Started();
        _watch.Tick += (_, _) => _api.WatchGame();
        _watch.Start();

        if (_serveOnly)
        {
            // Test runs read the address from here.
            File.WriteAllText(Path.Combine(AppState.LauncherDataDir, "serve-url.txt"), _host.Url);
            return;
        }

        var view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = BackColor };
        Controls.Add(view);
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppState.LauncherDataDir, "webview"));
            await view.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or FileNotFoundException)
        {
            Controls.Remove(view);
            ShowBrowserFallback();
            return;
        }

        var web = view.CoreWebView2;
        web.Settings.AreDevToolsEnabled = Environment.GetCommandLineArgs().Contains("--devtools");
        web.Settings.AreDefaultContextMenusEnabled = false;
        web.Settings.IsStatusBarEnabled = false;
        web.Settings.AreBrowserAcceleratorKeysEnabled = false; // F5, Ctrl+F etc. are the launcher's own shortcuts
        web.Settings.IsZoomControlEnabled = false;
        // Links to the web open in the real browser; the window only ever shows the launcher.
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        web.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith($"http://127.0.0.1:{_host.Port}/")) return;
            e.Cancel = true;
            OpenExternal(e.Uri);
        };
        web.Navigate(_host.Url);
    }

    private void ShowBrowserFallback()
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 12f),
            ForeColor = Color.White,
            Text = "The launcher opened in your web browser, because the Microsoft Edge WebView2 Runtime isn't installed.\n\n" +
                   "Install it from microsoft.com/edge/webview2 to have the launcher in its own window.\n\n" +
                   "Keep this window open while you use the launcher. Closing it quits.",
        };
        Controls.Add(label);
        OpenExternal(_host.Url);
    }

    private static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { /* no browser */ }
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing || _serveOnly) return;
        SaveWindow();
        if (_api != null && !_api.ConfirmClose())
        {
            e.Cancel = true;
            return;
        }
        _closing = true;
        _ = _host?.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------------
    // Window position (restored on the next start)
    // ---------------------------------------------------------------------------------------------

    private void RestoreWindow()
    {
        if (_app.Settings.Window is not [var x, var y, var w, var h]) return;
        var bounds = new Rectangle(x, y, w, h);
        // Only if it's still on a connected screen (monitors change).
        if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds) && s.WorkingArea.Contains(bounds.Location + new Size(40, 20)))) return;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(bounds.Location, new Size(Math.Max(bounds.Width, MinimumSize.Width), Math.Max(bounds.Height, MinimumSize.Height)));
        if (_app.Settings.Maximized) WindowState = FormWindowState.Maximized;
    }

    private void SaveWindow()
    {
        _app.Settings.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _app.Settings.Window = [b.X, b.Y, b.Width, b.Height];
        try { _app.Settings.Save(); }
        catch (IOException) { /* not important */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void DarkTitleBar()
    {
        try
        {
            var on = 1;
            DwmSetWindowAttribute(Handle, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            var round = 2;
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
            var caption = ColorTranslator.ToWin32(BackColor);
            DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
        }
        catch (Exception)
        {
            // Older Windows / Wine: default frame.
        }
    }
}
