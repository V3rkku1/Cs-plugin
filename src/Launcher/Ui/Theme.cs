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

    // Inter (embedded), drawn anti-aliased: smooth like macOS instead of the blocky hinted look.
    public static readonly Font Body = Fonts.Create("Inter-Regular.ttf", 10f, Family);
    public static readonly Font Small = Fonts.Create("Inter-Regular.ttf", 8.75f, Family);
    public static readonly Font Bold = Fonts.Create("Inter-SemiBold.ttf", 10f, Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold"));
    public static readonly Font Heading = Fonts.Create("Inter-SemiBold.ttf", 11.5f, Pick("Segoe UI Variable Display Semibold", "Segoe UI Semibold"));
    public static readonly Font Big = Fonts.Create("Inter-SemiBold.ttf", 13.5f, Pick("Segoe UI Variable Display Semibold", "Segoe UI Semibold"));
    public static readonly Font Title = Fonts.Create("Inter-SemiBold.ttf", 22f, Pick("Segoe UI Variable Display Bold", "Segoe UI Semibold"));
    public static readonly Font Mono = new(Pick("Cascadia Mono", "Consolas"), 8.75f);

    private static string Pick(params string[] names)
    {
        using var fonts = new InstalledFontCollection();
        var installed = fonts.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return names.FirstOrDefault(installed.Contains) ?? names[^1];
    }

    /// <summary>Screen scaling (1 at 100%, 1.5 at 150%). Layouts are in 96-DPI pixels and scaled by WinForms.</summary>
    public static readonly float Scale = ReadScale();

    private static float ReadScale()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero);
            return Math.Max(1f, g.DpiX / 96f);
        }
        catch (Exception)
        {
            return 1f;
        }
    }

    /// <summary>A 96-DPI length in real pixels.</summary>
    public static float S(float value) => value * Scale;

    public static int S(int value) => (int)Math.Round(value * Scale);

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
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    /// <summary>Brand gradient (the app icon's blue → violet), used for primary buttons.</summary>
    public static readonly Color GradientStart = Color.FromArgb(10, 132, 255);
    public static readonly Color GradientEnd = Color.FromArgb(124, 77, 255);

    public static void FillRoundedGradient(Graphics g, Color from, Color to, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = Rounded(r, radius);
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(r, from, to, System.Drawing.Drawing2D.LinearGradientMode.ForwardDiagonal);
        g.FillPath(brush, path);
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = Rounded(r, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    /// <summary>Anti-aliased (GDI+) text. Takes the same flags as TextRenderer for easy use.</summary>
    public static void DrawText(Graphics g, string text, Font font, Rectangle r, Color color,
        TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter)
    {
        if (string.IsNullOrEmpty(text) || r.Width <= 0 || r.Height <= 0) return;
        // A box shorter than one line would draw nothing: grow it around its middle.
        var line = (int)Math.Ceiling(font.GetHeight(g)) + 1;
        if (r.Height < line && !flags.HasFlag(TextFormatFlags.WordBreak)) r = new Rectangle(r.X, r.Y + r.Height / 2 - line / 2, r.Width, line);
        var hint = g.TextRenderingHint;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        using var format = Format(flags);
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, r, format);
        g.TextRenderingHint = hint;
    }

    /// <summary>Size of the text as <see cref="DrawText"/> draws it (rounded up, with a little air).</summary>
    public static Size MeasureText(string text, Font font)
    {
        using var bitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        var size = g.MeasureString(string.IsNullOrEmpty(text) ? " " : text, font, PointF.Empty, StringFormat.GenericTypographic);
        return new Size((int)Math.Ceiling(size.Width) + 4, (int)Math.Ceiling(font.GetHeight()) + 2);
    }

    private static StringFormat Format(TextFormatFlags flags)
    {
        var f = new StringFormat(StringFormat.GenericTypographic)
        {
            Trimming = StringTrimming.EllipsisCharacter,
            HotkeyPrefix = HotkeyPrefix.None,
            Alignment = flags.HasFlag(TextFormatFlags.HorizontalCenter) ? StringAlignment.Center
                : flags.HasFlag(TextFormatFlags.Right) ? StringAlignment.Far : StringAlignment.Near,
            LineAlignment = flags.HasFlag(TextFormatFlags.VerticalCenter) ? StringAlignment.Center
                : flags.HasFlag(TextFormatFlags.Bottom) ? StringAlignment.Far : StringAlignment.Near,
        };
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
        if (!flags.HasFlag(TextFormatFlags.WordBreak)) f.FormatFlags |= StringFormatFlags.NoWrap;
        return f;
    }

    /// <summary>Plain text label in the theme colours.</summary>
    public static Label Label(string text, Font? font = null, Color? color = null) => new SmoothLabel()
    {
        Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, BackColor = Color.Transparent,
        Margin = new Padding(0, 4, 8, 4),
    };

    /// <summary>Small grey caption above a group, like macOS settings section headers.</summary>
    public static Label Caption(string text) => new SmoothLabel()
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
        // Laid out in 96-DPI pixels: scale up on 125% / 150% screens (text stays sharp, nothing overlaps).
        form.AutoScaleDimensions = new SizeF(96F, 96F);
        form.AutoScaleMode = AutoScaleMode.Dpi;
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

    /// <summary>
    /// The solid colour actually painted behind <paramref name="control"/>: inside a rounded group
    /// that's the card colour, otherwise the first ancestor with a real (non-transparent) colour.
    /// </summary>
    public static Color BackdropOf(Control? control)
    {
        for (var c = control; c != null; c = c.Parent)
        {
            if (c is Group) return Card;
            if (c.BackColor.A == 255) return c.BackColor;
        }
        return Background;
    }

    /// <summary>
    /// Gives transparent layout containers (stacks, rows, panels) the solid colour they sit on.
    /// WinForms "transparency" repaints through every parent and leaves smears and odd corners
    /// behind; self-drawn controls and labels stay transparent on top of the solid containers.
    /// </summary>
    public static void SolidifyContainers(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is not DrawnControl and not System.Windows.Forms.Label and not Group && c.BackColor.A < 255)
                c.BackColor = BackdropOf(c.Parent);
            SolidifyContainers(c);
        }
    }

    /// <summary>Recursively sets theme colours on stock controls that are still used (labels, list boxes).</summary>
    public static void Apply(Control root)
    {
        if (root.BackColor.A == 255 || root.Parent != null) SolidifyContainers(root);
        ApplyColors(root);
    }

    private static void ApplyColors(Control root)
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
            ApplyColors(c);
        }
    }
}

/// <summary>A label drawn with anti-aliased GDI+ text, matching the self-drawn controls.</summary>
public class SmoothLabel : Label
{
    public SmoothLabel()
    {
        UseCompatibleTextRendering = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        base.OnPaint(e);
    }
}
