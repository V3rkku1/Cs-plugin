namespace LanSkinTester.Launcher.Ui;

public enum StepState { Missing, Ok, Warning, Working, Failed }

/// <summary>One line of the setup checklist: status dot, name, detail (version/info), progress bar and an action button.</summary>
public class StepRow : Panel
{
    private readonly Label _title = new() { AutoSize = true, Font = Theme.Bold, ForeColor = Theme.Text, Location = new Point(36, 10) };
    private readonly Label _detail = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Location = new Point(36, 32) };
    private StepState _state;
    private double _progress = -2; // -2 hidden, -1 indeterminate, 0..1

    public Button Action { get; }

    public StepRow(string title, string actionText)
    {
        Height = 60;
        Width = 640;
        BackColor = Theme.Card;
        Margin = new Padding(0, 0, 0, 6);
        DoubleBuffered = true;
        _title.Text = title;
        Action = Theme.MakeButton(actionText);
        Action.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        Action.AutoSize = false;
        Action.Size = new Size(130, 32);
        Action.Location = new Point(Width - Action.Width - 12, 14);
        Controls.AddRange([_title, _detail, Action]);
    }

    public void Set(StepState state, string detail)
    {
        _state = state;
        _detail.Text = detail;
        _detail.ForeColor = state switch { StepState.Failed => Theme.Bad, StepState.Warning => Theme.Accent, _ => Theme.Muted };
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
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var color = _state switch
        {
            StepState.Ok => Theme.Good,
            StepState.Warning or StepState.Working => Theme.Accent,
            StepState.Failed => Theme.Bad,
            _ => Theme.Border,
        };
        using (var brush = new SolidBrush(color)) g.FillEllipse(brush, 12, 22, 14, 14);
        if (_state == StepState.Missing)
            using (var bg = new SolidBrush(Theme.Card)) g.FillEllipse(bg, 15, 25, 8, 8);

        if (_progress > -2)
        {
            var bar = new Rectangle(36, Height - 7, Width - 36 - 160, 3);
            using var back = new SolidBrush(Theme.Border);
            g.FillRectangle(back, bar);
            using var fill = new SolidBrush(Theme.Accent);
            if (_progress >= 0) g.FillRectangle(fill, bar.Left, bar.Top, (int)(bar.Width * Math.Clamp(_progress, 0, 1)), bar.Height);
            else
            {
                // Unknown size: a moving block.
                var x = (int)(DateTime.Now.TimeOfDay.TotalMilliseconds / 4 % bar.Width);
                g.FillRectangle(fill, bar.Left + x, bar.Top, Math.Min(60, bar.Width - x), bar.Height);
            }
        }
    }
}
