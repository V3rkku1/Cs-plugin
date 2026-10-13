using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Ui;

/// <summary>A small square for one sticker/charm slot: "+" when empty, the picture when filled.</summary>
public class SlotTile : Control
{
    private readonly string _label;
    private string? _image;
    private ImageCache? _images;
    private bool _hover;
    private readonly ToolTip _tip = new();

    /// <summary>Right-click on a filled slot.</summary>
    public event EventHandler? Cleared;

    public SlotTile(string label)
    {
        _label = label;
        Size = new Size(52, 52);
        Margin = new Padding(0, 0, 6, 0);
        Cursor = Cursors.Hand;
        DoubleBuffered = true;
        BackColor = Theme.Card;
    }

    public bool IsFilled => _image != null;

    /// <param name="image">null = empty slot, "" = filled but no picture.</param>
    public void Set(string? image, string name, ImageCache images)
    {
        _image = image;
        _images = images;
        _tip.SetToolTip(this, image == null ? $"Add ({_label})" : $"{name}  (right-click to remove)");
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button == MouseButtons.Right && IsFilled) Cleared?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        if (e is MouseEventArgs { Button: MouseButtons.Right }) return;
        base.OnClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Background)) g.FillRectangle(bg, ClientRectangle);
        Theme.Smooth(g);
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, _hover ? Theme.FillHover : TextField.FieldColor, r, 10);

        if (_image == null)
        {
            // Dashed outline + plus sign, like an empty slot.
            using (var path = Theme.Rounded(RectangleF.Inflate(r, -2, -2), 9))
            using (var pen = new Pen(Theme.Tertiary, 1.2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawPath(pen, path);
            using var plus = new Pen(_hover ? Theme.Accent : Theme.Muted, 1.8f);
            var cx = Width / 2f;
            var cy = Height / 2f - 5;
            g.DrawLine(plus, cx - 6, cy, cx + 6, cy);
            g.DrawLine(plus, cx, cy - 6, cx, cy + 6);
            TextRenderer.DrawText(g, _label, Theme.Small, new Rectangle(0, Height - 20, Width, 16), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        using (var path = Theme.Rounded(r, 10))
        using (var ring = new Pen(Theme.Accent, 1.5f))
            g.DrawPath(ring, path);
        var img = _images?.Get(_image, 128, () => { if (IsHandleCreated) BeginInvoke(Invalidate); });
        if (img != null) g.DrawImage(img, CardGrid.Fit(img.Size, Rectangle.Inflate(Rectangle.Round(r), -5, -5)));
        else TextRenderer.DrawText(g, _label, Theme.Small, Rectangle.Round(r), Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
