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
    private readonly TextBox _input = new() { Multiline = true, Height = 90, Dock = DockStyle.Top, ScrollBars = ScrollBars.Vertical };
    private readonly Panel _preview = new() { Dock = DockStyle.Fill, BackColor = Theme.Card };
    private readonly Label _summary = new() { Dock = DockStyle.Bottom, Height = 110, ForeColor = Theme.Text, Padding = new Padding(12, 8, 12, 8) };
    private readonly Button _import;

    public ImportedItem? Result { get; private set; }

    public ImportDialog(ItemDatabase db, ImageCache images)
    {
        _db = db;
        _images = images;
        Text = "Import a real item";
        Size = new Size(720, 620);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;

        var help = new Label
        {
            Dock = DockStyle.Top, Height = 92, ForeColor = Theme.Muted, Padding = new Padding(0, 0, 0, 6),
            Text = "Paste one of these:\n" +
                   "  •  an inspect link with item data:  steam://run/730//+csgo_econ_action_preview 00…\n" +
                   "  •  a !gen code, e.g. from CSFloat's \"Copy !gen\":  !gen 7 282 661 0.15 …\n" +
                   "  •  a Steam Market listing name or link (exact float/pattern isn't in those)\n" +
                   "Old market \"Inspect in Game\" links (…M123A456D789) don't contain the item - use CSFloat for those.",
        };

        var paste = Theme.MakeButton("Paste");
        _import = Theme.MakeButton("Import and equip", Theme.ButtonKind.Primary, 200);
        var cancel = Theme.MakeButton("Cancel");
        paste.Click += (_, _) => { try { _input.Text = Clipboard.GetText(); } catch (Exception) { } };
        _import.Click += (_, _) => { if (Result != null) DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(0, 8, 0, 0), BackColor = Theme.Background };
        buttons.Controls.AddRange([_import, paste, cancel]);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Theme.Background };
        var spacer = new Panel { Dock = DockStyle.Top, Height = 10, BackColor = Theme.Background };
        var previewBox = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        previewBox.Controls.Add(_preview);
        previewBox.Controls.Add(_summary);
        body.Controls.Add(previewBox);
        body.Controls.Add(spacer);
        body.Controls.Add(_input);
        body.Controls.Add(help);
        body.Controls.Add(buttons);
        Controls.Add(body);
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
            _input.Focus();
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
        _import.Enabled = Result != null;
        _preview.Invalidate();
    }

    private void DrawPreview(object? sender, PaintEventArgs e)
    {
        if (Result == null) return;
        var info = _db.Find(Result.DefIndex, Result.Skin.Paint);
        var accent = Theme.Parse(info?.RarityColor, Theme.Border);
        var r = _preview.ClientRectangle;
        using (var stripe = new SolidBrush(accent)) e.Graphics.FillRectangle(stripe, r.Left, r.Bottom - 3, r.Width, 3);

        var img = _images.Get(info?.Image ?? _db.Weapon(Result.DefIndex)?.Image ?? "", 512, () => { if (IsHandleCreated) BeginInvoke(_preview.Invalidate); });
        if (img != null) e.Graphics.DrawImage(img, CardGrid.Fit(img.Size, Rectangle.Inflate(r, -12, -12)));
    }
}
