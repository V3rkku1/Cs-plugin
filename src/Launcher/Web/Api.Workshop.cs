using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// Workshop page: the maps Steam has downloaded for CS2, a search of the Steam Workshop, and one click to
/// host a map. Subscribing happens in Steam (the launcher opens the item's page there); Steam then
/// downloads it, and it shows under Downloaded.
/// </summary>
public partial class Api
{
    public record WorkshopItem(string Id, string Title, string Preview, string Size, string Updated, bool Downloaded, bool Cs2, string Tags);
    public record WorkshopState(bool HasGame, List<WorkshopItem> Downloaded, string? Hosting, string[] Modes, Dictionary<string, string> MapModes);

    /// <summary>Titles and pictures from Steam, kept for the launcher's run (they rarely change).</summary>
    private static readonly Dictionary<string, JsonElement> WorkshopDetails = new();

    [Call("workshop.state")]
    private async Task<WorkshopState> WorkshopStateNow()
    {
        var game = App.Game;
        if (game == null) return new WorkshopState(false, [], null, [], []);
        var downloaded = WorkshopMaps.AllDownloaded(game).OrderByDescending(d => d.Updated).ToList();
        await LoadWorkshopDetails(downloaded.Select(d => d.Id));
        var items = downloaded.Select(d => ToItem(d.Id, true, d.Bytes, d.Updated)).ToList();
        return new WorkshopState(true, items, App.TestModeOn ? App.Settings.Server.WorkshopMapId : null,
            ServerSettings.Modes.Select(m => m.Name).ToArray(), App.Settings.Server.MapModes);
    }

    public record WorkshopSearch(string? Text, string? Sort, int Page);

    /// <summary>Searches the Steam Workshop for CS2 maps (the community site, so no API key is needed).</summary>
    [Call("workshop.search")]
    private async Task<List<WorkshopItem>> SearchWorkshop(WorkshopSearch q)
    {
        var text = (q.Text ?? "").Trim();
        var sort = q.Sort switch { "new" => "mostrecent", "top" => "toprated", _ => text == "" ? "trend" : "textsearch" };
        var url = "https://steamcommunity.com/workshop/browse/?appid=730&section=readytouseitems&numperpage=30" +
                  $"&browsesort={sort}&actualsort={sort}&days=-1&p={Math.Clamp(q.Page, 1, 50)}" +
                  (text == "" ? "" : "&searchtext=" + Uri.EscapeDataString(text));
        string html;
        try { html = await App.Http.GetStringAsync(url); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ApiException("Couldn't reach the Steam Workshop: " + ex.Message);
        }
        var ids = Regex.Matches(html, @"sharedfiles/filedetails/\?id=(\d{6,})").Select(m => m.Groups[1].Value).Distinct().Take(30).ToList();
        await LoadWorkshopDetails(ids);
        var game = App.Game;
        return ids.Select(id => ToItem(id, game != null && WorkshopMaps.FindDownloaded(game, id) != null, 0, null))
            .Where(i => i.Title != "")
            .ToList();
    }

    /// <summary>Opens the item in Steam, where Subscribe downloads it.</summary>
    [Call("workshop.subscribe")]
    private void OpenInSteam(string id)
    {
        if (!Regex.IsMatch(id ?? "", @"^\d{6,}$")) throw new ApiException("Not a Workshop item.");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(WorkshopMaps.SubscribeUrl(id!)) { UseShellExecute = true });
            Toast("Opened in Steam: press Subscribe. It shows under Downloaded once Steam has it.");
        }
        catch (Exception ex)
        {
            throw new ApiException("Could not open Steam: " + ex.Message);
        }
    }

    /// <summary>Hosts the map: switches the running server to it, or starts the server on it.</summary>
    public record MapModeArgs(string Id, string Mode);

    [Call("workshop.setMode")]
    private void SetWorkshopMode(MapModeArgs a)
    {
        if (a.Mode != "" && ServerSettings.Modes.All(m => m.Name != a.Mode) || !Regex.IsMatch(a.Id ?? "", @"^\d{6,}$")) throw new ApiException("Unknown mode.");
        if (a.Mode == "") App.Settings.Server.MapModes.Remove(a.Id);
        else App.Settings.Server.MapModes[a.Id] = a.Mode;
        SavePlaySettings();
        App.NotifyChanged();
    }

    [Call("workshop.host")]
    private async Task HostWorkshopMap(string id)
    {
        if (!Regex.IsMatch(id ?? "", @"^\d{6,}$")) throw new ApiException("Not a Workshop item.");
        App.Settings.Server.WorkshopMap = id!;
        App.Settings.Server.RecallMapMode();
        SavePlaySettings();
        App.NotifyChanged();
        if (App.TestModeOn) await ApplyToServer(new AskArgs());
        else await StartServerAsync(new AskArgs());
    }

    private WorkshopItem ToItem(string id, bool downloaded, long bytes, DateTime? updated)
    {
        if (!WorkshopDetails.TryGetValue(id, out var d))
            return new WorkshopItem(id, downloaded ? $"Workshop map {id}" : "", "", FileSizeText(bytes), DateText(updated), downloaded, true, "");
        string Str(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        long Long(string name) => d.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64()
            : d.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var n) ? n : 0;
        var tags = d.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => x.TryGetProperty("tag", out var tag) ? tag.GetString() ?? "" : "").Where(x => x != "").ToList()
            : new List<string>();
        var cs2 = tags.Any(x => x.Contains("CS2", StringComparison.OrdinalIgnoreCase)) || Long("time_updated") > 1695686400; // after CS2's release
        var size = bytes > 0 ? bytes : Long("file_size");
        var when = updated ?? (Long("time_updated") > 0 ? DateTimeOffset.FromUnixTimeSeconds(Long("time_updated")).UtcDateTime : (DateTime?)null);
        return new WorkshopItem(id, WebUtility.HtmlDecode(Str("title")), Str("preview_url"), FileSizeText(size), DateText(when), downloaded, cs2,
            string.Join(" · ", tags.Where(x => !x.Equals("CS2", StringComparison.OrdinalIgnoreCase)).Take(3)));
    }

    private static string FileSizeText(long bytes) => bytes <= 0 ? "" : bytes >= 1L << 30
        ? (bytes / (double)(1L << 30)).ToString("0.0 GB", CultureInfo.InvariantCulture)
        : (bytes / (double)(1L << 20)).ToString("0 MB", CultureInfo.InvariantCulture);

    private static string DateText(DateTime? when) => when is { } w ? w.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "";

    /// <summary>Steam's public details call (no key needed): titles, pictures, sizes and tags.</summary>
    private async Task LoadWorkshopDetails(IEnumerable<string> ids)
    {
        var missing = ids.Where(id => !WorkshopDetails.ContainsKey(id)).Distinct().ToList();
        for (var start = 0; start < missing.Count; start += 50)
        {
            var batch = missing.Skip(start).Take(50).ToList();
            var form = new List<KeyValuePair<string, string>> { new("itemcount", batch.Count.ToString(CultureInfo.InvariantCulture)) };
            form.AddRange(batch.Select((id, i) => new KeyValuePair<string, string>($"publishedfileids[{i}]", id)));
            try
            {
                using var response = await App.Http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                    new FormUrlEncodedContent(form));
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!doc.RootElement.TryGetProperty("response", out var r) || !r.TryGetProperty("publishedfiledetails", out var list)) continue;
                foreach (var item in list.EnumerateArray())
                    if (item.TryGetProperty("publishedfileid", out var pid) && pid.GetString() is { } id)
                        WorkshopDetails[id] = item.Clone();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Offline: the list still works, with ids instead of titles.
            }
        }
    }
}
