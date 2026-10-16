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
    private WebView2? _view;
    private bool _fullscreen;
    private FormWindowState _beforeFullscreen;

    /// <summary>The layout is made for about this much room; bigger windows zoom the whole page instead of stretching it.</summary>
    private const double DesignWidth = 1480, DesignHeight = 920, MaxZoom = 1.6;

    public ShellForm(int port = 0, string? token = null, bool serveOnly = false)
    {
        _port = port;
        _token = token;
        _serveOnly = serveOnly;
        Text = "LAN Skin Tester";
        // Sizes are in pixels: scale them for the screen's DPI (150% on many laptops), but keep the window on the screen.
        var scale = DeviceDpi / 96.0;
        var area = Screen.PrimaryScreen?.WorkingArea.Size ?? new Size(1920, 1040);
        Size Fit(int w, int h) => new((int)Math.Min(w * scale, area.Width * 0.94), (int)Math.Min(h * scale, area.Height * 0.94));
        Size = Fit(1400, 900);
        MinimumSize = Fit(1080, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeColor("background", Color.FromArgb(15, 17, 23));
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

        HandleCreated += (_, _) => ApplyTitleBar();
        Resize += (_, _) => FitZoom();
        DpiChanged += (_, _) => FitZoom();
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
        _view = view;
        Controls.Add(view);
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppState.LauncherDataDir, "webview"));
            await view.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or FileNotFoundException)
        {
            Controls.Remove(view);
            _view = null;
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
        web.NavigationCompleted += (_, _) => FitZoom();
        web.Navigate(_host.Url);
        FitZoom();
    }

    /// <summary>
    /// Maximized or full screen on a big monitor the page would otherwise stay small and stretch across
    /// the screen: zoom it so it fills the window like it does at the normal size.
    /// </summary>
    private void FitZoom()
    {
        if (_view?.CoreWebView2 == null || WindowState == FormWindowState.Minimized) return;
        var scale = DeviceDpi / 96.0;
        var width = ClientSize.Width / scale;
        var height = ClientSize.Height / scale;
        var zoom = Math.Clamp(Math.Min(width / DesignWidth, height / DesignHeight), 1, MaxZoom);
        zoom = Math.Round(zoom * 20) / 20; // steps of 5%, so small resizes don't keep re-zooming
        if (Math.Abs(_view.ZoomFactor - zoom) > 0.001) _view.ZoomFactor = zoom;
    }

    /// <summary>F11: borderless full screen (covers the taskbar), and back.</summary>
    public void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _beforeFullscreen = WindowState;
            // Maximizing again after the border change makes Windows size it to the whole monitor.
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = _beforeFullscreen;
        }
        _fullscreen = !_fullscreen;
        FitZoom();
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

    private const int DwmUseImmersiveDarkMode = 20, DwmCornerPreference = 33, DwmBorderColor = 34,
        DwmCaptionColor = 35, DwmTextColor = 36, DwmColorDefault = unchecked((int)0xFFFFFFFF);

    /// <summary>A colour from Setup › Appearance (#rrggbb), or the fallback.</summary>
    private Color ThemeColor(string key, Color fallback)
    {
        var hex = _app.Settings.Theme.GetValueOrDefault(key);
        if (hex is not { Length: 7 } || hex[0] != '#') return fallback;
        try { return ColorTranslator.FromHtml(hex); }
        catch (Exception) { return fallback; }
    }

    /// <summary>
    /// Colours the Windows title bar like the launcher (Setup › Appearance › Title Bar): the theme's
    /// background with a panel-coloured border, the accent colour, or Windows' own. Colours need
    /// Windows 11; Windows 10 only gets the dark bar.
    /// </summary>
    public void ApplyTitleBar()
    {
        if (!IsHandleCreated) return;
        var style = _app.Settings.Theme.GetValueOrDefault("titlebar") ?? "theme";
        var background = ThemeColor("background", Color.FromArgb(15, 17, 23));
        BackColor = background;
        if (_view != null) _view.DefaultBackgroundColor = background;

        var caption = style switch
        {
            "accent" => ThemeColor("accent", Color.FromArgb(10, 132, 255)),
            "system" => (Color?)null,
            _ => background,
        };
        var border = style == "system" ? (Color?)null : style == "accent" ? caption : ThemeColor("panel", Color.FromArgb(26, 29, 38));
        try
        {
            var dark = caption is not { } c || c.GetBrightness() < 0.6f ? 1 : 0;
            DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            var round = 2;
            DwmSetWindowAttribute(Handle, DwmCornerPreference, ref round, sizeof(int)); // round corners
            var captionValue = caption is { } cap ? ColorTranslator.ToWin32(cap) : DwmColorDefault;
            DwmSetWindowAttribute(Handle, DwmCaptionColor, ref captionValue, sizeof(int));
            var textValue = caption is { } capText ? ColorTranslator.ToWin32(ReadableOn(capText)) : DwmColorDefault;
            DwmSetWindowAttribute(Handle, DwmTextColor, ref textValue, sizeof(int));
            var borderValue = border is { } b ? ColorTranslator.ToWin32(b) : DwmColorDefault;
            DwmSetWindowAttribute(Handle, DwmBorderColor, ref borderValue, sizeof(int));
        }
        catch (Exception)
        {
            // Older Windows / Wine: default frame.
        }
    }

    /// <summary>White or near-black title text, whichever reads better on the bar.</summary>
    private static Color ReadableOn(Color c)
    {
        var luminance = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        return luminance > 0.6 ? Color.FromArgb(20, 20, 24) : Color.FromArgb(240, 240, 245);
    }

    private static readonly Dictionary<string, string> PageTitles = new()
    {
        ["play"] = "Play", ["live"] = "Live Server", ["skins"] = "All Skins", ["inventory"] = "Inventory", ["cases"] = "Cases", ["hacker"] = "Hacker Mode", ["workshop"] = "Workshop",
        ["friends"] = "Friends", ["setup"] = "Setup",
    };

    /// <summary>"Skins · LAN Skin Tester" in the title bar and the taskbar.</summary>
    public void ShowPage(string page) =>
        Text = PageTitles.TryGetValue(page, out var title) ? $"{title} · LAN Skin Tester" : "LAN Skin Tester";
}
