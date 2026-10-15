using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Ui;

/// <summary>Search and pick a sticker (with scrape amount) or a charm.</summary>
public class StickerPicker : Form
{
    private static readonly string[] Effects = ["All", "Paper", "Holo", "Foil", "Glitter", "Gold", "Lenticular"];

    private readonly List<StickerInfo> _all;
    private readonly CardGrid _grid = new() { Dock = DockStyle.Fill, CardSize = new Size(150, 146) };
    private readonly TextField _search = new(380) { PlaceholderText = "Search team, player, tournament or name" };
    private readonly Segmented _effect = new(Effects);
    private readonly Slider _scrape = new() { Minimum = 0, Maximum = 100, Width = 200 };
    private readonly Label _scrapeLabel = new() { AutoSize = false, Width = 110, Height = 28, ForeColor = Theme.Muted, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, BackColor = Color.Transparent, Margin = new Padding(12, 8, 0, 0) };

    public int PickedId { get; private set; }
    public float PickedWear => _scrape.Value / 100f;

    public StickerPicker(List<StickerInfo> items, ImageCache images, string title, int currentId, float currentWear, bool isSticker)
    {
        _all = items;
        Text = title;
        Size = new Size(1040, 760);
        StartPosition = FormStartPosition.CenterParent;
        Theme.StyleWindow(this);
        _grid.SetImageCache(images);

        var heading = new SmoothLabel { Text = title, Font = Theme.Big, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 10) };
        var top = new Panel { Dock = DockStyle.Top, Height = isSticker ? 128 : 92, BackColor = Theme.Background, Padding = new Padding(24, 18, 24, 0) };
        var stack = isSticker
            ? new VStack(heading, new HStack(_search, _count), new HStack(_effect) { Margin = new Padding(0, 10, 0, 0) })
            : new VStack(heading, new HStack(_search, _count));
        stack.Location = new Point(24, 18);
        top.Controls.Add(stack);

        var use = new PillButton("Use", PillButton.Style.Primary) { AutoWidth = false };
        use.Width = 110;
        var cancel = new PillButton("Cancel") { DialogResult = DialogResult.Cancel };
        use.Click += (_, _) => Accept();
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Sidebar };
        var buttons = new HStack(cancel, use);
        bottom.Controls.Add(buttons);
        if (isSticker)
        {
            var scrape = new HStack(new SmoothLabel { Text = "Scrape", AutoSize = true, ForeColor = Theme.Text, BackColor = Color.Transparent, Margin = new Padding(0, 6, 12, 0) }, _scrape, _scrapeLabel);
            scrape.Location = new Point(24, 18);
            bottom.Controls.Add(scrape);
        }
        bottom.Layout += (_, _) => buttons.Location = new Point(bottom.Width - buttons.Width - 24, 16);

        var gridHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 14, 4), BackColor = Theme.Background };
        gridHost.Controls.Add(_grid);
        Controls.Add(gridHost);
        Controls.Add(top);
        Controls.Add(bottom);
        Theme.Apply(this);
        AcceptButton = use;
        CancelButton = cancel;

        _scrape.Value = (int)Math.Round(Math.Clamp(currentWear, 0, 1) * 100);
        _scrapeLabel.Text = ScrapeText();
        _scrape.ValueChanged += (_, _) => _scrapeLabel.Text = ScrapeText();
        _search.TextChanged += (_, _) => Filter();
        _effect.SelectedChanged += (_, _) => Filter();
        _grid.ItemActivated += (_, _) => Accept();

        Filter();
        if (currentId != 0) _grid.Select(c => (int)c.Tag! == currentId);
        Shown += (_, _) => _search.Box.Focus();
    }

    private string ScrapeText() => _scrape.Value == 0 ? "  New" : $"  {_scrape.Value}%";

    private void Filter()
    {
        var words = _search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var effect = Effects[_effect.SelectedIndex];
        var matches = _all
            .Where(s => effect == "All" || s.Effect.Equals(effect, StringComparison.OrdinalIgnoreCase))
            .Where(s => words.All(w => s.Name.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _count.Text = $"{matches.Count:N0} found";
        // Newest first: higher ids are newer releases.
        _grid.SetItems(matches.OrderByDescending(s => s.Id).Select(s => new CardItem
        {
            Tag = s.Id, Title = s.Name, Subtitle = s.Rarity, Accent = Theme.Parse(s.RarityColor, Theme.Muted), ImageUrl = s.Image,
        }), "Nothing found.");
    }

    private void Accept()
    {
        if (_grid.SelectedItem?.Tag is not int id) return;
        PickedId = id;
        DialogResult = DialogResult.OK;
    }
}
