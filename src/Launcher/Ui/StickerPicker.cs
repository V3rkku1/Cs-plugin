using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Ui;

/// <summary>Search and pick a sticker (with scrape amount) or a charm.</summary>
public class StickerPicker : Form
{
    private static readonly string[] Effects = ["All", "Paper", "Holo", "Foil", "Glitter", "Gold", "Lenticular"];

    private readonly List<StickerInfo> _all;
    private readonly CardGrid _grid = new() { Dock = DockStyle.Fill, CardSize = new Size(140, 140) };
    private readonly TextBox _search = new() { PlaceholderText = "Search (team, player, tournament, name...)", Width = 360 };
    private readonly Segmented _effect = new(Effects);
    private readonly TrackBar _scrape = new() { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Width = 200, Height = 28 };
    private readonly Label _scrapeLabel = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(4, 8, 0, 0) };
    private readonly Label _count = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(10, 8, 0, 0) };

    public int PickedId { get; private set; }
    public float PickedWear => _scrape.Value / 100f;

    public StickerPicker(List<StickerInfo> items, ImageCache images, string title, int currentId, float currentWear, bool isSticker)
    {
        _all = items;
        Text = title;
        Size = new Size(1000, 720);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        _grid.SetImageCache(images);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = isSticker ? 84 : 46, Padding = new Padding(10, 8, 10, 0), BackColor = Theme.Surface };
        top.Controls.Add(_search);
        top.Controls.Add(_count);
        if (isSticker)
        {
            top.SetFlowBreak(_count, true);
            top.Controls.Add(_effect);
        }

        var use = Theme.MakeButton("Use", Theme.ButtonKind.Primary, 120);
        var cancel = Theme.MakeButton("Cancel");
        use.Click += (_, _) => Accept();
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 8, 10, 0), BackColor = Theme.Surface };
        if (isSticker)
        {
            bottom.Controls.Add(new Label { Text = "Scrape:", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 8, 4, 0) });
            bottom.Controls.Add(_scrape);
            bottom.Controls.Add(_scrapeLabel);
        }
        bottom.Controls.Add(use);
        bottom.Controls.Add(cancel);

        Controls.Add(_grid);
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
        Shown += (_, _) => _search.Focus();
    }

    private string ScrapeText() => _scrape.Value == 0 ? "new" : $"{_scrape.Value}% scraped";

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
