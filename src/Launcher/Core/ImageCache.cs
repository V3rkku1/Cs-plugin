using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Downloads item pictures once and keeps them on disk (%LocalAppData%\LanSkinTester\images) and in memory.
/// Requests from Steam's image server ask for a small size so the grid loads fast.
/// </summary>
public class ImageCache
{
    private readonly HttpClient _http;
    private readonly string _dir;
    private readonly ConcurrentDictionary<string, Image?> _memory = new();
    private readonly ConcurrentDictionary<string, Task<Image?>> _pending = new();
    private readonly SemaphoreSlim _downloads = new(6);

    public ImageCache(HttpClient http)
    {
        _http = http;
        _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSkinTester", "images");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Returns the image right away if it's loaded; otherwise starts loading and calls <paramref name="loaded"/> later (on any thread).</summary>
    public Image? Get(string url, int size, Action? loaded = null)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var key = Sized(url, size);
        if (_memory.TryGetValue(key, out var image)) return image;

        var task = _pending.GetOrAdd(key, k => LoadAsync(k));
        if (loaded != null) task.ContinueWith(_ => loaded(), TaskScheduler.Default);
        return null;
    }

    private async Task<Image?> LoadAsync(string url)
    {
        Image? image = null;
        var file = Path.Combine(_dir, Hash(url) + ".png");
        try
        {
            if (File.Exists(file))
            {
                image = FromBytes(await File.ReadAllBytesAsync(file));
            }
            else
            {
                await _downloads.WaitAsync();
                try
                {
                    var bytes = await _http.GetByteArrayAsync(url);
                    image = FromBytes(bytes);
                    await File.WriteAllBytesAsync(file, bytes);
                }
                finally
                {
                    _downloads.Release();
                }
            }
        }
        catch (Exception)
        {
            // Offline or missing picture: the card shows its name only.
        }

        _memory[url] = image;
        _pending.TryRemove(url, out _);
        return image;
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

    private static Image FromBytes(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var img = Image.FromStream(ms);
        return new Bitmap(img); // detach from the stream
    }

    // Steam's economy image server resizes on request: .../economy/image/<id>/256fx256f
    private static string Sized(string url, int size) =>
        url.Contains("/economy/image/") ? $"{url.TrimEnd('/')}/{size}fx{size}f" : url;

    private static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..20];
}
