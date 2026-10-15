namespace LanSkinTester.Launcher.Ui;

/// <summary>macOS segmented control: one rounded track, the chosen segment raised. Can wrap into a grid (Columns).</summary>
public class Segmented : DrawnControl
{
    private readonly string[] _options;
    private readonly bool[] _enabled;
    private int _selected = -1;
    private int _hover = -1;
    private const int RowHeight = 28;

    public int Columns { get; }

    public event EventHandler? SelectedChanged;

    public Segmented(IEnumerable<string> options, int segmentWidth = 0, int columns = 0)
    {
        _options = options.ToArray();
        _enabled = _options.Select(_ => true).ToArray();
        Columns = columns > 0 ? columns : _options.Length;
        var width = segmentWidth > 0 ? segmentWidth : (int)Math.Ceiling(_options.Max(o => Theme.MeasureText(o, Theme.Body).Width) / Theme.Scale) + 24;
        var rows = (_options.Length + Columns - 1) / Columns;
        Size = new Size(width * Columns + 4, rows * RowHeight + 4);
        Margin = new Padding(0, 0, 8, 0);
        Cursor = Cursors.Hand;
        SelectedIndex = 0;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _options.Length) return;
            var changed = value != _selected;
            _selected = value;
            Invalidate();
            if (changed) SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedText => _options[_selected];

    public void SetEnabled(int index, bool enabled)
    {
        _enabled[index] = enabled;
        Invalidate();
    }

    private RectangleF Segment(int i)
    {
        var w = (Width - 4f) / Columns;
        return new RectangleF(2 + i % Columns * w, 2 + i / Columns * RowHeight, w, RowHeight);
    }

    private int HitTest(Point p)
    {
        for (var i = 0; i < _options.Length; i++)
            if (Segment(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit != _hover) { _hover = hit; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = -1; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var hit = HitTest(e.Location);
        if (hit >= 0 && _enabled[hit] && Enabled) SelectedIndex = hit;
    }

    protected override void Draw(Graphics g)
    {
        var track = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.FillRounded(g, Theme.Input, track, 9);
        using (var outline = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 9))
        using (var pen = new Pen(Theme.Separator))
            g.DrawPath(pen, outline);

        for (var i = 0; i < _options.Length; i++)
        {
            var r = Segment(i);
            var on = i == _selected;
            if (on)
            {
                var raised = RectangleF.Inflate(r, -1, -1);
                Theme.FillRounded(g, Enabled ? Theme.Accent : Theme.Blend(Theme.Accent, Theme.Input, 0.55f), raised, 7);
            }
            else if (i == _hover && _enabled[i] && Enabled)
            {
                Theme.FillRounded(g, Theme.Fill, RectangleF.Inflate(r, -1, -1), 7);
            }
            else if (i > 0 && i % Columns != 0 && i - 1 != _selected)
            {
                using var pen = new Pen(Theme.Separator);
                g.DrawLine(pen, r.Left, r.Top + 7, r.Left, r.Bottom - 7);
            }

            var color = on ? (Enabled ? Color.White : Theme.Muted) : !_enabled[i] || !Enabled ? Theme.Tertiary : Theme.Text;
            Theme.DrawText(g, _options[i], on ? Theme.Bold : Theme.Body, Rectangle.Round(r), color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
