using System.Globalization;
using System.Net;
using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Real market prices for the Cases and Inventory pages. One request gets every CS2 item's price from
/// Skinport's public price list (no account needed; refreshed every 12 hours, cached on disk). Items
/// it doesn't have are asked from the Steam Community Market one at a time. Prices are for showing only.
/// </summary>
public class PriceList(string dir, Action<string> log, Action changed)
{
    public static readonly string[] Currencies = ["USD", "EUR", "GBP"];

    /// <summary>A case key from Valve's store (not on the market): the usual price in each currency.</summary>
    public static double KeyPrice(string currency) => currency switch { "EUR" => 2.35, "GBP" => 2.05, _ => 2.49 };

    private static readonly string[] WearOrder = ["Factory New", "Minimal Wear", "Field-Tested", "Well-Worn", "Battle-Scarred"];
    private static readonly SemaphoreSlim SteamGate = new(1, 1);
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LanSkinTester/2");
        return http;
    }

    private Dictionary<string, double> _prices = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>"AK-47 | Redline" -> its prices by what's in the brackets ("Field-Tested"...), for items without an exact match.</summary>
    private Dictionary<string, List<(string Variant, double Price)>> _variants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double?> _steam = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastNotify, _loadedAt;
    private string _currency = "";
    private Task? _loading;

    public bool Ready => _prices.Count > 0;

    /// <summary>"Name (Variant)" split in two; Variant is null when there are no brackets.</summary>
    private static (string Base, string? Variant) Split(string name)
    {
        var open = name.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && name.EndsWith(')') ? (name[..open], name[(open + 2)..^1]) : (name, null);
    }

    private void SetPrices(Dictionary<string, double> prices)
    {
        _variants = prices.Select(p => (Name: Split(p.Key), p.Value)).Where(p => p.Name.Variant != null)
            .GroupBy(p => p.Name.Base, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(p => (p.Name.Variant!, p.Value)).ToList(), StringComparer.OrdinalIgnoreCase);
        _prices = prices;
    }

    /// <summary>Tells the pages a price arrived (at most every few seconds).</summary>
    private void Notify()
    {
        if (DateTime.UtcNow - _lastNotify < TimeSpan.FromSeconds(3)) return;
        _lastNotify = DateTime.UtcNow;
        try { changed(); } catch (Exception) { /* page refresh only */ }
    }

    /// <summary>Loads the cached list, and downloads a new one in the background when it is old.</summary>
    public void Ensure(string currency)
    {
        if (!Currencies.Contains(currency)) currency = "USD";
        if (_currency != currency)
        {
            _currency = currency;
            SetPrices(ReadCache(currency, out _loadedAt));
            lock (_steam) _steam.Clear();
        }
        if (DateTime.UtcNow - _loadedAt > TimeSpan.FromHours(12) && _loading is null or { IsCompleted: true })
            _loading = DownloadAsync(currency);
    }

    private string CacheFile(string currency) => Path.Combine(dir, $"prices-{currency.ToLowerInvariant()}.json");

    private Dictionary<string, double> ReadCache(string currency, out DateTime when)
    {
        when = DateTime.MinValue;
        try
        {
            var file = CacheFile(currency);
            if (!File.Exists(file)) return new(StringComparer.OrdinalIgnoreCase);
            when = File.GetLastWriteTimeUtc(file);
            return new(JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(file)) ?? [], StringComparer.OrdinalIgnoreCase);
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
            request.Headers.TryAddWithoutValidation("User-Agent", "LanSkinTester/2 (+https://github.com/V3rkku1/Cs-plugin)");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            using var response = await Http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var prices = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("market_hash_name", out var name) || name.GetString() is not { } hash) continue;
                double? Read(string field) => item.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
                // Suggested is Skinport's market estimate; the cheapest listing when there is none.
                if ((Read("suggested_price") ?? Read("median_price") ?? Read("min_price")) is > 0 and var price) prices[hash] = Math.Round(price, 2);
            }
            if (prices.Count == 0) return;
            Directory.CreateDirectory(dir);
            File.WriteAllText(CacheFile(currency), JsonSerializer.Serialize(prices));
            if (_currency == currency)
            {
                SetPrices(prices);
                _loadedAt = DateTime.UtcNow;
                Notify();
            }
            log($"Prices updated: {prices.Count} items ({currency}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            log("Could not download prices (they show when online): " + ex.Message);
            _loadedAt = DateTime.UtcNow - TimeSpan.FromHours(11); // try again in an hour
        }
    }

    /// <summary>
    /// The price of a market name. When that exact item has no price (no listings in that wear, or a
    /// name variant like graffiti colours), the nearest wear or the cheapest variant is used; only when
    /// nothing at all is known is Steam asked (in the background; the page refreshes when it answers).
    /// </summary>
    public double? Price(string marketName)
    {
        if (_prices.TryGetValue(marketName, out var p)) return p;
        var (baseName, wanted) = Split(marketName);
        if (_variants.TryGetValue(baseName, out var variants) && variants.Count > 0)
        {
            var at = wanted == null ? -1 : Array.IndexOf(WearOrder, wanted);
            if (at >= 0)
            {
                // Nearest wear that has a price.
                var nearest = variants.Where(v => Array.IndexOf(WearOrder, v.Variant) >= 0)
                    .OrderBy(v => Math.Abs(Array.IndexOf(WearOrder, v.Variant) - at)).ThenBy(v => v.Price).FirstOrDefault();
                if (nearest.Variant != null) return nearest.Price;
            }
            return variants.Min(v => v.Price);
        }
        lock (_steam)
        {
            if (_steam.TryGetValue(marketName, out var known)) return known;
            _steam[marketName] = null; // asked once per run
        }
        _ = SteamPriceAsync(marketName);
        return null;
    }

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
            if (ParseSteamPrice(text) is { } price)
            {
                lock (_steam) _steam[marketName] = price;
                Notify();
            }
            await Task.Delay(3500);
        }
        catch (Exception) { /* unknown stays unknown */ }
        finally { SteamGate.Release(); }
    }

    /// <summary>"1.234,56€" or "$1,234.56": the last separator is the decimal point.</summary>
    internal static double? ParseSteamPrice(string? text)
    {
        var digits = new string((text ?? "").Where(c => char.IsDigit(c) || c is ',' or '.').ToArray()).Replace(',', '.');
        var last = digits.LastIndexOf('.');
        if (last > 0) digits = digits[..last].Replace(".", "") + digits[last..];
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var price) && price > 0 ? price : null;
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
        var star = name.StartsWith('★');
        var baseName = star ? name[1..].Trim() : name;
        if (!skin) return (statTrak && !star ? "StatTrak™ " : "") + baseName;
        var prefix = (star ? "★ " : "") + (statTrak ? "StatTrak™ " : "") + (souvenir && !star ? "Souvenir " : "");
        return prefix + baseName + (hasPaint ? $" ({WearName(wear)})" : "");
    }
}
