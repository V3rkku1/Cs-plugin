namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// The big banner at the top of the Play page: a rounded card with a soft blue-violet gradient. Its
/// children (labels, buttons) are transparent and placed by the owner in <see cref="Control.Layout"/>.
/// </summary>
public class Hero : Panel
{
    public bool Running { get; set; }

    public Hero(int width, int height)
    {
        Size = new Size(width, height);
        Margin = new Padding(0, 0, 0, 4);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        using var path = Theme.Rounded(new RectangleF(0, 0, Width, Height), Theme.S(14f));
        Region = new Region(path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        g.Clear(Theme.Background);
        var r = new RectangleF(0, 0, Width, Height);
        // iOS widget: a flat card with a soft status-coloured light in the corner (green while running).
        Theme.FillRounded(g, Theme.Card, r, Theme.S(14f));

        using (var glow = new System.Drawing.Drawing2D.GraphicsPath())
        {
            glow.AddEllipse(Width - Theme.S(360f), -Theme.S(180f), Theme.S(520f), Theme.S(360f));
            using var brush = new System.Drawing.Drawing2D.PathGradientBrush(glow)
            {
                CenterColor = Color.FromArgb(Running ? 55 : 45, Running ? Theme.Good : Theme.Accent),
                SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
            };
            g.FillPath(brush, glow);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // Everything is in the background (children paint on top).
    }
}
