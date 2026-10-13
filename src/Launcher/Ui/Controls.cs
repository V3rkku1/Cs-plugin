using System.Diagnostics.CodeAnalysis;
using System.ComponentModel;
using System.Globalization;

namespace LanSkinTester.Launcher.Ui;

/// <summary>Base for self-drawn controls: transparent (the rounded group behind shows through), double-buffered.</summary>
public abstract class DrawnControl : Control
{
    protected bool Hover;
    protected bool Pressed;

    protected DrawnControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.SupportsTransparentBackColor | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
        Font = Theme.Body;
    }

    protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hover = Pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Smooth(e.Graphics);
        Draw(e.Graphics);
    }

    protected abstract void Draw(Graphics g);
}

// =================================================================================================

/// <summary>Rounded push button. Primary = blue fill, Secondary = grey fill, Destructive = red text, Plain = blue text.</summary>
public class PillButton : DrawnControl, IButtonControl
{
    public enum Style { Primary, Secondary, Destructive, Plain }

    public Style ButtonStyle { get; set; }
    [DefaultValue(true)] public bool AutoWidth { get; set; } = true;
    public float Radius { get; set; } = 8;
    public DialogResult DialogResult { get; set; }

    public PillButton(string text, Style style = Style.Secondary)
    {
        ButtonStyle = style;
        Font = style == Style.Primary ? Theme.Bold : Theme.Body;
        Height = 32;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 0, 8, 0);
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Text = text;
        FitWidth();
    }

    public new int Width
    {
        get => base.Width;
        set { AutoWidth = false; base.Width = value; }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitWidth();
        Invalidate();
    }

    private void FitWidth()
    {
        if (!AutoWidth) return;
        base.Width = TextRenderer.MeasureText(Text, Font).Width + 30;
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var (fill, text) = ButtonStyle switch
        {
            Style.Primary => (Pressed ? Theme.Blend(Theme.Accent, Color.Black, 0.15f) : Hover ? Theme.AccentHover : Theme.Accent, Color.White),
            Style.Destructive => (Pressed ? Theme.FillHover : Hover ? Theme.FillHover : Theme.Fill, Theme.Bad),
            Style.Plain => (Hover ? Theme.WithAlpha(Theme.Fill, 160) : Color.Transparent, Theme.Accent),
            _ => (Pressed ? Theme.Blend(Theme.FillHover, Color.White, 0.08f) : Hover ? Theme.FillHover : Theme.Fill, Theme.Text),
        };
        if (!Enabled)
        {
            fill = ButtonStyle == Style.Plain ? Color.Transparent : Theme.WithAlpha(Theme.Fill, 110);
            text = Theme.Tertiary;
        }
        if (fill.A > 0) Theme.FillRounded(g, fill, r, Radius);
        if (Focused && ShowFocusCues)
        {
            using var path = Theme.Rounded(r, Radius);
            using var pen = new Pen(Theme.WithAlpha(Theme.Accent, 160), 2);
            g.DrawPath(pen, path);
        }
        Theme.DrawText(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) PerformClick();
    }

    public void NotifyDefault(bool value) { }

    public void PerformClick()
    {
        if (CanSelect || Enabled) OnClick(EventArgs.Empty);
    }

    protected override void OnClick(EventArgs e)
    {
        if (!Enabled) return;
        base.OnClick(e);
        var form = FindForm();
        if (form != null && DialogResult != DialogResult.None) form.DialogResult = DialogResult;
    }
}

// =================================================================================================

/// <summary>iOS/macOS switch.</summary>
public class Toggle : DrawnControl
{
    private bool _checked;
    private float _anim; // 0 = off, 1 = on
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };

    public event EventHandler? CheckedChanged;

    public Toggle()
    {
        Size = new Size(44, 26);
        Cursor = Cursors.Hand;
        Margin = new Padding(0);
        _timer.Tick += (_, _) =>
        {
            var target = _checked ? 1f : 0f;
            _anim += Math.Sign(target - _anim) * 0.2f;
            if (Math.Abs(_anim - target) < 0.2f) { _anim = target; _timer.Stop(); }
            Invalidate();
        };
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated && Visible) _timer.Start();
            else _anim = value ? 1 : 0;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (Enabled) Checked = !Checked;
        base.OnClick(e);
    }

    protected override void Draw(Graphics g)
    {
        var track = new RectangleF(1, 2, Width - 3, Height - 5);
        var color = Theme.Blend(Theme.Fill, Theme.Good, _anim);
        if (!Enabled) color = Theme.WithAlpha(color, 100);
        Theme.FillRounded(g, color, track, track.Height / 2);

        var d = track.Height - 4;
        var x = track.Left + 2 + (track.Width - d - 4) * _anim;
        using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillEllipse(shadow, x, track.Top + 3, d, d);
        using var knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(160, 160, 160));
        g.FillEllipse(knob, x, track.Top + 2, d, d);
    }
}

// =================================================================================================

/// <summary>Thin track, blue fill, white knob.</summary>
public class Slider : DrawnControl
{
    private int _value;
    private bool _dragging;

    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;

    /// <summary>Raised for every change, including ones from code.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised only when the user drags or clicks.</summary>
    public event EventHandler? Scroll;

    public Slider()
    {
        Size = new Size(200, 28);
        Cursor = Cursors.Hand;
    }

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, Minimum, Maximum);
            if (_value == value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float Fraction => Maximum == Minimum ? 0 : (float)(_value - Minimum) / (Maximum - Minimum);
    private const int Knob = 18;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled || e.Button != MouseButtons.Left) return;
        _dragging = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
    }

    private void SetFromX(int x)
    {
        var f = Math.Clamp((x - Knob / 2f) / (Width - Knob), 0f, 1f);
        var old = _value;
        Value = Minimum + (int)Math.Round(f * (Maximum - Minimum));
        if (old != _value) Scroll?.Invoke(this, EventArgs.Empty);
    }

    protected override void Draw(Graphics g)
    {
        var cy = Height / 2f;
        var track = new RectangleF(Knob / 2f, cy - 2, Width - Knob, 4);
        Theme.FillRounded(g, Theme.Fill, track, 2);
        var fill = new RectangleF(track.Left, track.Top, track.Width * Fraction, track.Height);
        Theme.FillRounded(g, Enabled ? Theme.Accent : Theme.Tertiary, fill, 2);

        var x = track.Left + track.Width * Fraction - Knob / 2f;
        using (var shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0))) g.FillEllipse(shadow, x, cy - Knob / 2f + 1, Knob, Knob);
        using var knob = new SolidBrush(Enabled ? Color.White : Color.FromArgb(150, 150, 150));
        g.FillEllipse(knob, x, cy - Knob / 2f, Knob, Knob);
    }
}

// =================================================================================================

/// <summary>Rounded text field (wraps a borderless TextBox).</summary>
public class TextField : DrawnControl
{
    public TextBox Box { get; } = new()
    {
        BorderStyle = BorderStyle.None, BackColor = FieldColor, ForeColor = Theme.Text, Font = Theme.Body,
    };

    public static readonly Color FieldColor = Color.FromArgb(44, 44, 47);

    public new event EventHandler? TextChanged;

    public TextField(int width = 220, bool multiline = false, int height = 32)
    {
        Size = new Size(width, multiline ? height : 32);
        Margin = new Padding(0, 0, 8, 0);
        Box.Multiline = multiline;
        // No native (white) scrollbar: the mouse wheel still scrolls, and logs follow new lines.
        if (multiline) Box.ScrollBars = ScrollBars.None;
        Controls.Add(Box);
        Box.TextChanged += (_, _) => TextChanged?.Invoke(this, EventArgs.Empty);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Cursor = Cursors.IBeam;
        PlaceBox();
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); PlaceBox(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PlaceBox(); }

    private void PlaceBox()
    {
        if (Box.Multiline) Box.SetBounds(10, 8, Width - 16, Height - 14);
        else Box.SetBounds(10, (Height - Box.PreferredHeight) / 2 + 1, Width - 20, Box.PreferredHeight);
    }

    [AllowNull] public override string Text { get => Box.Text; set => Box.Text = value; }
    public string PlaceholderText { get => Box.PlaceholderText; set => Box.PlaceholderText = value; }
    public bool ReadOnly { get => Box.ReadOnly; set => Box.ReadOnly = value; }
    public int MaxLength { get => Box.MaxLength; set => Box.MaxLength = value; }
    public new Font Font { get => Box.Font; set => Box.Font = value; }

    public void AppendText(string text) => Box.AppendText(text);

    protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }
    protected override void OnEnabledChanged(EventArgs e) { Box.Enabled = Enabled; base.OnEnabledChanged(e); }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, FieldColor, r, 8);
        using var path = Theme.Rounded(r, 8);
        using var pen = new Pen(Box.Focused ? Theme.Accent : Theme.WithAlpha(Color.White, 22), Box.Focused ? 2 : 1);
        g.DrawPath(pen, path);
    }
}

// =================================================================================================

/// <summary>Number field with − / + buttons (replaces NumericUpDown).</summary>
public class NumberField : DrawnControl
{
    private readonly TextBox _box = new()
    {
        BorderStyle = BorderStyle.None, BackColor = TextField.FieldColor, ForeColor = Theme.Text, Font = Theme.Body, TextAlign = HorizontalAlignment.Center,
    };
    private decimal _value;
    private int _hot; // -1 minus, 1 plus

    public decimal Minimum { get; set; }
    public decimal Maximum { get; set; } = 100;
    public decimal Increment { get; set; } = 1;
    public int DecimalPlaces { get; set; }

    public event EventHandler? ValueChanged;

    public NumberField(int width = 120)
    {
        Size = new Size(width, 32);
        Margin = new Padding(0, 0, 8, 0);
        Controls.Add(_box);
        _box.Leave += (_, _) => Commit();
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Up) { Step(1); e.Handled = true; }
            if (e.KeyCode == Keys.Down) { Step(-1); e.Handled = true; }
        };
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        PlaceBox();
        ShowValue();
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); PlaceBox(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PlaceBox(); }

    private void PlaceBox() => _box?.SetBounds(30, (Height - _box.PreferredHeight) / 2 + 1, Width - 60, _box.PreferredHeight);

    public decimal Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(Math.Round(value, DecimalPlaces), Minimum, Maximum);
            var changed = value != _value;
            _value = value;
            ShowValue();
            if (changed) ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowValue() => _box.Text = _value.ToString("F" + DecimalPlaces, CultureInfo.InvariantCulture);

    private void Commit()
    {
        var text = _box.Text.Replace(',', '.');
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) Value = v;
        else ShowValue();
    }

    private void Step(int direction)
    {
        Commit();
        Value = _value + direction * Increment;
    }

    protected override void OnEnabledChanged(EventArgs e) { _box.Enabled = Enabled; base.OnEnabledChanged(e); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = e.X < 30 ? -1 : e.X > Width - 30 ? 1 : 0;
        if (hot != _hot) { _hot = hot; Cursor = hot != 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { _hot = 0; base.OnMouseLeave(e); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!Enabled) return;
        if (e.X < 30) Step(-1);
        else if (e.X > Width - 30) Step(1);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Enabled && _box.Focused) Step(Math.Sign(e.Delta));
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, TextField.FieldColor, r, 8);
        using (var path = Theme.Rounded(r, 8))
        using (var pen = new Pen(_box.Focused ? Theme.Accent : Theme.WithAlpha(Color.White, 22), _box.Focused ? 2 : 1))
            g.DrawPath(pen, path);

        var color = Enabled ? Theme.Text : Theme.Tertiary;
        if (_hot == -1 && Enabled) Theme.FillRounded(g, Theme.Fill, new RectangleF(3, 3, 24, Height - 6), 6);
        if (_hot == 1 && Enabled) Theme.FillRounded(g, Theme.Fill, new RectangleF(Width - 27, 3, 24, Height - 6), 6);
        using var pen2 = new Pen(color, 1.6f);
        var cy = Height / 2f;
        g.DrawLine(pen2, 10, cy, 20, cy);
        g.DrawLine(pen2, Width - 20, cy, Width - 10, cy);
        g.DrawLine(pen2, Width - 15, cy - 5, Width - 15, cy + 5);
    }
}

// =================================================================================================

/// <summary>Pop-up button (replaces ComboBox). Optional free text (Editable) for e.g. custom map names.</summary>
public class Picker : DrawnControl
{
    private readonly TextBox? _box;
    private int _selected = -1;

    public List<object> Items { get; } = new();
    public event EventHandler? SelectedIndexChanged;

    public Picker(int width = 220, bool editable = false)
    {
        Size = new Size(width, 32);
        Margin = new Padding(0, 0, 8, 0);
        Cursor = Cursors.Hand;
        if (editable)
        {
            _box = new TextBox { BorderStyle = BorderStyle.None, BackColor = TextField.FieldColor, ForeColor = Theme.Text, Font = Theme.Body };
            Controls.Add(_box);
            PlaceBox();
        }
    }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); PlaceBox(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PlaceBox(); }

    private void PlaceBox() => _box?.SetBounds(10, (Height - _box.PreferredHeight) / 2 + 1, Width - 44, _box.PreferredHeight);

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < -1 || value >= Items.Count) return;
            var changed = value != _selected;
            _selected = value;
            if (_box != null && value >= 0) _box.Text = Items[value].ToString();
            Invalidate();
            if (changed) SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public object? SelectedItem
    {
        get => _selected >= 0 ? Items[_selected] : null;
        set => SelectedIndex = value == null ? -1 : Items.IndexOf(value);
    }

    [AllowNull]
    public override string Text
    {
        get => _box?.Text ?? SelectedItem?.ToString() ?? "";
        set
        {
            if (_box != null) _box.Text = value;
            else SelectedIndex = Items.FindIndex(i => i.ToString() == value);
        }
    }

    public void SetItems(IEnumerable<object> items)
    {
        Items.Clear();
        Items.AddRange(items);
        _selected = -1;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e) { if (_box != null) _box.Enabled = Enabled; base.OnEnabledChanged(e); }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        if (!Enabled || Items.Count == 0) return;
        if (_box != null && PointToClient(MousePosition).X < Width - 34) { _box.Focus(); return; }

        var popup = new PickerPopup(Items, _selected, Width);
        popup.Picked += index => SelectedIndex = index;
        var screen = PointToScreen(new Point(0, Height + 4));
        var area = Screen.FromControl(this).WorkingArea;
        if (screen.Y + popup.Height > area.Bottom) screen.Y = PointToScreen(Point.Empty).Y - popup.Height - 4;
        popup.Location = screen;
        popup.Show(FindForm());
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Theme.FillRounded(g, Hover && Enabled ? Theme.Blend(TextField.FieldColor, Color.White, 0.06f) : TextField.FieldColor, r, 8);
        using (var path = Theme.Rounded(r, 8))
        using (var pen = new Pen(Theme.WithAlpha(Color.White, 22)))
            g.DrawPath(pen, path);

        if (_box == null)
            Theme.DrawText(g, SelectedItem?.ToString() ?? "", Font, new Rectangle(10, 0, Width - 40, Height), Enabled ? Theme.Text : Theme.Tertiary);

        // Up/down chevrons like a macOS pop-up button.
        var cx = Width - 18f;
        var cy = Height / 2f;
        Theme.FillRounded(g, Enabled ? Theme.Accent : Theme.Fill, new RectangleF(Width - 28, 6, 20, Height - 12), 5);
        using var pen2 = new Pen(Color.White, 1.5f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        g.DrawLines(pen2, [new PointF(cx - 3.5f, cy - 2), new PointF(cx, cy - 5.5f), new PointF(cx + 3.5f, cy - 2)]);
        g.DrawLines(pen2, [new PointF(cx - 3.5f, cy + 2), new PointF(cx, cy + 5.5f), new PointF(cx + 3.5f, cy + 2)]);
    }
}

/// <summary>The menu that drops down from a <see cref="Picker"/>.</summary>
internal class PickerPopup : Form
{
    private const int RowHeight = 30;
    private readonly List<object> _items;
    private readonly int _selected;
    private int _hover = -1;
    private int _scroll;
    private readonly int _visibleRows;

    public event Action<int>? Picked;

    public PickerPopup(List<object> items, int selected, int width)
    {
        _items = items;
        _selected = selected;
        _visibleRows = Math.Min(items.Count, 10);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Card;
        Size = new Size(Math.Max(width, 160), _visibleRows * RowHeight + 10);
        DoubleBuffered = true;
        KeyPreview = true;
        if (selected >= _visibleRows) _scroll = Math.Min(selected - _visibleRows / 2, items.Count - _visibleRows);
        Deactivate += (_, _) => Close();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Theme.StyleWindow(this);
        BackColor = Theme.Card;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var row = (e.Y - 5) / RowHeight + _scroll;
        if (row != _hover) { _hover = row; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * 2, 0, Math.Max(0, _items.Count - _visibleRows));
        Invalidate();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var row = (e.Y - 5) / RowHeight + _scroll;
        if (row < 0 || row >= _items.Count) return;
        Picked?.Invoke(row);
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        using (var border = new Pen(Theme.Separator)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        for (var i = 0; i < _visibleRows; i++)
        {
            var index = i + _scroll;
            if (index >= _items.Count) break;
            var r = new Rectangle(5, 5 + i * RowHeight, Width - 10, RowHeight);
            if (index == _hover) Theme.FillRounded(g, Theme.Accent, r, 6);
            if (index == _selected)
            {
                using var pen = new Pen(index == _hover ? Color.White : Theme.Accent, 1.8f);
                g.DrawLines(pen, [new PointF(r.Left + 9, r.Top + 15), new PointF(r.Left + 13, r.Top + 19), new PointF(r.Left + 20, r.Top + 10)]);
            }
            Theme.DrawText(g, _items[index].ToString() ?? "", Theme.Body, new Rectangle(r.Left + 26, r.Top, r.Width - 30, r.Height),
                index == _hover ? Color.White : Theme.Text);
        }
        if (_items.Count > _visibleRows)
        {
            var track = Height - 14f;
            var thumb = track * _visibleRows / _items.Count;
            var y = 7 + (track - thumb) * _scroll / (_items.Count - _visibleRows);
            Theme.FillRounded(g, Theme.Tertiary, new RectangleF(Width - 7, y, 3, thumb), 1.5f);
        }
    }
}

// =================================================================================================

/// <summary>
/// macOS-style grouped box: a rounded card holding rows separated by hairlines. Rows are transparent, so the
/// card's rounded corners show through.
/// </summary>
public class Group : Panel
{
    private readonly List<Control> _rows = new();

    public Group(int width)
    {
        Width = width;
        Margin = new Padding(0, 0, 0, 4);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Background;
    }

    /// <summary>A label on the left (with an optional grey line under it) and a control on the right.</summary>
    public SettingRow AddRow(string title, Control? accessory = null, string? subtitle = null)
    {
        var row = new SettingRow(title, accessory, subtitle);
        Add(row);
        return row;
    }

    /// <summary>Any control spanning the full width, with padding.</summary>
    public Control AddContent(Control content, Padding? padding = null)
    {
        var holder = new ContentRow(content, padding ?? new Padding(14, 12, 14, 12));
        Add(holder);
        return holder;
    }

    /// <summary>A full-width row control that lays itself out (e.g. a checklist row).</summary>
    public void AddRaw(Control row) => Add(row);

    private void Add(Control row)
    {
        _rows.Add(row);
        Controls.Add(row);
        row.VisibleChanged += (_, _) => Relayout();
        row.SizeChanged += (_, _) => Relayout();
        Relayout();
    }

    private bool _laying;

    public void Relayout()
    {
        if (_laying) return;
        _laying = true;
        var y = 0;
        foreach (var row in _rows)
        {
            if (!row.Visible && row.Parent == this && Created) continue;
            if (!row.Visible) continue;
            row.SetBounds(0, y, Width, row.Height);
            y += row.Height;
        }
        Height = Math.Max(y, 8);
        _laying = false;
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        foreach (var row in _rows) row.Width = Width;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Background)) e.Graphics.FillRectangle(bg, ClientRectangle);
        Theme.Smooth(e.Graphics);
        Theme.FillRounded(e.Graphics, Theme.Card, new RectangleF(0, 0, Width - 0.5f, Height - 0.5f), 12);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        using var pen = new Pen(Theme.Separator);
        Control? previous = null;
        foreach (var row in _rows.Where(r => r.Visible))
        {
            if (previous != null) e.Graphics.DrawLine(pen, 14, row.Top, Width - 14, row.Top);
            previous = row;
        }
    }

    private sealed class ContentRow : Panel
    {
        public ContentRow(Control content, Padding padding)
        {
            BackColor = Color.Transparent;
            Padding = padding;
            Controls.Add(content);
            content.Location = new Point(padding.Left, padding.Top);
            Height = content.Height + padding.Vertical;
            content.SizeChanged += (_, _) => Height = content.Height + padding.Vertical;
        }
    }
}

/// <summary>One line in a <see cref="Group"/>.</summary>
public class SettingRow : Panel
{
    private readonly Label _title;
    private readonly Label? _subtitle;

    public Control? Accessory { get; }

    public SettingRow(string title, Control? accessory, string? subtitle)
    {
        BackColor = Color.Transparent;
        _title = new Label { Text = title, AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent };
        Controls.Add(_title);
        if (subtitle != null)
        {
            _subtitle = new Label { Text = subtitle, AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent };
            Controls.Add(_subtitle);
        }
        Accessory = accessory;
        if (accessory != null)
        {
            accessory.Margin = Padding.Empty;
            Controls.Add(accessory);
            accessory.SizeChanged += (_, _) => PerformLayout();
        }
        Height = subtitle == null ? 46 : 58;
    }

    public string Title { get => _title.Text; set => _title.Text = value; }

    public string Subtitle
    {
        get => _subtitle?.Text ?? "";
        set { if (_subtitle != null) _subtitle.Text = value; }
    }

    public Color SubtitleColor { set { if (_subtitle != null) _subtitle.ForeColor = value; } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_title == null) return; // called by the base constructor before the labels exist
        if (_subtitle == null) _title.Location = new Point(14, (Height - _title.Height) / 2);
        else
        {
            _title.Location = new Point(14, Height / 2 - _title.Height + 1);
            _subtitle.Location = new Point(14, Height / 2 + 1);
        }
        if (Accessory != null)
        {
            Accessory.Location = new Point(Width - Accessory.Width - 14, (Height - Accessory.Height) / 2);
            var room = Accessory.Left - 24;
            _title.MaximumSize = new Size(Math.Max(room, 40), 0);
            if (_subtitle != null) _subtitle.MaximumSize = new Size(Math.Max(room, 40), 0);
        }
    }
}

/// <summary>Horizontal group of controls with spacing (for accessories with several buttons).</summary>
public class HStack : FlowLayoutPanel
{
    public HStack(params Control[] controls)
    {
        AutoSize = true;
        WrapContents = false;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Controls.AddRange(controls);
        Size = PreferredSize; // so rows can position it before it is first laid out
    }
}

/// <summary>Vertical stack.</summary>
public class VStack : FlowLayoutPanel
{
    public VStack(params Control[] controls)
    {
        AutoSize = true;
        WrapContents = false;
        FlowDirection = FlowDirection.TopDown;
        BackColor = Color.Transparent;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Controls.AddRange(controls);
        Size = PreferredSize;
    }
}
