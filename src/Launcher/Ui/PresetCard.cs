namespace LanSkinTester.Launcher.Ui;

/// <summary>A clickable quick-start card: bold title, grey description, accent bar on hover.</summary>
public class PresetCard : Control
{
    private readonly string _title;
    private readonly string _subtitle;
    private bool _hover;

    public PresetCard(string title, string subtitle)
    {
        _title = title;
        _subtitle = subtitle;
        Size = new Size(168, 64);
        Margin = new Padding(0, 0, 6, 6);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = CardGrid.Rounded(r, 5))
        {
            using var bg = new SolidBrush(_hover ? Theme.CardHover : Theme.Card);
            g.FillPath(bg, path);
            using var pen = new Pen(_hover ? Theme.Accent : Theme.Border);
            g.DrawPath(pen, path);
        }
        using (var bar = new SolidBrush(_hover ? Theme.Accent : Theme.Border)) g.FillRectangle(bar, 1, 8, 3, Height - 16);

        var text = Enabled ? Theme.Text : Theme.Muted;
        TextRenderer.DrawText(g, _title, Theme.Bold, new Rectangle(12, 8, Width - 18, 20), _hover ? Theme.Accent : text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, _subtitle, Theme.Small, new Rectangle(12, 28, Width - 18, 32), Theme.Muted,
            TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
