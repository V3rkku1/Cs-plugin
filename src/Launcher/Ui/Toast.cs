namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// A small rounded notification that slides in at the bottom of the window and fades after a few
/// seconds. <see cref="Show"/> works from anywhere once the main window has called <see cref="Attach"/>.
/// </summary>
public class Toast : DrawnControl
{
    public enum Kind { Info, Success, Warning, Error }

    private static Toast? _instance;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private Kind _kind;
    private DateTime _shownAt;
    private float _opacity;
    private const double ShowSeconds = 3.2;

    private Toast()
    {
        Height = 44;
        Visible = false;
        _timer.Tick += (_, _) => Animate();
        Click += (_, _) => _shownAt = DateTime.MinValue; // click to dismiss
        Cursor = Cursors.Hand;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // Opaque with clipped corners (a WinForms "transparent" control would show a box around it).
        using var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Height / 2f);
        Region = new Region(path);
    }

    /// <summary>Puts the toast host on <paramref name="host"/> (call once, from the main window).</summary>
    public static void Attach(Control host)
    {
        _instance = new Toast();
        host.Controls.Add(_instance);
        host.Resize += (_, _) => _instance.Place();
    }

    public static void Show(string text, Kind kind = Kind.Success)
    {
        var toast = _instance;
        if (string.IsNullOrWhiteSpace(text) || toast == null || toast.IsDisposed) return;
        if (toast.InvokeRequired)
        {
            toast.BeginInvoke(() => Show(text, kind));
            return;
        }
        toast.Text = text;
        toast._kind = kind;
        toast._shownAt = DateTime.UtcNow;
        toast.Width = Math.Min(TextRenderer.MeasureText(text, Theme.Body).Width + 64, Math.Max(toast.Parent!.Width - 80, 200));
        toast.Place();
        toast.Visible = true;
        toast.BringToFront();
        toast._timer.Start();
        toast.Invalidate();
    }

    private void Place()
    {
        if (Parent == null) return;
        // Slides up from below the window edge.
        var slide = (int)((1 - _opacity) * (Height + 30));
        Location = new Point((Parent.ClientSize.Width - Width) / 2, Parent.ClientSize.Height - Height - 26 + slide);
    }

    private void Animate()
    {
        var age = (DateTime.UtcNow - _shownAt).TotalSeconds;
        var target = age < ShowSeconds ? 1f : 0f;
        _opacity += (target - _opacity) * 0.25f;
        if (target == 0 && _opacity < 0.03f)
        {
            _opacity = 0;
            Visible = false;
            _timer.Stop();
            return;
        }
        Place();
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        var accent = _kind switch
        {
            Kind.Warning => Theme.Warning,
            Kind.Error => Theme.Bad,
            Kind.Info => Theme.Accent,
            _ => Theme.Good,
        };
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        g.Clear(Theme.Blend(Theme.Card, Color.White, 0.08f));
        using (var pen = new Pen(Theme.Blend(Theme.Separator, Color.White, 0.15f)))
        using (var path = Theme.Rounded(r, Height / 2f))
            g.DrawPath(pen, path);
        using (var dot = new SolidBrush(accent)) g.FillEllipse(dot, 18, Height / 2f - 5, 10, 10);
        Theme.DrawText(g, Text, Theme.Body, new Rectangle(38, 0, Width - 52, Height), Theme.Text);
    }
}
