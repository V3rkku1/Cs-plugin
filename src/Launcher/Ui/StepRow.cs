namespace LanSkinTester.Launcher.Ui;

public enum StepState { Missing, Ok, Warning, Working, Failed }

/// <summary>A row of the setup checklist (lives in a <see cref="Group"/>): status icon, name, detail, progress, button.</summary>
public class StepRow : Panel
{
    private readonly Label _title = new() { AutoSize = true, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Color.Transparent };
    private readonly Label _detail = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Color.Transparent };
    private StepState _state;
    private double _progress = -2; // -2 hidden, -1 indeterminate, 0..1

    public PillButton Action { get; }

    public StepRow(string title, string actionText)
    {
        Height = 62;
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        _title.Text = title;
        Action = new PillButton(actionText) { AutoWidth = false };
        Action.Width = 100;
        Action.Height = 28;
        Controls.AddRange([_title, _detail, Action]);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Action == null) return;
        _title.Location = new Point(52, 12);
        _detail.Location = new Point(52, 33);
        _detail.MaximumSize = new Size(Math.Max(Width - 52 - 130, 60), 0);
        Action.Location = new Point(Width - Action.Width - 14, (Height - Action.Height) / 2);
    }

    public void Set(StepState state, string detail)
    {
        _state = state;
        _detail.Text = detail;
        _detail.ForeColor = state switch { StepState.Failed => Theme.Bad, StepState.Warning => Theme.Warning, _ => Theme.Muted };
        if (state != StepState.Working) _progress = -2;
        Invalidate();
    }

    /// <summary>0..1, or -1 when the size is unknown.</summary>
    public void SetProgress(double progress)
    {
        _state = StepState.Working;
        _progress = progress;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Theme.Smooth(g);

        var icon = new RectangleF(16, Height / 2f - 11, 22, 22);
        switch (_state)
        {
            case StepState.Ok:
                using (var b = new SolidBrush(Theme.Good)) g.FillEllipse(b, icon);
                using (var pen = new Pen(Color.White, 2f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLines(pen, [new PointF(icon.Left + 6, icon.Top + 11.5f), new PointF(icon.Left + 9.5f, icon.Top + 15), new PointF(icon.Left + 16, icon.Top + 7.5f)]);
                break;
            case StepState.Warning or StepState.Failed:
                using (var b = new SolidBrush(_state == StepState.Failed ? Theme.Bad : Theme.Warning)) g.FillEllipse(b, icon);
                Theme.DrawText(g, "!", Theme.Bold, Rectangle.Round(icon), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                break;
            case StepState.Working:
                using (var track = new Pen(Theme.Fill, 2.5f)) g.DrawEllipse(track, icon);
                var sweep = (float)(DateTime.Now.TimeOfDay.TotalMilliseconds / 3 % 360);
                using (var arc = new Pen(Theme.Accent, 2.5f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawArc(arc, icon, sweep, 100);
                break;
            default:
                using (var ring = new Pen(Theme.Tertiary, 1.8f)) g.DrawEllipse(ring, icon);
                break;
        }

        if (_progress > -2)
        {
            var bar = new RectangleF(52, Height - 9, Width - 52 - 130, 4);
            Theme.FillRounded(g, Theme.Fill, bar, 2);
            if (_progress >= 0) Theme.FillRounded(g, Theme.Accent, new RectangleF(bar.Left, bar.Top, bar.Width * (float)Math.Clamp(_progress, 0, 1), bar.Height), 2);
            else
            {
                var x = (float)(DateTime.Now.TimeOfDay.TotalMilliseconds / 4 % bar.Width);
                Theme.FillRounded(g, Theme.Accent, new RectangleF(bar.Left + x, bar.Top, Math.Min(60, bar.Width - x), bar.Height), 2);
            }
        }
    }
}
