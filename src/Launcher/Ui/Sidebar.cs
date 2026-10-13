using System.Drawing.Drawing2D;

namespace LanSkinTester.Launcher.Ui;

/// <summary>A navigation row in the sidebar: drawn icon + label, rounded highlight when selected.</summary>
public class SidebarItem : DrawnControl
{
    public enum Icon { Play, Skins, Setup }

    private readonly Icon _icon;
    private bool _selected;

    public SidebarItem(string text, Icon icon)
    {
        Text = text;
        _icon = icon;
        Size = new Size(204, 36);
        Margin = new Padding(0, 0, 0, 2);
        Cursor = Cursors.Hand;
    }

    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        if (_selected) Theme.FillRounded(g, Theme.Fill, r, 8);
        else if (Hover) Theme.FillRounded(g, Theme.WithAlpha(Theme.Fill, 110), r, 8);

        DrawIcon(g, new RectangleF(12, 9, 18, 18), _selected ? Theme.Accent : Theme.Muted);
        Theme.DrawText(g, Text, _selected ? Theme.Bold : Theme.Body, new Rectangle(40, 0, Width - 44, Height), Theme.Text);
    }

    private void DrawIcon(Graphics g, RectangleF box, Color color)
    {
        using var pen = new Pen(color, 1.7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var brush = new SolidBrush(color);
        switch (_icon)
        {
            case Icon.Play:
                // play.circle
                g.DrawEllipse(pen, box);
                g.FillPolygon(brush, [new PointF(box.Left + 7, box.Top + 5), new PointF(box.Left + 7, box.Bottom - 5), new PointF(box.Right - 4.5f, box.Top + 9)]);
                break;
            case Icon.Skins:
                // a gem / diamond outline
                var top = box.Top + 3;
                var mid = box.Top + 8;
                g.DrawPolygon(pen, [new PointF(box.Left + 4, top), new PointF(box.Right - 4, top), new PointF(box.Right, mid),
                    new PointF(box.Left + box.Width / 2, box.Bottom - 1), new PointF(box.Left, mid)]);
                g.DrawLine(pen, box.Left, mid, box.Right, mid);
                break;
            case Icon.Setup:
                // gearshape: ring with 8 teeth
                var c = new PointF(box.Left + box.Width / 2, box.Top + box.Height / 2);
                for (var i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4;
                    g.DrawLine(pen, c.X + (float)Math.Cos(a) * 6.5f, c.Y + (float)Math.Sin(a) * 6.5f,
                        c.X + (float)Math.Cos(a) * 9f, c.Y + (float)Math.Sin(a) * 9f);
                }
                g.DrawEllipse(pen, c.X - 6, c.Y - 6, 12, 12);
                g.DrawEllipse(pen, c.X - 2.5f, c.Y - 2.5f, 5, 5);
                break;
        }
    }
}

/// <summary>Status capsule at the bottom of the sidebar.</summary>
public class StatusBadge : DrawnControl
{
    private Color _dot = Theme.Good;
    private string _title = "";
    private string _detail = "";

    public StatusBadge()
    {
        Size = new Size(204, 52);
    }

    public void Set(Color dot, string title, string detail)
    {
        _dot = dot;
        _title = title;
        _detail = detail;
        Invalidate();
    }

    protected override void Draw(Graphics g)
    {
        Theme.FillRounded(g, Theme.Card, new RectangleF(0, 0, Width - 1, Height - 1), 10);
        using (var glow = new SolidBrush(Theme.WithAlpha(_dot, 60))) g.FillEllipse(glow, 11, Height / 2f - 9, 18, 18);
        using (var dot = new SolidBrush(_dot)) g.FillEllipse(dot, 15, Height / 2f - 5, 10, 10);
        Theme.DrawText(g, _title, Theme.Bold, new Rectangle(38, 8, Width - 44, 18), Theme.Text);
        Theme.DrawText(g, _detail, Theme.Small, new Rectangle(38, 26, Width - 44, 18), Theme.Muted);
    }
}
