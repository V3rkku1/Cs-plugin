using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Real market prices for the Cases and Inventory pages. One request gets every CS2 item's price from
/// Skinport's public price list (no account needed; refreshed every 12 hours, cached on disk). Items
/// it doesn't have are asked from the Steam Community Market one at a time. Prices are for showing only.
/// </summary>
public class PriceList
{
    public static readonly string[] Currencies = ["USD", "EUR", "GBP"];

    /// <summary>A case key from Valve's store (not on the market): the usual price in each currency.</summary>
    public static double KeyPrice(string currency) => currency switch { "EUR" => 2.35, "GBP" => 2.05, _ => 2.49 };

    private static readonly HttpClient Http = new(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
        Timeout = TimeSpan.FromSeconds(60),
    };

    private readonly string _dir;
    private readonly Action<string> _log;
    private Dictionary<string, double> _prices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double?> _steam = new(StringComparer.OrdinalIgnoreCase);
    private string _currency = "";
    private DateTime _loadedAt;
    private Task? _loading;

    public PriceList(string dir, Action<string> log)
    {
        _dir = dir;
        _log = log;
    }

    public bool Ready => _prices.Count > 0;

    /// <summary>Loads the cached list, and downloads a new one in the background when it is old.</summary>
    public void Ensure(string currency)
    {
        if (!Currencies.Contains(currency)) currency = "USD";
        if (_currency != currency)
        {
            _currency = currency;
            _prices = ReadCache(currency, out _loadedAt);
            lock (_steam) _steam.Clear();
        }
        if (DateTime.UtcNow - _loadedAt > TimeSpan.FromHours(12) && _loading is null or { IsCompleted: true })
            _loading = DownloadAsync(currency);
    }

    private string CacheFile(string currency) => Path.Combine(_dir, $"prices-{currency.ToLowerInvariant()}.json");

    private Dictionary<string, double> ReadCache(string currency, out DateTime when)
    {
        when = DateTime.MinValue;
        try
        {
            var file = CacheFile(currency);
            if (!File.Exists(file)) return new(StringComparer.OrdinalIgnoreCase);
            when = File.GetLastWriteTimeUtc(file);
            var data = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(file));
            return data == null ? new(StringComparer.OrdinalIgnoreCase) : new(data, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task DownloadAsync(string currency)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.skinport.com/v1/items?app_id=730&currency={currency}&tradable=0");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("market_hash_name", out var name) || name.GetString() is not { } hash) continue;
                double? Read(string field) => item.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                // Suggested is Skinport's market estimate; the cheapest listing when there is none.
                var price = Read("suggested_price") ?? Read("median_price") ?? Read("min_price");
                if (price is > 0) prices[hash] = Math.Round(price.Value, 2);
            }
            if (prices.Count == 0) return;
            Directory.CreateDirectory(_dir);
            File.WriteAllText(CacheFile(currency), JsonSerializer.Serialize(prices));
            if (_currency == currency)
            {
                _prices = prices;
                _loadedAt = DateTime.UtcNow;
            }
            _log($"Prices updated: {prices.Count} items ({currency}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _log("Could not download prices (they show when online): " + ex.Message);
            _loadedAt = DateTime.UtcNow - TimeSpan.FromHours(11); // try again in an hour
        }
    }

    /// <summary>The price of a market name, or null when unknown (a Steam lookup then runs in the background).</summary>
    public double? Price(string marketName)
    {
        if (_prices.TryGetValue(marketName, out var p)) return p;
        lock (_steam)
        {
            if (_steam.TryGetValue(marketName, out var known)) return known;
            _steam[marketName] = null; // asked once per run
        }
        _ = SteamPriceAsync(marketName);
        return null;
    }

    private static readonly SemaphoreSlim SteamGate = new(1, 1);

    /// <summary>Steam's own price for one item (rate-limited by Steam: one request at a time, spaced out).</summary>
    private async Task SteamPriceAsync(string marketName)
    {
        await SteamGate.WaitAsync();
        try
        {
            var currencyId = _currency switch { "EUR" => 3, "GBP" => 2, _ => 1 };
            var url = $"https://steamcommunity.com/market/priceoverview/?appid=730&currency={currencyId}&market_hash_name={Uri.EscapeDataString(marketName)}";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var text = doc.RootElement.TryGetProperty("median_price", out var m) ? m.GetString()
                : doc.RootElement.TryGetProperty("lowest_price", out var l) ? l.GetString() : null;
            var digits = new string((text ?? "").Where(c => char.IsDigit(c) || c is ',' or '.').ToArray()).Replace(',', '.');
            // "1.234.56" -> keep the last dot as the decimal point
            var last = digits.LastIndexOf('.');
            if (last > 0) digits = digits[..last].Replace(".", "") + digits[last..];
            if (double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) && price > 0)
                lock (_steam) _steam[marketName] = price;
            await Task.Delay(3500);
        }
        catch (Exception) { /* unknown stays unknown */ }
        finally { SteamGate.Release(); }
    }

    public static string WearName(float wear) => wear switch
    {
        < 0.07f => "Factory New", < 0.15f => "Minimal Wear", < 0.38f => "Field-Tested", < 0.45f => "Well-Worn", _ => "Battle-Scarred",
    };

    /// <summary>
    /// The Steam market name of an item: "StatTrak™ AK-47 | Redline (Field-Tested)", "★ StatTrak™ Karambit |
    /// Fade (Factory New)", "Souvenir AWP | Dragon Lore (Minimal Wear)", "★ Karambit", "Sticker | ...".
    /// </summary>
    public static string MarketName(string name, bool skin, bool hasPaint, float wear, bool statTrak, bool souvenir)
    {
        name = name.Replace("StatTrak™ ", "").Trim();
        var star = name.StartsWith("★");
        var baseName = star ? name[1..].Trim() : name;
        var prefix = (star ? "★ " : "") + (statTrak ? "StatTrak™ " : "") + (souvenir && !star ? "Souvenir " : "");
        if (!skin) return (statTrak && !star ? "StatTrak™ " : "") + baseName;
        return prefix + baseName + (hasPaint ? $" ({WearName(wear)})" : "");
    }
}
