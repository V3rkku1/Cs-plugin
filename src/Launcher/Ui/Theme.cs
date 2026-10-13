using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// macOS-style dark appearance: system colours, Segoe UI Variable (the closest thing to SF Pro on Windows),
/// rounded shapes. All custom controls draw themselves with these values.
/// </summary>
public static class Theme
{
    // Backgrounds (window → sidebar → grouped content → controls)
    public static readonly Color Background = Color.FromArgb(22, 22, 24);
    public static readonly Color Sidebar = Color.FromArgb(30, 30, 33);
    public static readonly Color Surface = Color.FromArgb(22, 22, 24);
    public static readonly Color Card = Color.FromArgb(37, 37, 40);
    public static readonly Color CardHover = Color.FromArgb(46, 46, 50);
    public static readonly Color Fill = Color.FromArgb(58, 58, 61);
    public static readonly Color FillHover = Color.FromArgb(72, 72, 76);
    public static readonly Color Input = Color.FromArgb(28, 28, 30);
    public static readonly Color Separator = Color.FromArgb(56, 56, 58);
    public static readonly Color Border = Separator;

    // Text
    public static readonly Color Text = Color.FromArgb(245, 245, 247);
    public static readonly Color Muted = Color.FromArgb(152, 152, 159);
    public static readonly Color Tertiary = Color.FromArgb(99, 99, 102);

    // System colours
    public static readonly Color Accent = Color.FromArgb(10, 132, 255);
    public static readonly Color AccentHover = Color.FromArgb(64, 156, 255);
    public static readonly Color Good = Color.FromArgb(48, 209, 88);
    public static readonly Color Bad = Color.FromArgb(255, 69, 58);
    public static readonly Color Warning = Color.FromArgb(255, 159, 10);

    private static readonly string Family = Pick("Segoe UI Variable Text", "Segoe UI");
    private static readonly string DisplayFamily = Pick("Segoe UI Variable Display", "Segoe UI");

    public static readonly Font Body = new(Family, 10f);
    public static readonly Font Small = new(Family, 8.75f);
    public static readonly Font Bold = new(Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 10f);
    public static readonly Font Heading = new(Pick("Segoe UI Variable Display Semibold", "Segoe UI Semibold"), 12f);
    public static readonly Font Big = new(Pick("Segoe UI Variable Display Semibold", "Segoe UI Semibold"), 14f);
    public static readonly Font Title = new(Pick("Segoe UI Variable Display Bold", "Segoe UI Semibold"), 24f);
    public static readonly Font Mono = new(Pick("Cascadia Mono", "Consolas"), 8.75f);

    private static string Pick(params string[] names)
    {
        using var fonts = new InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.FirstOrDefault(installed.Contains) ?? names[^1];
    }

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try { return ColorTranslator.FromHtml(hex); }
        catch (Exception) { return fallback; }
    }

    public static Color WithAlpha(Color c, int alpha) => Color.FromArgb(alpha, c);

    public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    public static void DrawText(Graphics g, string text, Font font, Rectangle r, Color color,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Plain text label in the theme colours.</summary>
    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, BackColor = Color.Transparent,
        Margin = new Padding(0, 4, 8, 4),
    };

    /// <summary>Small grey caption above a group, like macOS settings section headers.</summary>
    public static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, Font = Small, ForeColor = Muted, BackColor = Color.Transparent,
        Margin = new Padding(14, 18, 0, 6),
    };

    // ---------------------------------------------------------------------------------------------
    // Windows: dark title bar + rounded corners (Windows 11). Ignored where unsupported.
    // ---------------------------------------------------------------------------------------------

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void StyleWindow(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = Body;
        form.HandleCreated += (_, _) =>
        {
            try
            {
                var on = 1;
                DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
                var round = 2;
                DwmSetWindowAttribute(form.Handle, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
                var caption = ColorTranslator.ToWin32(Background);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
            }
            catch (Exception)
            {
                // Older Windows / Wine: keep the default frame.
            }
        };
    }

    // ---------------------------------------------------------------------------------------------
    // Compatibility helpers used by older code paths
    // ---------------------------------------------------------------------------------------------

    public enum ButtonKind { Normal, Primary, Danger, Ghost }

    public static PillButton MakeButton(string text, ButtonKind kind = ButtonKind.Normal, int width = 0)
    {
        var style = kind switch
        {
            ButtonKind.Primary => PillButton.Style.Primary,
            ButtonKind.Danger => PillButton.Style.Destructive,
            ButtonKind.Ghost => PillButton.Style.Plain,
            _ => PillButton.Style.Secondary,
        };
        var b = new PillButton(text, style);
        if (width > 0) b.Width = width;
        return b;
    }

    /// <summary>Recursively sets theme colours on stock controls that are still used (labels, list boxes).</summary>
    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls)
        {
            switch (c)
            {
                case Label l when l.ForeColor == SystemColors.ControlText:
                    l.ForeColor = Text;
                    break;
                case LinkLabel ll:
                    ll.LinkColor = ll.VisitedLinkColor = Accent;
                    ll.ActiveLinkColor = AccentHover;
                    ll.LinkBehavior = LinkBehavior.HoverUnderline;
                    break;
            }
            Apply(c);
        }
    }
}
