namespace LanSkinTester.Launcher.Ui;

/// <summary>Saved loadouts: load / delete them, save the current one, randomize or reset everything.</summary>
public class LoadoutsDialog : Form
{
    public enum Choice { None, Load, SaveCurrent, Randomize, Reset }

    private readonly ListBox _list = new()
    {
        Width = 300, Height = 220, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text,
        IntegralHeight = false, Font = Theme.Body,
    };
    private readonly Button _load = Theme.MakeButton("Load", Theme.ButtonKind.Primary, 120);
    private readonly Button _delete = Theme.MakeButton("Delete", Theme.ButtonKind.Danger, 90);

    public Choice Result { get; private set; }
    public string? SelectedName => _list.SelectedItem as string;

    public LoadoutsDialog(IEnumerable<string> names, Action<string> delete, bool canRandomize)
    {
        Text = "Loadouts";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(560, 340);

        foreach (var n in names) _list.Items.Add(n);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        var save = Theme.MakeButton("Save current as...", Theme.ButtonKind.Normal, 200);
        var random = Theme.MakeButton("Randomize everything", Theme.ButtonKind.Normal, 200);
        var reset = Theme.MakeButton("Reset everything", Theme.ButtonKind.Danger, 200);
        var close = Theme.MakeButton("Close", Theme.ButtonKind.Ghost, 200);
        random.Enabled = canRandomize;

        _load.Click += (_, _) => Finish(Choice.Load);
        _list.DoubleClick += (_, _) => Finish(Choice.Load);
        _delete.Click += (_, _) =>
        {
            if (SelectedName is not { } name) return;
            if (MessageBox.Show(this, $"Delete the saved loadout \"{name}\"?", "Delete", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            delete(name);
            _list.Items.Remove(name);
            UpdateButtons();
        };
        save.Click += (_, _) => Finish(Choice.SaveCurrent);
        random.Click += (_, _) => Finish(Choice.Randomize);
        reset.Click += (_, _) => Finish(Choice.Reset);
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 16, 0) };
        var empty = Theme.Label(_list.Items.Count == 0 ? "No saved loadouts yet." : "Saved loadouts", Theme.Bold, Theme.Muted);
        var row = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        row.Controls.AddRange([_load, _delete]);
        left.Controls.AddRange([empty, _list, row]);

        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 26, 0, 0) };
        foreach (var b in new[] { save, random, reset, close }) b.Margin = new Padding(0, 0, 0, 8);
        right.Controls.AddRange([save, random, reset, close]);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 12, 18, 12), WrapContents = false };
        layout.Controls.AddRange([left, right]);
        Controls.Add(layout);
        Theme.Apply(this);
        CancelButton = close;
        UpdateButtons();
    }

    private void UpdateButtons() => _load.Enabled = _delete.Enabled = SelectedName != null;

    private void Finish(Choice choice)
    {
        if (choice == Choice.Load && SelectedName == null) return;
        Result = choice;
        DialogResult = DialogResult.OK;
    }
}

/// <summary>A one-line text prompt.</summary>
public class Prompt : Form
{
    private readonly TextBox _text = new() { Width = 320 };

    public string Value => _text.Text.Trim();

    public Prompt(string title, string label, string initial = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        ClientSize = new Size(360, 130);

        var ok = Theme.MakeButton("Save", Theme.ButtonKind.Primary, 100);
        var cancel = Theme.MakeButton("Cancel");
        ok.Click += (_, _) => { if (Value != "") DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        _text.Text = initial;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(18, 14, 18, 8) };
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange([ok, cancel]);
        layout.Controls.AddRange([Theme.Label(label), _text, buttons]);
        Controls.Add(layout);
        Theme.Apply(this);
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
