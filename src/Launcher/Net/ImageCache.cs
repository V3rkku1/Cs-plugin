using System.Security.Cryptography;
using System.Text;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Downloads item pictures once and keeps them on disk (%LocalAppData%\LanSkinTester\images).
/// Requests from Steam's image server ask for a small size so the grid loads fast.
/// </summary>
public class ImageCache
{
    private readonly HttpClient _http;
    private readonly string _dir;
    private readonly SemaphoreSlim _downloads = new(6);

    public ImageCache(HttpClient http)
    {
        _http = http;
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSkinTester", "images");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>The picture's file bytes (disk cache first), or null when it can't be downloaded.</summary>
    public async Task<byte[]?> GetBytesAsync(string url, int size)
    {
        url = Sized(url, size);
        var file = Path.Combine(_dir, Hash(url) + ".png");
        try
        {
            if (File.Exists(file)) return await File.ReadAllBytesAsync(file);
            await _downloads.WaitAsync();
            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(file, bytes);
                return bytes;
            }
            finally
            {
                _downloads.Release();
            }
        }
        catch (Exception)
        {
            return null; // offline or missing picture
        }
    }

    // Steam's economy image server resizes on request: .../economy/image/<id>/256fx256f
    private static string Sized(string url, int size) =>
        url.Contains("/economy/image/") ? $"{url.TrimEnd('/')}/{size}fx{size}f" : url;

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..20];
}
