using System.Drawing.Drawing2D;

namespace LanSkinTester.Launcher.Ui;

/// <summary>A navigation row in the sidebar: drawn icon + label, rounded highlight when selected.</summary>
public class SidebarItem : DrawnControl
{
    public enum Icon { Play, Live, Skins, Hacker, Friends, Setup }

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
        // iPadOS sidebar: the selected page is a filled systemBlue row with white icon and text.
        if (_selected) Theme.FillRounded(g, Theme.Accent, r, 10);
        else if (Hover) Theme.FillRounded(g, Theme.WithAlpha(Theme.Fill, 110), r, 10);

        var state = g.Save();
        g.TranslateTransform(Theme.S(12f), (Height - Theme.S(18f)) / 2);
        g.ScaleTransform(Theme.Scale, Theme.Scale);
        DrawIcon(g, new RectangleF(0, 0, 18, 18), _selected ? Color.White : Theme.Accent);
        g.Restore(state);
        Theme.DrawText(g, Text, _selected ? Theme.Bold : Theme.Body, new Rectangle(Theme.S(40), 0, Width - Theme.S(44), Height), _selected ? Color.White : Theme.Text);
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
            case Icon.Live:
                // a server rack: three stacked bars and a status dot
                g.DrawRectangle(pen, box.Left + 2, box.Top + 2, 11, 4);
                g.DrawRectangle(pen, box.Left + 2, box.Top + 7.5f, 11, 4);
                g.DrawRectangle(pen, box.Left + 2, box.Top + 13, 11, 4);
                g.FillEllipse(brush, box.Right - 5, box.Top + 3, 3.2f, 3.2f);
                break;
            case Icon.Skins:
                // a gem / diamond outline
                var top = box.Top + 3;
                var mid = box.Top + 8;
                g.DrawPolygon(pen, [new PointF(box.Left + 4, top), new PointF(box.Right - 4, top), new PointF(box.Right, mid),
                    new PointF(box.Left + box.Width / 2, box.Bottom - 1), new PointF(box.Left, mid)]);
                g.DrawLine(pen, box.Left, mid, box.Right, mid);
                break;
            case Icon.Hacker:
                // scope: ring, four ticks and a centre dot
                var m = new PointF(box.Left + box.Width / 2, box.Top + box.Height / 2);
                g.DrawEllipse(pen, m.X - 6.5f, m.Y - 6.5f, 13, 13);
                g.DrawLine(pen, m.X, m.Y - 9.5f, m.X, m.Y - 4);
                g.DrawLine(pen, m.X, m.Y + 4, m.X, m.Y + 9.5f);
                g.DrawLine(pen, m.X - 9.5f, m.Y, m.X - 4, m.Y);
                g.DrawLine(pen, m.X + 4, m.Y, m.X + 9.5f, m.Y);
                g.FillEllipse(brush, m.X - 1.5f, m.Y - 1.5f, 3, 3);
                break;
            case Icon.Friends:
                // person.2: two heads and shoulders
                g.DrawEllipse(pen, box.Left + 2, box.Top + 2, 6.5f, 6.5f);
                g.DrawArc(pen, box.Left - 1, box.Top + 11, 12.5f, 12, 180, 180);
                g.DrawEllipse(pen, box.Left + 10, box.Top + 3.5f, 5.5f, 5.5f);
                g.DrawArc(pen, box.Left + 9.5f, box.Top + 12, 9.5f, 9, 200, 140);
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

/// <summary>"Update available" card above the status badge; click opens the download page.</summary>
public class UpdateCard : DrawnControl
{
    private string _title = "Update available";
    private string _detail = "Click to install";

    public void Set(string title, string detail)
    {
        _title = title;
        _detail = detail;
        Invalidate();
    }

    public UpdateCard()
    {
        Size = new Size(204, 52);
        Cursor = Cursors.Hand;
        Visible = false;
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, Hover ? Theme.AccentHover : Theme.Accent, r, 10);
        // arrow.down.circle
        var state = g.Save();
        g.ScaleTransform(Theme.Scale, Theme.Scale);
        var h = Height / Theme.Scale;
        using (var pen = new Pen(Color.White, 1.7f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
        {
            g.DrawEllipse(pen, 12, h / 2f - 9, 18, 18);
            g.DrawLine(pen, 21, h / 2f - 5, 21, h / 2f + 4);
            g.DrawLine(pen, 17, h / 2f, 21, h / 2f + 4);
            g.DrawLine(pen, 25, h / 2f, 21, h / 2f + 4);
        }
        g.Restore(state);
        Theme.DrawText(g, _title, Theme.Bold, new Rectangle(Theme.S(40), Theme.S(8), Width - Theme.S(44), Theme.S(18)), Color.White);
        Theme.DrawText(g, _detail, Theme.Small, new Rectangle(Theme.S(40), Theme.S(26), Width - Theme.S(44), Theme.S(18)), Color.FromArgb(220, 235, 255));
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
        Theme.FillRounded(g, Theme.CardHover, new RectangleF(0, 0, Width - 1, Height - 1), 12); // sits on the sidebar grey
        var mid = Height / 2f;
        using (var glow = new SolidBrush(Theme.WithAlpha(_dot, 60))) g.FillEllipse(glow, Theme.S(11f), mid - Theme.S(9f), Theme.S(18f), Theme.S(18f));
        using (var dot = new SolidBrush(_dot)) g.FillEllipse(dot, Theme.S(15f), mid - Theme.S(5f), Theme.S(10f), Theme.S(10f));
        Theme.DrawText(g, _title, Theme.Bold, new Rectangle(Theme.S(38), Theme.S(8), Width - Theme.S(44), Theme.S(18)), Theme.Text);
        Theme.DrawText(g, _detail, Theme.Small, new Rectangle(Theme.S(38), Theme.S(26), Width - Theme.S(44), Theme.S(18)), Theme.Muted);
    }
}
