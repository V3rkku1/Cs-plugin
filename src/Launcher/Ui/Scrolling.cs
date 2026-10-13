namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// macOS-style overlay scrollbar logic shared by the self-scrolling controls: keeps the offset, draws a thin
/// rounded thumb (wider when hovered) and handles dragging it. Replaces the white Windows scrollbars.
/// </summary>
public class OverlayScroller
{
    private readonly Control _owner;
    private bool _dragging;
    private int _dragStartY;
    private int _dragStartOffset;
    private bool _hot;

    public int Offset { get; private set; }
    public int ContentHeight { get; set; }
    public int ViewHeight => _owner.ClientSize.Height;
    public int MaxOffset => Math.Max(0, ContentHeight - ViewHeight);
    public bool CanScroll => ContentHeight > ViewHeight;

    public event Action? Scrolled;

    public OverlayScroller(Control owner) => _owner = owner;

    public void ScrollTo(int offset)
    {
        offset = Math.Clamp(offset, 0, MaxOffset);
        if (offset == Offset) return;
        Offset = offset;
        Scrolled?.Invoke();
    }

    public void Clamp() => ScrollTo(Offset);

    public void Wheel(int delta) => ScrollTo(Offset - delta / 120 * 64);

    private RectangleF Thumb()
    {
        var track = ViewHeight - 8f;
        var size = Math.Max(36f, track * ViewHeight / Math.Max(ContentHeight, 1));
        var y = 4 + (track - size) * (MaxOffset == 0 ? 0 : (float)Offset / MaxOffset);
        var width = _hot || _dragging ? 8f : 5f;
        return new RectangleF(_owner.ClientSize.Width - width - 3, y, width, size);
    }

    /// <summary>The strip on the right where the thumb lives (mouse there = scrollbar, not content).</summary>
    public bool InGutter(Point p) => CanScroll && p.X >= _owner.ClientSize.Width - 14;

    public bool MouseDown(MouseEventArgs e)
    {
        if (!InGutter(e.Location)) return false;
        var thumb = Thumb();
        if (thumb.Top <= e.Y && e.Y <= thumb.Bottom)
        {
            _dragging = true;
            _dragStartY = e.Y;
            _dragStartOffset = Offset;
        }
        else
        {
            // Click on the track: page up/down.
            ScrollTo(Offset + (e.Y < thumb.Top ? -ViewHeight : ViewHeight) * 9 / 10);
        }
        _owner.Invalidate();
        return true;
    }

    public bool MouseMove(MouseEventArgs e)
    {
        var hot = InGutter(e.Location);
        if (hot != _hot)
        {
            _hot = hot;
            _owner.Invalidate(new Rectangle(_owner.ClientSize.Width - 16, 0, 16, ViewHeight));
        }
        if (!_dragging) return hot;

        var track = ViewHeight - 8f - Thumb().Height;
        if (track > 0) ScrollTo(_dragStartOffset + (int)((e.Y - _dragStartY) * MaxOffset / track));
        return true;
    }

    public void MouseUp()
    {
        _dragging = false;
        _owner.Invalidate();
    }

    public void MouseLeave()
    {
        if (_dragging) return;
        _hot = false;
        _owner.Invalidate();
    }

    public void Paint(Graphics g)
    {
        if (!CanScroll) return;
        Theme.Smooth(g);
        var thumb = Thumb();
        if (_hot || _dragging)
            Theme.FillRounded(g, Theme.WithAlpha(Color.White, 18), new RectangleF(thumb.X - 2, 2, thumb.Width + 4, ViewHeight - 4), 6);
        Theme.FillRounded(g, Theme.WithAlpha(Color.White, _hot || _dragging ? 150 : 90), thumb, thumb.Width / 2);
    }
}

// =================================================================================================

/// <summary>
/// A scrolling container with the overlay scrollbar. Put one control in <see cref="Content"/> (e.g. a page's
/// stack of groups); it is moved up/down instead of using native scrollbars.
/// </summary>
public class ScrollPanel : Panel
{
    private readonly OverlayScroller _scroller;
    private Control? _content;
    private readonly Gutter _gutter;

    public ScrollPanel()
    {
        BackColor = Theme.Background;
        DoubleBuffered = true;
        _scroller = new OverlayScroller(this);
        _scroller.Scrolled += Place;
        _gutter = new Gutter(this);
        Controls.Add(_gutter);
    }

    public Control? Content
    {
        get => _content;
        set
        {
            if (_content != null) Controls.Remove(_content);
            _content = value;
            if (value == null) return;
            Controls.Add(value);
            _gutter.BringToFront();
            value.SizeChanged += (_, _) => Measure();
            HookWheel(value);
            Measure();
        }
    }

    // Children don't scroll themselves, so forward their mouse wheel here.
    private void HookWheel(Control control)
    {
        if (control is TextBox { Multiline: true }) return; // logs scroll on their own
        control.MouseWheel += OnChildWheel;
        control.ControlAdded += (_, e) => { if (e.Control != null) HookWheel(e.Control); };
        foreach (Control child in control.Controls) HookWheel(child);
    }

    private void OnChildWheel(object? sender, MouseEventArgs e)
    {
        if (sender is NumberField or Picker && ((Control)sender).ContainsFocus) return;
        _scroller.Wheel(e.Delta);
        if (e is HandledMouseEventArgs h) h.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroller.Wheel(e.Delta);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Measure();
    }

    private void Measure()
    {
        if (_content == null) return;
        _scroller.ContentHeight = _content.Height + Padding.Vertical;
        _scroller.Clamp();
        Place();
    }

    private void Place()
    {
        if (_content == null) return;
        _content.Location = new Point(Padding.Left, Padding.Top - _scroller.Offset);
        _gutter.SetBounds(Width - 16, 0, 16, Height);
        _gutter.Visible = _scroller.CanScroll;
        _gutter.Invalidate();
    }

    /// <summary>Right-edge strip that draws the thumb above the content and takes the mouse for dragging.</summary>
    private sealed class Gutter : Control
    {
        private readonly ScrollPanel _panel;

        public Gutter(ScrollPanel panel)
        {
            _panel = panel;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Background;
        }

        private MouseEventArgs Translate(MouseEventArgs e) => new(e.Button, e.Clicks, e.X + Left, e.Y, e.Delta);

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _panel._scroller.MouseDown(Translate(e)); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); _panel._scroller.MouseMove(Translate(e)); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _panel._scroller.MouseUp(); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _panel._scroller.MouseLeave(); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _panel._scroller.Wheel(e.Delta); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var bg = new SolidBrush(_panel.BackColor)) e.Graphics.FillRectangle(bg, ClientRectangle);
            e.Graphics.TranslateTransform(-Left, 0);
            _panel._scroller.Paint(e.Graphics);
        }
    }
}

// =================================================================================================

/// <summary>Owner-drawn list with the overlay scrollbar (replaces ListBox and its white scrollbar).</summary>
public class RowList : Control
{
    private readonly OverlayScroller _scroller;
    private int _selected = -1;
    private int _hover = -1;

    public List<object> Items { get; } = new();
    public int RowHeight { get; set; } = 34;

    /// <summary>Draws one row: (graphics, item, bounds, selected, hovered).</summary>
    public Action<Graphics, object, Rectangle, bool, bool>? DrawRow { get; set; }

    public event EventHandler? SelectedIndexChanged;
    public event EventHandler? ItemActivated;

    public RowList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Sidebar;
        _scroller = new OverlayScroller(this);
        _scroller.Scrolled += Invalidate;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < -1 || value >= Items.Count) return;
            var changed = value != _selected;
            _selected = value;
            EnsureVisible(value);
            Invalidate();
            if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem => _selected >= 0 && _selected < Items.Count ? Items[_selected] : null;

    public void SetItems(IEnumerable<object> items)
    {
        Items.Clear();
        Items.AddRange(items);
        _selected = -1;
        _hover = -1;
        _scroller.ContentHeight = Items.Count * RowHeight;
        _scroller.ScrollTo(0);
        Invalidate();
    }

    public void Remove(object item)
    {
        var index = Items.IndexOf(item);
        if (index < 0) return;
        Items.RemoveAt(index);
        _scroller.ContentHeight = Items.Count * RowHeight;
        _scroller.Clamp();
        _selected = Math.Min(_selected, Items.Count - 1);
        Invalidate();
        SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureVisible(int index)
    {
        if (index < 0) return;
        var top = index * RowHeight;
        if (top < _scroller.Offset) _scroller.ScrollTo(top);
        else if (top + RowHeight > _scroller.Offset + ClientSize.Height) _scroller.ScrollTo(top + RowHeight - ClientSize.Height);
    }

    private int HitTest(Point p) => p.Y < 0 ? -1 : (p.Y + _scroller.Offset) / RowHeight is var i && i < Items.Count ? i : -1;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        _scroller.ContentHeight = Items.Count * RowHeight;
        _scroller.Clamp();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroller.Wheel(e.Delta);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (_scroller.MouseDown(e)) return;
        var hit = HitTest(e.Location);
        if (hit >= 0) SelectedIndex = hit;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_scroller.MouseMove(e)) return;
        var hit = HitTest(e.Location);
        if (hit != _hover) { _hover = hit; Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _scroller.MouseUp(); }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _scroller.MouseLeave();
        Invalidate();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) >= 0) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(_selected + 1, Items.Count - 1);
        if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(_selected - 1, 0);
        if (e.KeyCode == Keys.Enter) ItemActivated?.Invoke(this, EventArgs.Empty);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var first = _scroller.Offset / RowHeight;
        for (var i = first; i < Items.Count; i++)
        {
            var r = new Rectangle(0, i * RowHeight - _scroller.Offset, ClientSize.Width - (_scroller.CanScroll ? 12 : 0), RowHeight);
            if (r.Top > ClientSize.Height) break;
            DrawRow?.Invoke(g, Items[i], r, i == _selected, i == _hover);
        }
        _scroller.Paint(g);
    }
}
