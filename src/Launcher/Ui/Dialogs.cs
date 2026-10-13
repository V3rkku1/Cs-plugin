namespace LanSkinTester.Launcher.Ui;

/// <summary>Saved loadouts: load / delete them, save the current one, randomize or reset everything.</summary>
public class LoadoutsDialog : Form
{
    public enum Choice { None, Load, SaveCurrent, Randomize, Reset }

    private readonly RowList _list = new() { Width = 276, Height = 214, BackColor = Theme.Card, RowHeight = 34 };
    private readonly PillButton _load = new("Load", PillButton.Style.Primary);
    private readonly PillButton _delete = new("Delete", PillButton.Style.Destructive);

    public Choice Result { get; private set; }
    public string? SelectedName => _list.SelectedItem as string;

    public LoadoutsDialog(IEnumerable<string> names, Action<string> delete, bool canRandomize)
    {
        Text = "Loadouts";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Theme.StyleWindow(this);
        ClientSize = new Size(700, 390);
        Padding = new Padding(24, 20, 24, 20);

        _list.SetItems(names);
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;
        _list.DrawRow = (g, item, r, selected, hover) =>
        {
            Theme.Smooth(g);
            var pill = new RectangleF(r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
            if (selected) Theme.FillRounded(g, Theme.Accent, pill, 7);
            else if (hover) Theme.FillRounded(g, Theme.Fill, pill, 7);
            Theme.DrawText(g, (string)item, Theme.Body, new Rectangle(r.X + 12, r.Y, r.Width - 16, r.Height), selected ? Color.White : Theme.Text);
        };

        var save = Row("Save Current", "Keep it under a name", new PillButton("Save…"), Choice.SaveCurrent);
        var random = Row("Randomize", "Random skins, knife and gloves", new PillButton("Shuffle"), Choice.Randomize);
        var reset = Row("Reset All", "Back to default weapons", new PillButton("Reset", PillButton.Style.Destructive), Choice.Reset);
        random.Accessory!.Enabled = canRandomize;

        _load.Click += (_, _) => Finish(Choice.Load);
        _list.ItemActivated += (_, _) => Finish(Choice.Load);
        _delete.Click += (_, _) =>
        {
            if (SelectedName is not { } name) return;
            if (MessageBox.Show(this, $"Delete the saved loadout \"{name}\"?", "Delete", MessageBoxButtons.OKCancel) != DialogResult.OK) return;
            delete(name);
            _list.Remove(name);
            UpdateButtons();
        };
        _list.SelectedIndexChanged += (_, _) => UpdateButtons();

        var savedGroup = new Group(300);
        savedGroup.AddContent(_list, new Padding(12, 10, 12, 10));
        var savedColumn = new VStack(
            Theme.Caption(_list.Items.Count == 0 ? "NO SAVED LOADOUTS YET" : "SAVED LOADOUTS"), savedGroup,
            new HStack(_load, _delete) { Margin = new Padding(0, 12, 0, 0) }) { Margin = new Padding(0, 0, 20, 0) };

        var actions = new Group(320);
        actions.AddRaw(save);
        actions.AddRaw(random);
        actions.AddRaw(reset);
        var close = new PillButton("Done", PillButton.Style.Primary) { DialogResult = DialogResult.Cancel, Margin = new Padding(0, 14, 0, 0) };
        var actionColumn = new VStack(Theme.Caption("EVERYTHING"), actions, close);

        Controls.Add(new HStack(savedColumn, actionColumn) { Location = new Point(24, 8) });
        Theme.Apply(this);
        CancelButton = close;
        UpdateButtons();
    }

    private SettingRow Row(string title, string subtitle, PillButton button, Choice choice)
    {
        button.Click += (_, _) => Finish(choice);
        return new SettingRow(title, button, subtitle) { Height = 62 };
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
    private readonly TextField _text = new(340);

    public string Value => _text.Text.Trim();

    public Prompt(string title, string label, string initial = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Theme.StyleWindow(this);
        ClientSize = new Size(390, 160);

        var ok = new PillButton("Save", PillButton.Style.Primary);
        var cancel = new PillButton("Cancel") { DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => { if (Value != "") DialogResult = DialogResult.OK; };
        _text.Text = initial;

        var buttons = new HStack(cancel, ok) { Margin = new Padding(0, 18, 0, 0) };
        var layout = new VStack(Theme.Label(label, Theme.Bold), _text, buttons) { Location = new Point(24, 20) };
        Controls.Add(layout);
        Theme.Apply(this);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { _text.Box.Focus(); _text.Box.SelectAll(); };
    }
}
