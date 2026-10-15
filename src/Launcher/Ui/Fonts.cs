using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace LanSkinTester.Launcher.Ui;

/// <summary>
/// The Inter typeface ships inside the exe (assets/fonts, SIL Open Font License), so the launcher looks
/// the same on every PC. Each file is loaded for GDI+ (custom drawing) and registered with GDI too, so
/// text boxes and list boxes can use it by name. Falls back to Segoe UI if loading fails.
/// </summary>
internal static class Fonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly Dictionary<string, FontFamily> Loaded = new();

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint fonts);

    /// <summary>The family inside the embedded file (e.g. "Inter-Regular.ttf"), or null.</summary>
    public static FontFamily? Embedded(string file)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(file, out var known)) return known;
            try
            {
                using var stream = typeof(Fonts).Assembly.GetManifestResourceStream("fonts/" + file);
                if (stream == null) return null;
                var data = new byte[stream.Length];
                stream.ReadExactly(data);

                var before = Collection.Families.Select(f => f.Name).ToHashSet();
                var memory = Marshal.AllocCoTaskMem(data.Length); // kept for the lifetime of the app
                Marshal.Copy(data, 0, memory, data.Length);
                Collection.AddMemoryFont(memory, data.Length);
                try
                {
                    uint count = 0;
                    AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
                }
                catch (Exception)
                {
                    // GDI registration is only needed for native controls.
                }

                var family = Collection.Families.FirstOrDefault(f => !before.Contains(f.Name))
                             ?? Collection.Families.LastOrDefault();
                if (family != null) Loaded[file] = family;
                return family;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>A font from the embedded file, or <paramref name="fallback"/> when that isn't possible.</summary>
    public static Font Create(string file, float size, string fallback)
    {
        var family = Embedded(file);
        if (family != null)
        {
            foreach (var style in new[] { FontStyle.Regular, FontStyle.Bold })
                if (family.IsStyleAvailable(style))
                    return new Font(family, size, style);
        }
        return new Font(fallback, size);
    }
}
