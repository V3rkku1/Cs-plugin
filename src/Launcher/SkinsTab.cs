using System.Globalization;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher;

/// <summary>
/// Pick skins with the mouse. Writes the plugin's loadouts.json ("local" loadout); the plugin notices
/// the change within a second and re-skins your weapons in game.
/// </summary>
public class SkinsTab : UserControl
{
    private enum Slot { Weapon, Knife, Gloves }

    private record ItemRow(Slot Slot, ushort DefIndex, string Name)
    {
        public string Summary { get; set; } = "";
        public override string ToString() => Summary == "" ? Name : $"{Name}  -  {Summary}";
    }

    private record TypeOption(string Name, ushort DefIndex)
    {
        public override string ToString() => Name;
    }

    private GameFolder? _game;
    private SkinCatalog _catalog = new("");
    private bool _loading;

    private readonly ListBox _items = new() { Dock = DockStyle.Left, Width = 290, IntegralHeight = false };
    private readonly Label _typeLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly ComboBox _typeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly TextBox _search = new() { Width = 300, PlaceholderText = "Search skins (e.g. fade, asiimov)..." };
    private readonly ListBox _skinList = new() { Width = 360, Height = 230, IntegralHeight = false };
    private readonly PictureBox _preview = new() { Width = 256, Height = 192, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle };
    private readonly NumericUpDown _paint = new() { Maximum = 100000, Width = 90 };
    private readonly TrackBar _wearBar = new() { Minimum = 0, Maximum = 1000, TickFrequency = 50, Width = 300 };
    private readonly NumericUpDown _wear = new() { DecimalPlaces = 4, Increment = 0.01m, Minimum = 0, Maximum = 1, Width = 80 };
    private readonly Label _wearName = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly NumericUpDown _seed = new() { Maximum = 1000, Width = 80 };
    private readonly CheckBox _statTrak = new() { Text = "StatTrak", AutoSize = true };
    private readonly NumericUpDown _kills = new() { Maximum = 999999, Width = 90 };
    private readonly TextBox _nametag = new() { MaxLength = 20, Width = 200 };
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Button _save = new() { Text = "Save  (changes in game right away)", AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
    private readonly Button _remove = new() { Text = "Remove skin", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(560, 0) };
    private readonly FlowLayoutPanel _typeRow;
    private readonly FlowLayoutPanel _editor;

    public SkinsTab()
    {
        _model.Items.AddRange(["Auto", "Old (legacy) model", "New model"]);
        _model.SelectedIndex = 0;

        _typeRow = Row(_typeLabel, _typeBox);
        _editor = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 6, 6, 6) };
        _editor.Controls.AddRange([
            _typeRow,
            Row(_search),
            Row(_skinList, _preview),
            Row(Caption("Paint ID:"), _paint),
            Row(Caption("Wear:"), _wearBar, _wear, _wearName),
            Row(Caption("Pattern seed:"), _seed),
            Row(_statTrak, Caption("kills:"), _kills),
            Row(Caption("Name tag:"), _nametag),
            Row(Caption("Weapon model:"), _model, Caption("(change if the skin looks stretched)")),
            Row(_save, _remove),
            _status,
        ]);

        Controls.Add(_editor);
        Controls.Add(_items);

        _items.SelectedIndexChanged += (_, _) => ShowSelectedItem();
        _typeBox.SelectedIndexChanged += (_, _) => { if (!_loading) ShowSkinFor(CurrentDefIndex()); };
        _search.TextChanged += (_, _) => FillSkinList(CurrentDefIndex());
        _skinList.SelectedIndexChanged += (_, _) => OnSkinPicked();
        _wearBar.Scroll += (_, _) => { if (!_loading) _wear.Value = _wearBar.Value / 1000m; };
        _wear.ValueChanged += (_, _) => OnWearChanged();
        _save.Click += (_, _) => Save();
        _remove.Click += (_, _) => Remove();

        Populate();
    }

    public void SetGame(GameFolder? game)
    {
        _game = game;
        Reload();
    }

    /// <summary>Re-reads skin names and the saved loadout (e.g. after chat commands changed it).</summary>
    public void Reload()
    {
        _catalog = new SkinCatalog(_game?.SkinNamesDir ?? "");
        UpdateSummaries();
        ShowSelectedItem();
    }

    private void Populate()
    {
        _items.Items.Add(new ItemRow(Slot.Knife, 0, "Knife"));
        _items.Items.Add(new ItemRow(Slot.Gloves, 0, "Gloves"));
        foreach (var (def, name) in ItemData.DisplayNames.Where(d => d.Key != 31).OrderBy(d => d.Value))
            _items.Items.Add(new ItemRow(Slot.Weapon, def, name));
        _items.SelectedIndex = 0;
    }

    private ItemRow? Selected => _items.SelectedItem as ItemRow;

    private ushort CurrentDefIndex() => Selected switch
    {
        { Slot: Slot.Weapon } w => w.DefIndex,
        _ => (_typeBox.SelectedItem as TypeOption)?.DefIndex ?? 0,
    };

    private PlayerLoadout ReadLoadout() =>
        _game != null && Directory.Exists(_game.PluginDir)
            ? new LoadoutStore(_game.LoadoutFile).Get(LoadoutStore.LocalKey)
            : new PlayerLoadout();

    private void ShowSelectedItem()
    {
        var row = Selected;
        if (row == null) return;

        var loadout = ReadLoadout();
        _loading = true;
        // The row stays visible for weapons (showing the name) so the layout doesn't jump.
        _typeBox.Visible = row.Slot != Slot.Weapon;
        _typeLabel.Font = row.Slot == Slot.Weapon ? _save.Font : Font;
        _typeLabel.Text = row.Name;
        _typeBox.Items.Clear();
        if (row.Slot == Slot.Knife)
        {
            _typeLabel.Text = "Knife:";
            _typeBox.Items.Add(new TypeOption("Default knife", 0));
            foreach (var (name, def) in ItemData.Knives) _typeBox.Items.Add(new TypeOption(Pretty(name), def));
            _typeBox.SelectedItem = _typeBox.Items.Cast<TypeOption>().FirstOrDefault(t => t.DefIndex == loadout.Knife) ?? _typeBox.Items[0];
        }
        else if (row.Slot == Slot.Gloves)
        {
            _typeLabel.Text = "Gloves:";
            _typeBox.Items.Add(new TypeOption("Default gloves", 0));
            foreach (var (name, def) in ItemData.Gloves) _typeBox.Items.Add(new TypeOption(Pretty(name), def));
            _typeBox.SelectedItem = _typeBox.Items.Cast<TypeOption>().FirstOrDefault(t => t.DefIndex == loadout.Gloves) ?? _typeBox.Items[0];
        }
        _loading = false;

        ShowSkinFor(CurrentDefIndex());
    }

    private void ShowSkinFor(ushort defIndex)
    {
        var loadout = ReadLoadout();
        var skin = loadout.Skins.GetValueOrDefault(defIndex) ?? new SkinSettings();

        _loading = true;
        _search.Text = "";
        _paint.Value = skin.Paint;
        _wear.Value = (decimal)Math.Clamp(skin.Wear, 0f, 1f);
        _wearBar.Value = (int)Math.Round(_wear.Value * 1000);
        _wearName.Text = ItemData.WearName(skin.Wear);
        _seed.Value = Math.Clamp(skin.Seed, 0, 1000);
        _statTrak.Checked = skin.StatTrak;
        _kills.Value = Math.Clamp(skin.StatTrakCount, 0, 999999);
        _nametag.Text = skin.Nametag;
        _model.SelectedIndex = skin.Legacy switch { null => 0, true => 1, false => 2 };
        _loading = false;

        FillSkinList(defIndex);

        var editable = defIndex != 0 && _game?.PluginInstalled == true;
        foreach (Control c in new Control[] { _search, _skinList, _paint, _wearBar, _wear, _seed, _statTrak, _kills, _nametag, _model, _remove })
            c.Enabled = editable;
        _save.Enabled = _game?.PluginInstalled == true;

        _status.ForeColor = SystemColors.ControlText;
        _status.Text = _game?.PluginInstalled != true
            ? "Install the plugin first (Setup & Play tab)."
            : defIndex == 0
                ? "Pick a type above, then a skin. Save with \"Default\" to go back to the normal one."
                : _catalog.Count == 0 ? "No skin names installed - type a paint ID, or install skin names in Setup." : "";
    }

    private void FillSkinList(ushort defIndex)
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var entries = _catalog.ForItem(defIndex)
            .Where(e => words.All(w => e.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(SkinCatalog.DisplayName)
            .ToList();

        _loading = true;
        _skinList.BeginUpdate();
        _skinList.Items.Clear();
        foreach (var e in entries) _skinList.Items.Add(new SkinItem(e));
        _skinList.SelectedItem = _skinList.Items.Cast<SkinItem>().FirstOrDefault(s => s.Entry.Paint == (int)_paint.Value);
        _skinList.EndUpdate();
        _loading = false;

        ShowPreview((_skinList.SelectedItem as SkinItem)?.Entry);
    }

    private void OnSkinPicked()
    {
        if (_loading || _skinList.SelectedItem is not SkinItem item) return;
        _paint.Value = item.Entry.Paint;
        ShowPreview(item.Entry);
    }

    private void ShowPreview(CatalogEntry? entry)
    {
        _preview.Image = null;
        if (entry == null || string.IsNullOrEmpty(entry.Image)) return;
        try { _preview.LoadAsync(entry.Image); } catch (Exception) { /* no internet - no picture */ }
    }

    private void OnWearChanged()
    {
        _wearName.Text = ItemData.WearName((float)_wear.Value);
        if (_loading) return;
        _loading = true;
        _wearBar.Value = (int)Math.Round(_wear.Value * 1000);
        _loading = false;
    }

    private void Save()
    {
        if (_game == null || Selected is not { } row) return;

        var store = new LoadoutStore(_game.LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        var defIndex = CurrentDefIndex();

        if (row.Slot == Slot.Knife) loadout.Knife = defIndex;
        if (row.Slot == Slot.Gloves) loadout.Gloves = defIndex;

        if (defIndex != 0 && _paint.Value > 0)
        {
            var skin = loadout.GetOrCreate(defIndex);
            skin.Paint = (int)_paint.Value;
            skin.Wear = Math.Max((float)_wear.Value, 0.00001f);
            skin.Seed = (int)_seed.Value;
            skin.StatTrak = _statTrak.Checked;
            skin.StatTrakCount = (int)_kills.Value;
            skin.Nametag = _nametag.Text.Trim();
            skin.Legacy = _model.SelectedIndex switch { 1 => true, 2 => false, _ => null };
        }
        else if (row.Slot == Slot.Gloves && defIndex != 0)
        {
            Status("Pick a glove skin before saving - gloves need one.", error: true);
            return;
        }

        try
        {
            store.Save();
        }
        catch (IOException ex)
        {
            Status("Could not save: " + ex.Message, error: true);
            return;
        }

        UpdateSummaries();
        Status(TestSession.Cs2Running()
            ? "Saved. Your weapons refresh in game in about a second (gloves may need a respawn)."
            : "Saved. You'll have it next time you start a skin test.");
    }

    private void Remove()
    {
        if (_game == null || Selected is not { } row) return;

        var store = new LoadoutStore(_game.LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        loadout.Skins.Remove(CurrentDefIndex());
        if (row.Slot == Slot.Gloves) loadout.Gloves = 0;
        store.Save();

        UpdateSummaries();
        ShowSelectedItem();
        Status("Skin removed.");
    }

    private void UpdateSummaries()
    {
        var loadout = ReadLoadout();
        for (var i = 0; i < _items.Items.Count; i++)
        {
            var row = (ItemRow)_items.Items[i];
            var def = row.Slot switch { Slot.Knife => loadout.Knife, Slot.Gloves => loadout.Gloves, _ => row.DefIndex };
            var paint = def != 0 && loadout.Skins.TryGetValue(def, out var s) ? s.Paint : 0;
            var entry = paint == 0 ? null : _catalog.Find(def, paint);
            var finish = paint == 0 ? "" : entry != null ? SkinCatalog.DisplayName(entry) : $"#{paint}";
            var type = row.Slot != Slot.Weapon && def != 0 ? Pretty(ItemData.NameOf(def).Replace(" gloves", "")) : "";

            row.Summary = string.Join(" ", new[] { type, finish }.Where(x => x != ""));
            _items.Items[i] = row; // forces the ListBox to redraw the text
        }
    }

    private void Status(string text, bool error = false)
    {
        _status.ForeColor = error ? Color.Firebrick : Color.ForestGreen;
        _status.Text = text;
    }

    private static string Pretty(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.AddRange(controls);
        return row;
    }

    private sealed record SkinItem(CatalogEntry Entry)
    {
        public override string ToString() => $"{SkinCatalog.DisplayName(Entry)}   (#{Entry.Paint})";
    }
}
