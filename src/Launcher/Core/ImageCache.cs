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
    private readonly string _dir = Path.Combine(AppState.LauncherDataDir, "images");
    private readonly SemaphoreSlim _downloads = new(6);

    public ImageCache(HttpClient http)
    {
        _http = http;
        Directory.CreateDirectory(_dir);
    }

    /// <summary>The picture's file bytes (disk cache first), or null when it can't be downloaded.</summary>
    public async Task<byte[]?> GetBytesAsync(string url, int size)
    {
        // Steam's economy image server resizes on request: .../economy/image/<id>/256fx256f
        if (url.Contains("/economy/image/")) url = $"{url.TrimEnd('/')}/{size}fx{size}f";
        var file = Path.Combine(_dir, Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(url)))[..20] + ".png");
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
}
