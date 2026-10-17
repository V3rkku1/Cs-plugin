using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// Setup › Appearance: theme colours, panel opacity, corners, font, a background picture, theme codes to
/// share, and custom.css in the launcher's data folder for anything else (loaded after the built-in styles).
/// </summary>
public partial class Api
{
    public static string BackgroundFile(string ext) => Path.Combine(AppState.LauncherDataDir, "theme-background" + ext);
    public static string CustomCssFile => Path.Combine(AppState.LauncherDataDir, "custom.css");
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".webp"];

    /// <summary>The background picture on disk, if one was chosen.</summary>
    public static string? BackgroundPath() =>
        ImageExtensions.Select(BackgroundFile).FirstOrDefault(File.Exists);

    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$");

    /// <summary>Allowed theme keys and how a value is checked (null = not allowed).</summary>
    private static string? CleanThemeValue(string key, string value) => key switch
    {
        "accent" or "background" or "panel" or "text" => HexColor.IsMatch(value) ? value.ToLowerInvariant() : null,
        "opacity" => Number(value, 40, 100),
        "radius" => Number(value, 0, 160),
        "dim" => Number(value, 0, 90),
        "font" => value is "inter" or "segoe" or "system" or "mono" ? value : null,
        "preset" => Regex.IsMatch(value, "^[a-z0-9-]{1,24}$") ? value : null,
        "titlebar" => value is "theme" or "accent" or "system" ? value : null,
        _ => null,
    };

    private static string? Number(string value, int min, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max
            ? n.ToString(CultureInfo.InvariantCulture) : null;

    /// <summary>Changes some theme values: { "accent": "#ff9f0a", "opacity": "85" }. Null or "" removes a value.</summary>
    [Call("setup.theme")]
    private void SetTheme(Dictionary<string, string?> changes)
    {
        ApplyTheme(changes, replace: false);
        SaveTheme();
        App.NotifyChanged();
    }

    private void ApplyTheme(Dictionary<string, string?> changes, bool replace)
    {
        var theme = App.Settings.Theme;
        if (replace)
        {
            var image = theme.GetValueOrDefault("image");
            theme.Clear();
            if (image != null) theme["image"] = image;
        }
        foreach (var (key, value) in changes)
        {
            if (string.IsNullOrEmpty(value)) { theme.Remove(key); continue; }
            if (CleanThemeValue(key, value.Trim()) is { } clean) theme[key] = clean;
        }
    }

    [Call("setup.themeReset")]
    private void ResetTheme()
    {
        App.Settings.Theme.Clear();
        foreach (var file in ImageExtensions.Select(BackgroundFile)) TryDelete(file);
        SaveTheme();
        Toast("Appearance back to the default look.", "good");
        App.NotifyChanged();
    }

    [Call("setup.themeImage")]
    private async Task PickBackground()
    {
        var file = await OnUi(() =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Background picture",
                Filter = "Pictures (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp",
            };
            return dialog.ShowDialog(Window) == DialogResult.OK ? dialog.FileName : null;
        });
        if (file == null) return;
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (!ImageExtensions.Contains(ext)) throw new ApiException("Pick a PNG, JPG or WebP picture.");
        if (new FileInfo(file).Length > 25_000_000) throw new ApiException("That picture is too big (over 25 MB).");
        Directory.CreateDirectory(AppState.LauncherDataDir);
        foreach (var old in ImageExtensions.Select(BackgroundFile)) TryDelete(old);
        File.Copy(file, BackgroundFile(ext), overwrite: true);
        // The value only changes the picture's address, so the page loads the new one.
        App.Settings.Theme["image"] = DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
        SaveTheme();
        App.NotifyChanged();
    }

    [Call("setup.themeImageRemove")]
    private void RemoveBackground()
    {
        foreach (var file in ImageExtensions.Select(BackgroundFile)) TryDelete(file);
        App.Settings.Theme.Remove("image");
        SaveTheme();
        App.NotifyChanged();
    }

    /// <summary>Copies the theme as a short code friends can paste (the background picture isn't in it).</summary>
    [Call("setup.themeExport")]
    private async Task<string> ExportTheme()
    {
        var values = App.Settings.Theme.Where(kv => kv.Key != "image").ToDictionary(kv => kv.Key, kv => kv.Value);
        var code = "LST1-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));
        var ok = await OnUi(() =>
        {
            try { Clipboard.SetText(code); return true; }
            catch (Exception) { return false; }
        });
        Toast(ok ? "Theme code copied. Send it to a friend: they paste it on Setup › Appearance." : "Could not use the clipboard.", ok ? "good" : "bad");
        return code;
    }

    [Call("setup.themeImport")]
    private void ImportTheme(string code)
    {
        code = (code ?? "").Trim();
        Dictionary<string, string?>? values = null;
        try
        {
            var json = code.StartsWith("LST1-") ? Encoding.UTF8.GetString(Convert.FromBase64String(code[5..])) : code;
            values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch (Exception e) when (e is FormatException or JsonException or DecoderFallbackException)
        {
            // handled below
        }
        if (values == null || values.Count == 0) throw new ApiException("That isn't a theme code (they start with LST1-).");
        values.Remove("image");
        ApplyTheme(values, replace: true);
        SaveTheme();
        Toast("Theme applied.", "good");
        App.NotifyChanged();
    }

    private const string CustomCssTemplate = """
        /*
         * LAN Skin Tester launcher: your own styles. Loaded after the built-in ones, so anything here wins.
         * Save, then click the launcher window: it reloads this file. Delete everything to go back.
         *
         * The main colours (Setup › Appearance sets these too):
         *   --bg            window background
         *   --panel         cards and the sidebar
         *   --blue          accent: main buttons, links, selected items
         *   --text, --text-2, --text-3   main, secondary and faint text
         *   --r-xl, --r-lg, --r-md, --r-sm   corner sizes
         *   --font          the font
         *
         * Examples (remove the comment marks to try them):
         */

        /* :root { --blue: #ff9f0a; --bg: #101010; } */
        /* .page-header h1 { letter-spacing: 0; font-weight: 700; } */
        /* .sidebar { border-radius: 0; } */
        /* .backdrop { background: linear-gradient(160deg, #0d1020, #1a0d1a); } */

        """;

    /// <summary>Creates custom.css with notes on first use and opens it in the default editor.</summary>
    [Call("setup.customCss")]
    private void OpenCustomCss()
    {
        Directory.CreateDirectory(AppState.LauncherDataDir);
        if (!File.Exists(CustomCssFile)) File.WriteAllText(CustomCssFile, CustomCssTemplate);
        try
        {
            Process.Start(new ProcessStartInfo(CustomCssFile) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No app for .css files: Notepad always exists.
            Process.Start("notepad.exe", $"\"{CustomCssFile}\"");
        }
        Toast("custom.css opened. Save it, then click the launcher to see the change.");
    }

    private void SaveTheme()
    {
        // The title bar is drawn by Windows: give it the new colours too.
        _ = OnUi(() => (Window as ShellForm)?.ApplyTitleBar());
        App.SaveSettings();
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { /* in use: replaced next time */ }
    }
}
