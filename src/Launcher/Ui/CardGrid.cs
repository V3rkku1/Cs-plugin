using System.Drawing.Drawing2D;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Ui;

public class CardItem
{
    public object? Tag { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public Color Accent { get; init; } = Theme.Border;
    public string ImageUrl { get; init; } = "";
    public string Badge { get; set; } = "";
}

/// <summary>
/// A scrollable grid of item cards (picture, name, rarity stripe). Draws only the visible cards, so it
/// stays fast with thousands of items (the sticker list has 11,000+).
/// </summary>
public class CardGrid : Panel
{
    private List<CardItem> _items = new();
    private int _selected = -1;
    private int _hover = -1;
    private ImageCache? _images;
    private string _emptyText = "";

    public Size CardSize { get; set; } = new(176, 156);
    public int Gap { get; set; } = 12;

    public event EventHandler? SelectedChanged;
    public event EventHandler? ItemActivated;

    public CardGrid()
    {
        DoubleBuffered = true;
        AutoScroll = true;
        BackColor = Theme.Background;
        SetStyle(ControlStyles.Selectable | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
    }

    public void SetImageCache(ImageCache images) => _images = images;

    public CardItem? SelectedItem => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public void SetItems(IEnumerable<CardItem> items, string emptyText = "Nothing here")
    {
        _items = items.ToList();
        _selected = -1;
        _hover = -1;
        _emptyText = emptyText;
        AutoScrollPosition = Point.Empty;
        UpdateScrollSize();
        Invalidate();
    }

    public void Select(Func<CardItem, bool> predicate, bool scrollTo = true)
    {
        _selected = _items.FindIndex(i => predicate(i));
        if (scrollTo && _selected >= 0) ScrollToIndex(_selected);
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshBadges() => Invalidate();

    private int Columns => Math.Max(1, (ClientSize.Width - Gap) / (CardSize.Width + Gap));

    private Rectangle CardRect(int index)
    {
        var col = index % Columns;
        var row = index / Columns;
        // Centre the grid horizontally.
        var used = Columns * (CardSize.Width + Gap) - Gap;
        var left = Math.Max(Gap, (ClientSize.Width - used) / 2);
        return new Rectangle(left + col * (CardSize.Width + Gap), Gap + row * (CardSize.Height + Gap) + AutoScrollPosition.Y,
            CardSize.Width, CardSize.Height);
    }

    private void UpdateScrollSize()
    {
        var rows = (_items.Count + Columns - 1) / Columns;
        AutoScrollMinSize = new Size(0, Gap + rows * (CardSize.Height + Gap));
    }

    private void ScrollToIndex(int index)
    {
        var r = CardRect(index);
        if (r.Top < 0 || r.Bottom > ClientSize.Height)
            AutoScrollPosition = new Point(0, -AutoScrollPosition.Y + r.Top - Gap);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScrollSize();
        Invalidate();
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Invalidate();
    }

    private int HitTest(Point p)
    {
        for (var i = FirstVisible(); i < _items.Count; i++)
        {
            var r = CardRect(i);
            if (r.Top > ClientSize.Height) break;
            if (r.Contains(p)) return i;
        }
        return -1;
    }

    private int FirstVisible()
    {
        var row = Math.Max(0, (-AutoScrollPosition.Y - Gap) / (CardSize.Height + Gap));
        return row * Columns;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover) return;
        _hover = hit;
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        var hit = HitTest(e.Location);
        if (hit < 0) return;
        _selected = hit;
        Invalidate();
        SelectedChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        Theme.Smooth(g);

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, _emptyText, Theme.Heading, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        for (var i = FirstVisible(); i < _items.Count; i++)
        {
            var r = CardRect(i);
            if (r.Top > ClientSize.Height) break;
            if (r.Bottom < 0) continue;
            DrawCard(g, _items[i], r, i == _selected, i == _hover);
        }
    }

    private void DrawCard(Graphics g, CardItem item, Rectangle r, bool selected, bool hover)
    {
        var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        Theme.FillRounded(g, hover ? Theme.CardHover : Theme.Card, rf, 12);

        // Soft spotlight in the rarity colour behind the picture.
        var imageRect = new Rectangle(r.Left + 12, r.Top + 10, r.Width - 24, r.Height - 58);
        using (var glowPath = new GraphicsPath())
        {
            var glow = RectangleF.Inflate(imageRect, -imageRect.Width * 0.12f, -imageRect.Height * 0.05f);
            glowPath.AddEllipse(glow);
            using var brush = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(hover ? 70 : 45, item.Accent),
                SurroundColors = [Color.FromArgb(0, item.Accent)],
            };
            g.FillPath(brush, glowPath);
        }

        var img = _images?.Get(item.ImageUrl, 256, () => { if (IsHandleCreated) BeginInvoke(Invalidate); });
        if (img != null) g.DrawImage(img, Fit(img.Size, imageRect));
        else
        {
            using var ph = new SolidBrush(Color.FromArgb(30, item.Accent));
            g.FillEllipse(ph, imageRect.Left + imageRect.Width / 2 - 24, imageRect.Top + imageRect.Height / 2 - 24, 48, 48);
        }

        Theme.DrawText(g, item.Title, Theme.Bold, new Rectangle(r.Left + 12, r.Bottom - 46, r.Width - 24, 20), Theme.Text);

        // Rarity: small dot + name, like a tag.
        using (var dot = new SolidBrush(item.Accent)) g.FillEllipse(dot, r.Left + 13, r.Bottom - 20, 7, 7);
        Theme.DrawText(g, item.Subtitle, Theme.Small, new Rectangle(r.Left + 25, r.Bottom - 26, r.Width - 37, 18), Theme.Muted);

        if (selected)
        {
            using var path = Theme.Rounded(RectangleF.Inflate(rf, -1, -1), 11);
            using var ring = new Pen(Theme.Accent, 2.5f);
            g.DrawPath(ring, path);
        }

        if (item.Badge != "")
        {
            var size = TextRenderer.MeasureText(item.Badge, Theme.Small);
            var badge = new RectangleF(r.Right - size.Width - 18, r.Top + 9, size.Width + 8, 20);
            Theme.FillRounded(g, Theme.Accent, badge, 10);
            Theme.DrawText(g, item.Badge, Theme.Small, Rectangle.Round(badge), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    public static Rectangle Fit(Size image, Rectangle box)
    {
        var scale = Math.Min((float)box.Width / image.Width, (float)box.Height / image.Height);
        var w = (int)(image.Width * scale);
        var h = (int)(image.Height * scale);
        return new Rectangle(box.Left + (box.Width - w) / 2, box.Top + (box.Height - h) / 2, w, h);
    }

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));
}
