using System.Globalization;
using System.Text.RegularExpressions;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// Paste a real item: an inspect link with item data, a !gen code (CSFloat "Copy !gen"), or a Steam Market
/// listing name/URL (exact float/pattern isn't in those, so a typical value for the condition is used).
/// </summary>
public class ImportDialog : Form
{
    private static readonly (string Name, float Min, float Max)[] Conditions =
    [
        ("Factory New", 0f, 0.07f), ("Minimal Wear", 0.07f, 0.15f), ("Field-Tested", 0.15f, 0.38f),
        ("Well-Worn", 0.38f, 0.45f), ("Battle-Scarred", 0.45f, 1f),
    ];

    private readonly ItemDatabase _db;
    private readonly ImageCache _images;
    private readonly TextField _input = new(612, multiline: true, height: 84) { PlaceholderText = "Paste a !gen code or inspect link…" };
    private readonly Panel _preview = new() { Width = 612, Height = 200 };
    private readonly Label _summary = new() { AutoSize = true, MaximumSize = new Size(584, 0), ForeColor = Theme.Text, BackColor = Color.Transparent };
    private readonly PillButton _import = new("Import and Equip", PillButton.Style.Primary);

    public ImportedItem? Result { get; private set; }

    public ImportDialog(ItemDatabase db, ImageCache images)
    {
        _db = db;
        _images = images;
        Text = "Import a Real Item";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Theme.StyleWindow(this);
        ClientSize = new Size(660, 640);

        var title = new Label { Text = "Import a Real Item", Font = Theme.Big, ForeColor = Theme.Text, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 4) };
        var help = new Label
        {
            AutoSize = true, MaximumSize = new Size(612, 0), ForeColor = Theme.Muted, BackColor = Color.Transparent, Margin = new Padding(2, 0, 0, 12),
            Text = "Paste a !gen code (CSFloat → Copy !gen), an inspect link with item data (csgo_econ_action_preview 00…), " +
                   "or a Steam Market listing name or link. Old market \"Inspect in Game\" links (…M123A456D789) don't " +
                   "contain the item - open those on CSFloat and copy the !gen code.",
        };

        var paste = new PillButton("Paste");
        var cancel = new PillButton("Cancel") { DialogResult = DialogResult.Cancel };
        paste.Click += (_, _) => { try { _input.Text = Clipboard.GetText(); } catch (Exception) { } };
        _import.Click += (_, _) => { if (Result != null) DialogResult = DialogResult.OK; };

        var previewGroup = new Group(612);
        previewGroup.AddContent(_preview, Padding.Empty);
        previewGroup.AddContent(_summary, new Padding(14, 12, 14, 14));

        var layout = new VStack(
            title, help, _input,
            new Panel { Height = 14, Width = 10, BackColor = Color.Transparent },
            previewGroup,
            new HStack(paste, cancel, _import) { Margin = new Padding(0, 16, 0, 0) }) { Location = new Point(24, 20) };
        Controls.Add(layout);
        Theme.Apply(this);
        CancelButton = cancel;

        _input.TextChanged += (_, _) => Parse();
        _preview.Paint += DrawPreview;
        Parse();

        // Pre-fill from the clipboard when it looks like an item.
        Shown += (_, _) =>
        {
            try
            {
                var clip = Clipboard.GetText().Trim();
                if (clip.Contains("csgo_econ_action_preview") || clip.StartsWith("!g") || clip.Contains("/market/listings/730/"))
                    _input.Text = clip;
            }
            catch (Exception) { }
            _input.Box.Focus();
        };
    }

    private void Parse()
    {
        Result = null;
        var text = _input.Text.Trim();
        string error;

        if (text == "")
        {
            ShowSummary("", false);
            return;
        }

        if (!ItemImport.TryParse(text, out var item, out error) && !TryMarketName(text, out item, ref error))
        {
            ShowSummary(error, true);
            return;
        }

        Result = item;
        var info = _db.Find(item.DefIndex, item.Skin.Paint);
        var name = info?.FullName ?? $"{_db.Weapon(item.DefIndex)?.Name ?? ItemData.NameOf(item.DefIndex)} | paint {item.Skin.Paint}";
        var lines = new List<string>
        {
            (item.Skin.StatTrak ? "StatTrak™ " : "") + name,
            $"Float {item.Skin.Wear.ToString("0.00000000", CultureInfo.InvariantCulture)} ({ItemData.WearName(item.Skin.Wear)})   ·   Pattern {item.Skin.Seed}",
        };
        if (item.Skin.Stickers.Count > 0)
            lines.Add("Stickers: " + string.Join(", ", item.Skin.Stickers.OrderBy(s => s.Slot).Select(s => _db.Sticker(s.Id)?.Name ?? $"#{s.Id}")));
        if (item.Skin.Keychain != null) lines.Add("Charm: " + (_db.Charm(item.Skin.Keychain.Id)?.Name ?? $"#{item.Skin.Keychain.Id}"));
        if (item.Skin.Nametag != "") lines.Add($"Name tag: \"{item.Skin.Nametag}\"");
        if (error != "") lines.Add(error);
        ShowSummary(string.Join("\n", lines), false);
    }

    /// <summary>"StatTrak™ AK-47 | Redline (Field-Tested)" or a steamcommunity.com/market/listings/730/... link.</summary>
    private bool TryMarketName(string text, out ImportedItem item, ref string error)
    {
        item = new ImportedItem();
        var url = Regex.Match(text, @"/market/listings/730/([^?#\s]+)");
        var name = url.Success ? Uri.UnescapeDataString(url.Groups[1].Value) : text;
        if (!name.Contains('|')) return false;

        name = name.Replace("★", "").Trim();
        var statTrak = name.StartsWith("StatTrak™", StringComparison.OrdinalIgnoreCase);
        name = Regex.Replace(name, @"^(StatTrak™|Souvenir)\s*", "", RegexOptions.IgnoreCase).Trim();

        var condition = Regex.Match(name, @"\((Factory New|Minimal Wear|Field-Tested|Well-Worn|Battle-Scarred)\)\s*$");
        if (condition.Success) name = name[..condition.Index].Trim();

        var skin = _db.Skins.FirstOrDefault(s => $"{s.Weapon} | {s.Finish}".Equals(name, StringComparison.OrdinalIgnoreCase));
        if (skin == null)
        {
            error = _db.IsEmpty ? "Download the skin list first (PLAY > Install / update everything) to import by name." : $"No skin called \"{name}\" found.";
            return false;
        }

        var range = condition.Success ? Conditions.First(c => c.Name == condition.Groups[1].Value) : Conditions[0];
        var min = Math.Max(range.Min, skin.MinFloat);
        var max = Math.Min(range.Max, skin.MaxFloat);
        item.DefIndex = skin.DefIndex;
        item.Skin = new SkinSettings { Paint = skin.Paint, Wear = Math.Max(min + (max - min) * 0.4f, 0.00001f), StatTrak = statTrak };
        error = "Market names don't include the exact float or pattern - a typical value was used. " +
                "For an exact copy paste the listing's !gen code from CSFloat.";
        return true;
    }

    private void ShowSummary(string text, bool isError)
    {
        _summary.Text = text;
        _summary.ForeColor = isError ? Theme.Bad : Theme.Text;
        _summary.Visible = text != "";
        _import.Enabled = Result != null;
        _preview.Invalidate();
    }

    private void DrawPreview(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        var r = _preview.ClientRectangle;
        if (Result == null)
        {
            Theme.DrawText(g, "The item appears here", Theme.Heading, r, Theme.Tertiary, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        var info = _db.Find(Result.DefIndex, Result.Skin.Paint);
        var accent = Theme.Parse(info?.RarityColor, Theme.Tertiary);
        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
        {
            path.AddEllipse(RectangleF.Inflate(r, -r.Width * 0.15f, -r.Height * 0.05f));
            using var glow = new System.Drawing.Drawing2D.PathGradientBrush(path) { CenterColor = Color.FromArgb(80, accent), SurroundColors = [Color.FromArgb(0, accent)] };
            g.FillPath(glow, path);
        }
        var img = _images.Get(info?.Image ?? _db.Weapon(Result.DefIndex)?.Image ?? "", 512, () => { if (IsHandleCreated) BeginInvoke(_preview.Invalidate); });
        if (img != null) g.DrawImage(img, CardGrid.Fit(img.Size, Rectangle.Inflate(r, -16, -14)));
    }
}
