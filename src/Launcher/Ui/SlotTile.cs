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
        Size = new Size(50, 50);
        Margin = new Padding(0, 0, 5, 0);
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = CardGrid.Rounded(r, 5))
        {
            using var bg = new SolidBrush(_hover ? Theme.CardHover : Theme.Card);
            g.FillPath(bg, path);
            using var pen = new Pen(IsFilled ? Theme.Accent : Theme.Border) { DashStyle = IsFilled ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawPath(pen, path);
        }

        if (_image == null)
        {
            TextRenderer.DrawText(g, "+", Theme.Heading, new Rectangle(0, 0, Width, Height - 12), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _label, Theme.Small, new Rectangle(0, Height - 20, Width, 16), Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            return;
        }

        var img = _images?.Get(_image, 128, () => { if (IsHandleCreated) BeginInvoke(Invalidate); });
        if (img != null) g.DrawImage(img, CardGrid.Fit(img.Size, Rectangle.Inflate(r, -4, -4)));
        else TextRenderer.DrawText(g, _label, Theme.Small, r, Theme.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}
