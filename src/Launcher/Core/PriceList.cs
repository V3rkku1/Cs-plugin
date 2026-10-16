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

    static PriceList() =>
        Http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) LanSkinTester/2");

    private readonly string _dir;
    private readonly Action<string> _log;
    private readonly Action _changed;
    private Dictionary<string, double> _prices = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>"AK-47 | Redline" -> its prices by what's in the brackets ("Field-Tested"...), for items without an exact match.</summary>
    private Dictionary<string, List<(string Variant, double Price)>> _variants = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastNotify;
    private readonly Dictionary<string, double?> _steam = new(StringComparer.OrdinalIgnoreCase);
    private string _currency = "";
    private DateTime _loadedAt;
    private Task? _loading;

    public PriceList(string dir, Action<string> log, Action changed)
    {
        _dir = dir;
        _log = log;
        _changed = changed;
    }

    private void SetPrices(Dictionary<string, double> prices)
    {
        var variants = new Dictionary<string, List<(string, double)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, price) in prices)
        {
            var open = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (open <= 0 || !name.EndsWith(')')) continue;
            var key = name[..open];
            if (!variants.TryGetValue(key, out var list)) variants[key] = list = new();
            list.Add((name[(open + 2)..^1], price));
        }
        _prices = prices;
        _variants = variants;
    }

    /// <summary>Tells the pages a price arrived (at most every few seconds).</summary>
    private void Notify()
    {
        if (DateTime.UtcNow - _lastNotify < TimeSpan.FromSeconds(3)) return;
        _lastNotify = DateTime.UtcNow;
        try { _changed(); } catch (Exception) { /* page refresh only */ }
    }

    public bool Ready => _prices.Count > 0;

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
                var price = Read("suggested_price") ?? Read("median_price") ?? Read("min_price");
                if (price is > 0) prices[hash] = Math.Round(price.Value, 2);
            }
            if (prices.Count == 0) return;
            Directory.CreateDirectory(_dir);
            File.WriteAllText(CacheFile(currency), JsonSerializer.Serialize(prices));
            if (_currency == currency)
            {
                SetPrices(prices);
                _loadedAt = DateTime.UtcNow;
                Notify();
            }
            _log($"Prices updated: {prices.Count} items ({currency}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            _log("Could not download prices (they show when online): " + ex.Message);
            _loadedAt = DateTime.UtcNow - TimeSpan.FromHours(11); // try again in an hour
        }
    }

    private static readonly string[] WearOrder = ["Factory New", "Minimal Wear", "Field-Tested", "Well-Worn", "Battle-Scarred"];

    /// <summary>
    /// The price of a market name. When that exact item has no price (no listings in that wear, or a
    /// name variant like graffiti colours), the nearest wear or the cheapest variant is used; only when
    /// nothing at all is known is Steam asked (in the background; the page refreshes when it answers).
    /// </summary>
    public double? Price(string marketName)
    {
        if (_prices.TryGetValue(marketName, out var p)) return p;
        var open = marketName.LastIndexOf(" (", StringComparison.Ordinal);
        var baseName = open > 0 && marketName.EndsWith(')') ? marketName[..open] : marketName;
        var wanted = open > 0 && marketName.EndsWith(')') ? marketName[(open + 2)..^1] : null;
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
            {
                lock (_steam) _steam[marketName] = price;
                Notify();
            }
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
