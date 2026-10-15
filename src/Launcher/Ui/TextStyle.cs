using System.Drawing.Text;

namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// How text looks, chosen on the Setup page and read once at start (before any font is made):
/// the embedded Inter font or Windows' own Segoe UI, ClearType-sharp or smooth, normal or large.
/// </summary>
public static class TextStyle
{
    public static bool UseSegoe { get; private set; }
    public static bool Sharp { get; private set; } = true;
    public static float SizeBoost { get; private set; }

    public static TextRenderingHint Hint => Sharp ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAlias;

    public static void Configure(string? font, string? rendering, string? size)
    {
        UseSegoe = string.Equals(font, "segoe", StringComparison.OrdinalIgnoreCase);
        Sharp = !string.Equals(rendering, "smooth", StringComparison.OrdinalIgnoreCase);
        SizeBoost = string.Equals(size, "large", StringComparison.OrdinalIgnoreCase) ? 1f : 0f;
    }
}
