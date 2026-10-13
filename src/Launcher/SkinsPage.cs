using System.Globalization;
using LanSkinTester.Launcher.Core;
using LanSkinTester.Launcher.Ui;

namespace LanSkinTester.Launcher;

/// <summary>
/// Pick skins like in the game's inventory: category + weapon on the left, skin cards in the middle,
/// details (wear, pattern, StatTrak, name tag, stickers, charm) on the right. Equipping writes the plugin's
/// loadouts.json ("local" loadout); the plugin re-skins your weapons within a second.
/// </summary>
public class SkinsPage : UserControl
{
    private static readonly string[] Categories = ["Knives", "Gloves", "Pistols", "Rifles", "SMGs", "Heavy"];

    private static readonly (string Short, string Name, float Min, float Max)[] Conditions =
    [
        ("FN", "Factory New", 0f, 0.07f), ("MW", "Minimal Wear", 0.07f, 0.15f), ("FT", "Field-Tested", 0.15f, 0.38f),
        ("WW", "Well-Worn", 0.38f, 0.45f), ("BS", "Battle-Scarred", 0.45f, 1f),
    ];

    private readonly ImageCache _images;
    private GameFolder? _game;
    private ItemDatabase _db = new();
    private bool _loading;

    // The weapon being edited and a working copy of its settings (saved on Equip).
    private ushort _def;
    private SkinSettings _work = new();

    private readonly Segmented _category = new(Categories, 0);
    private readonly ListBox _weapons = new()
    {
        DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 58, BorderStyle = BorderStyle.None, Dock = DockStyle.Fill,
        BackColor = Theme.Surface, IntegralHeight = false,
    };
    private readonly TextBox _search = new() { PlaceholderText = "Search skins...", Width = 300 };
    private readonly Button _importButton = Theme.MakeButton("Import real item...", Theme.ButtonKind.Normal);
    private readonly Label _gridTitle = new() { AutoSize = true, Font = Theme.Heading, ForeColor = Theme.Text, Margin = new Padding(0, 6, 16, 0) };
    private readonly CardGrid _grid = new() { Dock = DockStyle.Fill };

    // Details
    private readonly Panel _preview = new() { Height = 200, Dock = DockStyle.Top, BackColor = Theme.Card };
    private readonly Label _title = new() { AutoSize = true, Font = Theme.Big, ForeColor = Theme.Text, MaximumSize = new Size(330, 0) };
    private readonly Label _rarity = new() { AutoSize = true, Font = Theme.Bold };
    private readonly Label _range = new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted };
    private readonly TrackBar _wearBar = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, Width = 220, Height = 28 };
    private readonly NumericUpDown _wear = new() { DecimalPlaces = 6, Increment = 0.001m, Minimum = 0, Maximum = 1, Width = 92 };
    private readonly Segmented _condition = new(Conditions.Select(c => c.Short), 58);
    private readonly NumericUpDown _seed = new() { Maximum = 1000, Width = 80 };
    private readonly Button _randomSeed = Theme.MakeButton("Random");
    private readonly CheckBox _statTrak = new() { Text = "StatTrak™", AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
    private readonly NumericUpDown _kills = new() { Maximum = 999999, Width = 90 };
    private readonly TextBox _nametag = new() { MaxLength = 20, Width = 220, PlaceholderText = "Name tag (optional)" };
    private readonly SlotTile[] _stickerSlots = Enumerable.Range(0, 5).Select(i => new SlotTile($"{i + 1}")).ToArray();
    private readonly SlotTile _charmSlot = new("Charm");
    private readonly ComboBox _model = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly Button _equip = Theme.MakeButton("EQUIP", Theme.ButtonKind.Primary, 200);
    private readonly Button _unequip = Theme.MakeButton("Unequip", Theme.ButtonKind.Danger, 100);
    private readonly Button _copyGen = Theme.MakeButton("Copy !gen", Theme.ButtonKind.Ghost);
    private readonly Button _copyLink = Theme.MakeButton("Copy inspect link", Theme.ButtonKind.Ghost);
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(330, 0), Font = Theme.Small, Margin = new Padding(0, 6, 0, 0) };

    public SkinsPage(ImageCache images)
    {
        _images = images;
        BackColor = Theme.Background;
        _grid.SetImageCache(images);
        _model.Items.AddRange(["Model: auto", "Model: old (legacy)", "Model: new"]);
        _model.SelectedIndex = 0;

        Controls.Add(BuildCenter());
        Controls.Add(BuildDetails());
        Controls.Add(BuildLeft());
        Theme.Apply(this);

        _category.SelectedChanged += (_, _) => FillWeapons();
        _weapons.DrawItem += DrawWeaponRow;
        _weapons.SelectedIndexChanged += (_, _) => { if (!_loading) OpenWeapon(); };
        _search.TextChanged += (_, _) => FillGrid();
        _grid.SelectedChanged += (_, _) => OnCardPicked();
        _grid.ItemActivated += (_, _) => Equip();
        _importButton.Click += (_, _) => Import();

        _wearBar.Scroll += (_, _) => { if (!_loading) _wear.Value = _wearBar.Value / 1000m; };
        _wear.ValueChanged += (_, _) => OnWearChanged();
        _condition.SelectedChanged += (_, _) => OnConditionPicked();
        _seed.ValueChanged += (_, _) => { if (!_loading) _work.Seed = (int)_seed.Value; };
        _randomSeed.Click += (_, _) => _seed.Value = Random.Shared.Next(0, 1001);
        _statTrak.CheckedChanged += (_, _) => { if (!_loading) _work.StatTrak = _statTrak.Checked; _kills.Enabled = _statTrak.Checked; };
        _kills.ValueChanged += (_, _) => { if (!_loading) _work.StatTrakCount = (int)_kills.Value; };
        _nametag.TextChanged += (_, _) => { if (!_loading) _work.Nametag = _nametag.Text.Trim(); };
        _model.SelectedIndexChanged += (_, _) => { if (!_loading) _work.Legacy = _model.SelectedIndex switch { 1 => true, 2 => false, _ => null }; };
        for (var i = 0; i < 5; i++)
        {
            var slot = i;
            _stickerSlots[i].Click += (_, _) => PickSticker(slot);
            _stickerSlots[i].Cleared += (_, _) => { _work.Stickers.RemoveAll(s => s.Slot == slot); ShowAttachments(); };
        }
        _charmSlot.Click += (_, _) => PickCharm();
        _charmSlot.Cleared += (_, _) => { _work.Keychain = null; ShowAttachments(); };
        _equip.Click += (_, _) => Equip();
        _unequip.Click += (_, _) => Unequip();
        _copyGen.Click += (_, _) => Copy(ItemImport.ToGen(_def, _work), "!gen code copied.");
        _copyLink.Click += (_, _) => Copy(ItemImport.ToInspectLink(_def, _work), "Inspect link copied.");
        _preview.Paint += DrawPreview;

        FillWeapons();
    }

    public void SetData(GameFolder? game, ItemDatabase db)
    {
        // Start on the equipped item again when the folder or data changes (not on every status refresh).
        if (game?.Root != _game?.Root || db != _db) _def = 0;
        _game = game;
        _db = db;
        Reload();
    }

    /// <summary>Re-reads the saved loadout (e.g. after chat commands changed it).</summary>
    public void Reload()
    {
        var keep = _def;
        FillWeapons();
        if (keep != 0) SelectWeapon(keep);
    }

    // ---------------------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------------------

    private Control BuildLeft()
    {
        var panel = new Panel { Dock = DockStyle.Left, Width = 290, BackColor = Theme.Surface, Padding = new Padding(12) };
        _category.Dock = DockStyle.Top;
        _category.MaximumSize = new Size(270, 0);
        var gap = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Theme.Surface };
        panel.Controls.Add(_weapons);
        panel.Controls.Add(gap);
        panel.Controls.Add(_category);
        return panel;
    }

    private Control BuildCenter()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(14, 12, 14, 12) };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, BackColor = Theme.Background, WrapContents = false };
        _search.Margin = new Padding(0, 6, 10, 0);
        toolbar.Controls.AddRange([_gridTitle, _search, _importButton]);
        panel.Controls.Add(_grid);
        panel.Controls.Add(toolbar);
        return panel;
    }

    private Control BuildDetails()
    {
        var outer = new Panel { Dock = DockStyle.Right, Width = 390, BackColor = Theme.Surface, Padding = new Padding(14) };
        var body = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true,
            BackColor = Theme.Surface, Padding = new Padding(0, 10, 0, 0),
        };

        var stickers = Row(_stickerSlots.Cast<Control>().Append(_charmSlot).ToArray());
        body.Controls.AddRange([
            _title, _rarity, _range,
            Caption("WEAR"), Row(_wearBar, _wear), _condition,
            Caption("PATTERN SEED"), Row(_seed, _randomSeed),
            Row(_statTrak, _kills),
            Caption("NAME TAG"), _nametag,
            Caption("STICKERS  ·  CHARM   (click to add, right-click to remove)"), stickers,
            _model,
            Row(_equip, _unequip),
            Row(_copyGen, _copyLink),
            _status,
        ]);
        _seed.Margin = new Padding(0, 6, 8, 0);
        _wear.Margin = new Padding(4, 2, 0, 0);
        _model.Margin = new Padding(0, 10, 0, 6);

        outer.Controls.Add(body);
        outer.Controls.Add(_preview);
        return outer;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, Margin = new Padding(0, 12, 0, 3) };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Surface, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.AddRange(controls);
        return row;
    }

    // ---------------------------------------------------------------------------------------------
    // Left: weapons
    // ---------------------------------------------------------------------------------------------

    private record WeaponRow(ushort Def, string Name, string Image)
    {
        public override string ToString() => Name;
    }

    private void FillWeapons()
    {
        var category = Categories[_category.SelectedIndex];
        IEnumerable<WeaponRow> rows;
        if (!_db.IsEmpty)
        {
            rows = _db.Weapons
                .Where(w => w.Category == category || (category == "Heavy" && w.Category == "Equipment"))
                .Where(w => category is "Knives" or "Gloves" || ItemData.Weapons.ContainsKey(w.DefIndex))
                .Select(w => new WeaponRow(w.DefIndex, w.Name, w.Image));
        }
        else
        {
            // No item data yet: still allow picking by paint ID.
            rows = category switch
            {
                "Knives" => ItemData.Knives.Select(k => new WeaponRow(k.Value, Pretty(k.Key), "")),
                "Gloves" => ItemData.Gloves.Select(k => new WeaponRow(k.Value, Pretty(k.Key) + " Gloves", "")),
                "Pistols" => ItemData.DisplayNames.Where(d => ItemData.Pistols.Contains(d.Key)).Select(d => new WeaponRow(d.Key, d.Value, "")),
                _ => ItemData.DisplayNames.Where(d => !ItemData.Pistols.Contains(d.Key)).Select(d => new WeaponRow(d.Key, d.Value, "")),
            };
        }

        _loading = true;
        _weapons.BeginUpdate();
        _weapons.Items.Clear();
        foreach (var row in rows.OrderBy(r => r.Name)) _weapons.Items.Add(row);
        _weapons.EndUpdate();
        _loading = false;

        if (_weapons.Items.Count > 0)
        {
            var loadout = ReadLoadout();
            var equipped = category switch { "Knives" => loadout.Knife, "Gloves" => loadout.Gloves, _ => (ushort)0 };
            var index = _weapons.Items.Cast<WeaponRow>().ToList().FindIndex(r => r.Def == equipped);
            _weapons.SelectedIndex = Math.Max(0, index);
        }
    }

    private void SelectWeapon(ushort def)
    {
        var category = CategoryOf(def);
        if (category >= 0 && category != _category.SelectedIndex) _category.SelectedIndex = category;
        var index = _weapons.Items.Cast<WeaponRow>().ToList().FindIndex(r => r.Def == def);
        if (index >= 0) _weapons.SelectedIndex = index;
    }

    private int CategoryOf(ushort def)
    {
        if (ItemData.Knives.ContainsValue(def)) return 0;
        if (ItemData.Gloves.ContainsValue(def)) return 1;
        var info = _db.Weapon(def);
        if (info != null) return Math.Max(0, Array.IndexOf(Categories, info.Category == "Equipment" ? "Heavy" : info.Category));
        return ItemData.Pistols.Contains(def) ? 2 : 3;
    }

    private void DrawWeaponRow(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var row = (WeaponRow)_weapons.Items[e.Index];
        var g = e.Graphics;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var r = e.Bounds;

        using (var bg = new SolidBrush(selected ? Theme.Card : Theme.Surface)) g.FillRectangle(bg, r);
        if (selected)
            using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, r.Left, r.Top + 6, 3, r.Height - 12);

        var loadout = ReadLoadoutCached();
        var skin = loadout.Skins.GetValueOrDefault(row.Def);
        var info = skin is { Paint: not 0 } ? _db.Find(row.Def, skin.Paint) : null;

        var imageBox = new Rectangle(r.Left + 10, r.Top + 6, 74, r.Height - 12);
        var img = _images.Get(info?.Image ?? row.Image, 128, () => { if (IsHandleCreated) BeginInvoke(() => _weapons.Invalidate()); });
        if (img != null) g.DrawImage(img, CardGrid.Fit(img.Size, imageBox));

        TextRenderer.DrawText(g, row.Name, Theme.Bold, new Rectangle(r.Left + 92, r.Top + 9, r.Width - 96, 20), Theme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var equippedType = ItemData.Knives.ContainsValue(row.Def) ? loadout.Knife == row.Def
            : ItemData.Gloves.ContainsValue(row.Def) && loadout.Gloves == row.Def;
        var sub = info != null ? info.DisplayFinish : skin is { Paint: not 0 } ? $"Paint #{skin.Paint}" : equippedType ? "Vanilla" : "Default";
        var color = info != null ? Theme.Parse(info.RarityColor, Theme.Muted) : Theme.Muted;
        if (equippedType) sub = "EQUIPPED · " + sub;
        TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(r.Left + 92, r.Top + 30, r.Width - 96, 18),
            equippedType ? Theme.Accent : color, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // The list redraws often; read the file at most twice a second.
    private PlayerLoadout? _cachedLoadout;
    private DateTime _cachedAt;

    private PlayerLoadout ReadLoadoutCached()
    {
        if (_cachedLoadout == null || (DateTime.UtcNow - _cachedAt).TotalMilliseconds > 500)
        {
            _cachedLoadout = ReadLoadout();
            _cachedAt = DateTime.UtcNow;
        }
        return _cachedLoadout;
    }

    // ---------------------------------------------------------------------------------------------
    // Middle: skin cards
    // ---------------------------------------------------------------------------------------------

    private void OpenWeapon()
    {
        if (_weapons.SelectedItem is not WeaponRow row) return;
        _def = row.Def;
        _gridTitle.Text = row.Name;

        // Start from what is saved for this weapon (or a fresh default).
        var saved = ReadLoadout().Skins.GetValueOrDefault(_def);
        _work = saved != null ? Clone(saved) : new SkinSettings();

        _search.Text = "";
        FillGrid();
        ShowDetails();
    }

    private static readonly Dictionary<string, int> RarityOrder = new()
    {
        ["Contraband"] = 0, ["Extraordinary"] = 1, ["Covert"] = 1, ["Classified"] = 2, ["Restricted"] = 3,
        ["Mil-Spec Grade"] = 4, ["Industrial Grade"] = 5, ["Consumer Grade"] = 6,
    };

    private void FillGrid()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var saved = ReadLoadout().Skins.GetValueOrDefault(_def);
        var isGloves = ItemData.Gloves.ContainsValue(_def);

        var cards = new List<CardItem>();
        if (!isGloves && words.Length == 0)
            cards.Add(new CardItem { Tag = 0, Title = "Vanilla", Subtitle = "No paint", Accent = Theme.Muted, ImageUrl = _db.Weapon(_def)?.Image ?? "" });

        cards.AddRange(_db.SkinsFor(_def)
            .Where(s => words.All(w => s.DisplayFinish.Contains(w, StringComparison.OrdinalIgnoreCase) || s.Rarity.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => RarityOrder.GetValueOrDefault(s.Rarity, 9))
            .ThenBy(s => s.DisplayFinish)
            .Select(s => new CardItem { Tag = s.Paint, Title = s.DisplayFinish, Subtitle = s.Rarity, Accent = Theme.Parse(s.RarityColor, Theme.Muted), ImageUrl = s.Image }));

        foreach (var c in cards)
            if (saved != null && (int)c.Tag! == saved.Paint) c.Badge = "EQUIPPED";

        var empty = _db.IsEmpty
            ? "Skin list not downloaded yet.\nGo to PLAY and press \"Install / update everything\".\n\nYou can still import items with a !gen code or inspect link."
            : "No skins match your search.";
        _grid.SetItems(cards, empty);
        _loading = true;
        _grid.Select(c => (int)c.Tag! == _work.Paint);
        _loading = false;
    }

    private void OnCardPicked()
    {
        if (_loading || _grid.SelectedItem?.Tag is not int paint) return;
        _work.Paint = paint;

        // Keep the wear inside what this skin can actually have.
        var info = _db.Find(_def, paint);
        if (info != null && (_work.Wear < info.MinFloat || _work.Wear > info.MaxFloat))
            _work.Wear = Math.Max(info.MinFloat, 0.00001f) + (info.MaxFloat - info.MinFloat) * 0.02f;
        ShowDetails();
    }

    // ---------------------------------------------------------------------------------------------
    // Right: details
    // ---------------------------------------------------------------------------------------------

    private SkinInfo? CurrentInfo => _work.Paint != 0 ? _db.Find(_def, _work.Paint) : null;

    private void ShowDetails()
    {
        var info = CurrentInfo;
        var weaponName = _db.Weapon(_def)?.Name ?? ItemData.DisplayNames.GetValueOrDefault(_def) ?? Pretty(ItemData.NameOf(_def));

        _loading = true;
        _title.Text = info?.FullName ?? (_work.Paint != 0 ? $"{weaponName} | Paint #{_work.Paint}" : $"{weaponName} | Vanilla");
        _rarity.Text = info?.Rarity ?? "";
        _rarity.ForeColor = Theme.Parse(info?.RarityColor, Theme.Muted);
        _range.Text = info != null ? $"Float range {info.MinFloat:0.00} – {info.MaxFloat:0.00}" : "";

        _wear.Value = (decimal)Math.Clamp(_work.Wear, 0f, 1f);
        _wearBar.Value = (int)Math.Round(_wear.Value * 1000);
        for (var i = 0; i < Conditions.Length; i++)
            _condition.SetEnabled(i, info == null || (Conditions[i].Max > info.MinFloat && Conditions[i].Min < info.MaxFloat));
        _condition.SelectedIndex = ConditionIndex(_work.Wear);
        _seed.Value = Math.Clamp(_work.Seed, 0, 1000);
        _statTrak.Checked = _work.StatTrak;
        _kills.Value = Math.Clamp(_work.StatTrakCount, 0, 999999);
        _kills.Enabled = _work.StatTrak;
        _nametag.Text = _work.Nametag;
        _model.SelectedIndex = _work.Legacy switch { null => 0, true => 1, false => 2 };
        _loading = false;

        var paintable = _work.Paint != 0;
        _wearBar.Enabled = _wear.Enabled = _condition.Enabled = _seed.Enabled = _randomSeed.Enabled = paintable;

        var isType = ItemData.Knives.ContainsValue(_def) || ItemData.Gloves.ContainsValue(_def);
        var loadout = ReadLoadout();
        _unequip.Enabled = loadout.Skins.ContainsKey(_def) || (isType && (loadout.Knife == _def || loadout.Gloves == _def));
        _equip.Enabled = _game?.PluginInstalled == true;
        foreach (var tile in _stickerSlots) tile.Visible = !ItemData.Gloves.ContainsValue(_def);
        _charmSlot.Visible = !isType;

        ShowAttachments();
        _preview.Invalidate();
        SetStatus(_game?.PluginInstalled == true ? "" : "Install the plugin first (PLAY page).", false);
    }

    private void ShowAttachments()
    {
        for (var i = 0; i < 5; i++)
        {
            var st = _work.Stickers.FirstOrDefault(s => s.Slot == i);
            var info = st != null ? _db.Sticker(st.Id) : null;
            _stickerSlots[i].Set(st == null ? null : info?.Image ?? "", st == null ? "" : info?.Name ?? $"#{st.Id}", _images);
        }
        var charm = _work.Keychain != null ? _db.Charm(_work.Keychain.Id) : null;
        _charmSlot.Set(_work.Keychain == null ? null : charm?.Image ?? "", _work.Keychain == null ? "" : charm?.Name ?? $"#{_work.Keychain.Id}", _images);
    }

    private void OnWearChanged()
    {
        if (_loading) return;
        _work.Wear = Math.Max((float)_wear.Value, 0.00001f);
        _loading = true;
        _wearBar.Value = (int)Math.Round(_wear.Value * 1000);
        _condition.SelectedIndex = ConditionIndex(_work.Wear);
        _loading = false;
        _preview.Invalidate();
    }

    private void OnConditionPicked()
    {
        if (_loading) return;
        var c = Conditions[_condition.SelectedIndex];
        var info = CurrentInfo;
        var min = Math.Max(c.Min, info?.MinFloat ?? 0f);
        var max = Math.Min(c.Max, info?.MaxFloat ?? 1f);
        _wear.Value = (decimal)Math.Round(Math.Max(min + (max - min) * 0.25f, 0.00001f), 6);
    }

    private static int ConditionIndex(float wear) => Array.FindIndex(Conditions, c => wear < c.Max) is var i and >= 0 ? i : 4;

    private void DrawPreview(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = _preview.ClientRectangle;
        var info = CurrentInfo;
        var accent = Theme.Parse(info?.RarityColor, Theme.Border);

        using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(r, Theme.Card, CardGrid.Blend(Theme.Card, accent, 0.35f),
                   System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            g.FillRectangle(bg, r);
        using (var stripe = new SolidBrush(accent)) g.FillRectangle(stripe, r.Left, r.Bottom - 4, r.Width, 4);

        var url = info?.Image ?? _db.Weapon(_def)?.Image ?? "";
        var img = _images.Get(url, 512, () => { if (IsHandleCreated) BeginInvoke(_preview.Invalidate); });
        if (img != null)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(img, CardGrid.Fit(img.Size, Rectangle.Inflate(r, -14, -14)));
        }

        if (_work.Paint != 0)
        {
            var text = $"{ItemData.WearName(_work.Wear)}  ·  {_work.Wear.ToString("0.000000", CultureInfo.InvariantCulture)}";
            TextRenderer.DrawText(g, text, Theme.Small, new Rectangle(r.Left + 10, r.Top + 8, r.Width - 20, 18), Theme.Muted, TextFormatFlags.NoPrefix);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Stickers / charm
    // ---------------------------------------------------------------------------------------------

    private void PickSticker(int slot)
    {
        if (_db.Stickers.Count == 0)
        {
            SetStatus("Sticker list not downloaded yet (PLAY > Install / update everything).", true);
            return;
        }

        var current = _work.Stickers.FirstOrDefault(s => s.Slot == slot);
        using var picker = new StickerPicker(_db.Stickers, _images, "Pick a sticker for slot " + (slot + 1), current?.Id ?? 0, current?.Wear ?? 0, true);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.PickedId == 0) return;

        _work.Stickers.RemoveAll(s => s.Slot == slot);
        _work.Stickers.Add(new StickerSettings { Slot = slot, Id = picker.PickedId, Wear = picker.PickedWear });
        ShowAttachments();
    }

    private void PickCharm()
    {
        if (_db.Charms.Count == 0)
        {
            SetStatus("Charm list not downloaded yet (PLAY > Install / update everything).", true);
            return;
        }

        using var picker = new StickerPicker(_db.Charms, _images, "Pick a charm", _work.Keychain?.Id ?? 0, 0, false);
        if (picker.ShowDialog(this) != DialogResult.OK || picker.PickedId == 0) return;
        _work.Keychain = new KeychainSettings { Id = picker.PickedId };
        ShowAttachments();
    }

    // ---------------------------------------------------------------------------------------------
    // Save / import
    // ---------------------------------------------------------------------------------------------

    private PlayerLoadout ReadLoadout() =>
        _game != null && Directory.Exists(_game.PluginDir)
            ? new LoadoutStore(_game.LoadoutFile).Get(LoadoutStore.LocalKey)
            : new PlayerLoadout();

    private void Equip()
    {
        if (_game == null || !_game.PluginInstalled || _def == 0) return;
        if (ItemData.Gloves.ContainsValue(_def) && _work.Paint == 0)
        {
            SetStatus("Pick a glove skin first - gloves can't be vanilla.", true);
            return;
        }

        Save(_def, Clone(_work));
        SetStatus(TestSession.Cs2Running()
            ? "Equipped. It changes in game in about a second (gloves may need a respawn)."
            : "Equipped. You'll have it next time you start.", false);
    }

    private void Save(ushort def, SkinSettings skin)
    {
        var store = new LoadoutStore(_game!.LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        if (ItemData.Knives.ContainsValue(def)) loadout.Knife = def;
        if (ItemData.Gloves.ContainsValue(def)) loadout.Gloves = def;
        if (skin.HasCustomization) loadout.Skins[def] = skin;
        else loadout.Skins.Remove(def);
        store.Save();

        _cachedLoadout = null;
        _weapons.Invalidate();
        FillGrid();
        ShowDetails();
    }

    private void Unequip()
    {
        if (_game == null || !_game.PluginInstalled) return;
        var store = new LoadoutStore(_game.LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        loadout.Skins.Remove(_def);
        if (loadout.Knife == _def) loadout.Knife = 0;
        if (loadout.Gloves == _def) loadout.Gloves = 0;
        store.Save();

        _cachedLoadout = null;
        _work = new SkinSettings();
        _weapons.Invalidate();
        FillGrid();
        ShowDetails();
        SetStatus("Back to default.", false);
    }

    private void Import()
    {
        using var dialog = new ImportDialog(_db, _images);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;

        var item = dialog.Result;
        if (_game == null || !_game.PluginInstalled)
        {
            SetStatus("Install the plugin first (PLAY page).", true);
            return;
        }

        Save(item.DefIndex, item.Skin);
        SelectWeapon(item.DefIndex);
        SetStatus("Imported and equipped.", false);
    }

    private void Copy(string text, string message)
    {
        try
        {
            Clipboard.SetText(text);
            SetStatus(message, false);
        }
        catch (Exception)
        {
            SetStatus("Could not use the clipboard.", true);
        }
    }

    private void SetStatus(string text, bool error)
    {
        _status.ForeColor = error ? Theme.Bad : Theme.Good;
        _status.Text = text;
    }

    private static SkinSettings Clone(SkinSettings s) => new()
    {
        Paint = s.Paint, Wear = s.Wear, Seed = s.Seed, StatTrak = s.StatTrak, StatTrakCount = s.StatTrakCount,
        Nametag = s.Nametag, Legacy = s.Legacy,
        Stickers = s.Stickers.Select(x => new StickerSettings { Slot = x.Slot, Id = x.Id, Wear = x.Wear, Scale = x.Scale, Rotation = x.Rotation, OffsetX = x.OffsetX, OffsetY = x.OffsetY }).ToList(),
        Keychain = s.Keychain == null ? null : new KeychainSettings { Id = s.Keychain.Id, OffsetX = s.Keychain.OffsetX, OffsetY = s.Keychain.OffsetY, OffsetZ = s.Keychain.OffsetZ, Seed = s.Keychain.Seed },
    };

    private static string Pretty(string name) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name);
}
