namespace LanSkinTester.Launcher.Ui;

/// <summary>Dark, CS2-inventory-like colours and helpers for styling WinForms controls.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(17, 19, 24);
    public static readonly Color Surface = Color.FromArgb(25, 28, 35);
    public static readonly Color Card = Color.FromArgb(33, 37, 46);
    public static readonly Color CardHover = Color.FromArgb(43, 48, 60);
    public static readonly Color Border = Color.FromArgb(52, 58, 72);
    public static readonly Color Text = Color.FromArgb(232, 235, 241);
    public static readonly Color Muted = Color.FromArgb(139, 148, 165);
    public static readonly Color Accent = Color.FromArgb(235, 164, 52);
    public static readonly Color AccentDark = Color.FromArgb(190, 128, 30);
    public static readonly Color Good = Color.FromArgb(92, 196, 120);
    public static readonly Color Bad = Color.FromArgb(232, 96, 96);
    public static readonly Color Input = Color.FromArgb(14, 16, 20);

    public static readonly Font Body = new("Segoe UI", 9.5f);
    public static readonly Font Small = new("Segoe UI", 8.5f);
    public static readonly Font Bold = new("Segoe UI Semibold", 9.5f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11.5f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font Big = new("Segoe UI Semibold", 12f);

    public static Color Parse(string? hex, Color fallback)
    {
        if (string.IsNullOrEmpty(hex)) return fallback;
        try { return ColorTranslator.FromHtml(hex); }
        catch (Exception) { return fallback; }
    }

    /// <summary>Styles a control tree. Call after building it.</summary>
    public static void Apply(Control root)
    {
        Style(root);
        foreach (Control c in root.Controls) Apply(c);
    }

    private static void Style(Control c)
    {
        switch (c)
        {
            case Button b when b.Tag is ButtonKind:
                break; // styled when created
            case Button b:
                StyleButton(b, ButtonKind.Normal);
                break;
            case TextBox t:
                t.BackColor = Input;
                t.ForeColor = Text;
                t.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Input;
                cb.ForeColor = Text;
                break;
            case NumericUpDown n:
                n.BackColor = Input;
                n.ForeColor = Text;
                n.BorderStyle = BorderStyle.FixedSingle;
                break;
            case CheckBox ch:
                ch.ForeColor = Text;
                ch.FlatStyle = FlatStyle.Flat;
                ch.FlatAppearance.BorderColor = Border;
                ch.FlatAppearance.CheckedBackColor = Accent;
                break;
            case LinkLabel l:
                l.LinkColor = Accent;
                l.ActiveLinkColor = Text;
                l.VisitedLinkColor = Accent;
                break;
            case TrackBar tb:
                tb.BackColor = c.Parent?.BackColor ?? Surface;
                break;
            case Label l when l.ForeColor == SystemColors.ControlText:
                l.ForeColor = Text;
                break;
        }
    }

    public enum ButtonKind { Normal, Primary, Danger, Ghost }

    public static Button MakeButton(string text, ButtonKind kind = ButtonKind.Normal, int width = 0)
    {
        var b = new Button { Text = text, AutoSize = width == 0, Height = 34, Padding = new Padding(10, 0, 10, 0), Cursor = Cursors.Hand };
        if (width > 0) b.Width = width;
        StyleButton(b, kind);
        return b;
    }

    public static void StyleButton(Button b, ButtonKind kind)
    {
        b.Tag ??= kind;
        b.FlatStyle = FlatStyle.Flat;
        b.UseVisualStyleBackColor = false;
        b.Font = kind == ButtonKind.Primary ? Big : Bold;
        b.FlatAppearance.BorderSize = kind == ButtonKind.Ghost ? 0 : 1;
        (b.BackColor, b.ForeColor, b.FlatAppearance.BorderColor, b.FlatAppearance.MouseOverBackColor) = kind switch
        {
            ButtonKind.Primary => (Accent, Color.FromArgb(20, 16, 8), Accent, Color.FromArgb(247, 184, 82)),
            ButtonKind.Danger => (Card, Bad, Bad, Color.FromArgb(70, 36, 40)),
            ButtonKind.Ghost => (Surface, Muted, Surface, Card),
            _ => (Card, Text, Border, CardHover),
        };
        b.EnabledChanged -= GreyOut;
        b.EnabledChanged += GreyOut;
    }

    // Flat WinForms buttons keep their colours when disabled; dim them so the state is visible.
    private static void GreyOut(object? sender, EventArgs e)
    {
        if (sender is not Button b || b.Tag is not ButtonKind kind) return;
        if (b.Enabled) StyleButton(b, kind);
        else
        {
            b.BackColor = Surface;
            b.ForeColor = Color.FromArgb(80, 86, 100);
            b.FlatAppearance.BorderColor = Card;
        }
    }

    public static Label Label(string text, Font? font = null, Color? color = null) =>
        new() { Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Text, Margin = new Padding(0, 6, 6, 0) };

    /// <summary>A rounded "card" panel with a heading.</summary>
    public static Panel Section(string title, Control content)
    {
        var panel = new Panel { BackColor = Surface, Padding = new Padding(16, 12, 16, 14), Margin = new Padding(0, 0, 0, 12) };
        var heading = new Label { Text = title.ToUpperInvariant(), Font = Bold, ForeColor = Accent, Dock = DockStyle.Top, Height = 26 };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Controls.Add(heading);
        return panel;
    }
}
