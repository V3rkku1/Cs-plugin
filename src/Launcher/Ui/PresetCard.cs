namespace LanSkinTester.Launcher.Ui;

/// <summary>A quick-start tile: coloured symbol, title and description.</summary>
public class PresetCard : DrawnControl
{
    private readonly string _title;
    private readonly string _subtitle;
    private readonly Color _tint;

    public PresetCard(string title, string subtitle, Color tint)
    {
        _title = title;
        _subtitle = subtitle;
        _tint = tint;
        Size = new Size(176, 92);
        Margin = new Padding(0, 0, 10, 10);
        Cursor = Cursors.Hand;
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var bg = Pressed ? Theme.Fill : Hover ? Theme.CardHover : Theme.Card;
        Theme.FillRounded(g, bg, r, 12);
        if (Hover)
        {
            using var path = Theme.Rounded(r, 12);
            using var pen = new Pen(Theme.WithAlpha(_tint, 180), 1.5f);
            g.DrawPath(pen, path);
        }

        // Icon: a rounded square in the tile's colour with a play triangle.
        var icon = new RectangleF(Theme.S(14f), Theme.S(14f), Theme.S(26f), Theme.S(26f));
        Theme.FillRounded(g, _tint, icon, 7);
        using (var white = new SolidBrush(Color.White))
            g.FillPolygon(white, [new PointF(icon.Left + Theme.S(10f), icon.Top + Theme.S(7.5f)), new PointF(icon.Left + Theme.S(10f), icon.Bottom - Theme.S(7.5f)), new PointF(icon.Right - Theme.S(7f), icon.Top + Theme.S(13f))]);

        Theme.DrawText(g, _title, Theme.Bold, new Rectangle(Theme.S(14), Theme.S(46), Width - Theme.S(22), Theme.S(20)), Enabled ? Theme.Text : Theme.Muted);
        Theme.DrawText(g, _subtitle, Theme.Small, new Rectangle(Theme.S(14), Theme.S(64), Width - Theme.S(20), Theme.S(22)), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.Top);
    }
}
