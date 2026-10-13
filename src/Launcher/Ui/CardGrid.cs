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

    public Size CardSize { get; set; } = new(172, 150);
    public int Gap { get; set; } = 10;

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
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, _emptyText, Theme.Body, ClientRectangle, Theme.Muted,
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
        using (var path = Rounded(r, 6))
        {
            // Background: card colour with a soft glow of the rarity colour at the bottom.
            using var bg = new LinearGradientBrush(r, hover ? Theme.CardHover : Theme.Card,
                Blend(hover ? Theme.CardHover : Theme.Card, item.Accent, 0.22f), LinearGradientMode.Vertical);
            g.FillPath(bg, path);

            using var border = new Pen(selected ? Theme.Accent : hover ? Theme.Border : Theme.Card, selected ? 2 : 1);
            g.DrawPath(border, path);
        }

        // Rarity stripe
        using (var stripe = new SolidBrush(item.Accent))
            g.FillRectangle(stripe, r.Left + 6, r.Bottom - 4, r.Width - 12, 3);

        var imageRect = new Rectangle(r.Left + 10, r.Top + 6, r.Width - 20, r.Height - 52);
        var img = _images?.Get(item.ImageUrl, 256, () => { if (IsHandleCreated) BeginInvoke(Invalidate); });
        if (img != null) g.DrawImage(img, Fit(img.Size, imageRect));
        else
        {
            using var ph = new SolidBrush(Color.FromArgb(40, item.Accent));
            g.FillEllipse(ph, imageRect.Left + imageRect.Width / 2 - 26, imageRect.Top + imageRect.Height / 2 - 26, 52, 52);
        }

        var titleRect = new Rectangle(r.Left + 8, r.Bottom - 46, r.Width - 16, 20);
        TextRenderer.DrawText(g, item.Title, Theme.Bold, titleRect, Theme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
        var subRect = new Rectangle(r.Left + 8, r.Bottom - 27, r.Width - 16, 18);
        TextRenderer.DrawText(g, item.Subtitle, Theme.Small, subRect, Theme.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);

        if (item.Badge != "")
        {
            var size = TextRenderer.MeasureText(item.Badge, Theme.Small);
            var badge = new Rectangle(r.Right - size.Width - 12, r.Top + 6, size.Width + 6, 18);
            using var path = Rounded(badge, 4);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillPath(brush, path);
            TextRenderer.DrawText(g, item.Badge, Theme.Small, badge, Color.FromArgb(20, 16, 8),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
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
