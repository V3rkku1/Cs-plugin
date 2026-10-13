namespace LanSkinTester.Launcher.Ui;

/// <summary>A row of toggle buttons where exactly one is selected (like a tab strip).</summary>
public class Segmented : FlowLayoutPanel
{
    private readonly List<Button> _buttons = new();
    private int _selected = -1;

    public event EventHandler? SelectedChanged;

    public Segmented(IEnumerable<string> options, int buttonWidth = 0)
    {
        AutoSize = true;
        WrapContents = true;
        Margin = new Padding(0, 2, 0, 2);
        BackColor = Color.Transparent;

        foreach (var option in options)
        {
            var index = _buttons.Count;
            var b = new Button
            {
                Text = option,
                FlatStyle = FlatStyle.Flat,
                Height = 30,
                AutoSize = buttonWidth == 0,
                Width = buttonWidth > 0 ? buttonWidth : 75,
                Margin = new Padding(0, 0, 4, 4),
                Font = Theme.Bold,
                Cursor = Cursors.Hand,
                Padding = new Padding(6, 0, 6, 0),
                UseVisualStyleBackColor = false,
                Tag = Theme.ButtonKind.Normal, // tells Theme.Apply to leave the colours to this control
            };
            b.Click += (_, _) => SelectedIndex = index;
            _buttons.Add(b);
            Controls.Add(b);
        }
        SelectedIndex = 0;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            if (value < 0 || value >= _buttons.Count) return;
            var changed = value != _selected;
            _selected = value;
            Restyle();
            if (changed) SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedText => _buttons[_selected].Text;

    public void SetEnabled(int index, bool enabled)
    {
        _buttons[index].Enabled = enabled;
        Restyle();
    }

    private void Restyle()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            var b = _buttons[i];
            var on = i == _selected;
            b.BackColor = on ? Theme.Accent : b.Enabled ? Theme.Card : Theme.Surface;
            b.ForeColor = on ? Color.FromArgb(20, 16, 8) : b.Enabled ? Theme.Text : Color.FromArgb(80, 86, 100);
            b.FlatAppearance.BorderColor = on ? Theme.Accent : Theme.Border;
            b.FlatAppearance.MouseOverBackColor = on ? Theme.Accent : Theme.CardHover;
        }
    }
}
