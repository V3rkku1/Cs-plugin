using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Compares the commit this launcher was built from (build.sh passes it as SourceRevisionId) with the
/// commit of the "latest" release tag on GitHub.
/// </summary>
public static class UpdateCheck
{
    public const string ReleaseUrl = "https://github.com/V3rkku1/Cs-plugin/releases/tag/latest";
    private const string TagApi = "https://api.github.com/repos/V3rkku1/Cs-plugin/git/ref/tags/latest";
    private const string CompareApi = "https://api.github.com/repos/V3rkku1/Cs-plugin/compare";

    /// <summary>Full commit hash of this build, or null for local builds without one.</summary>
    public static string? LocalCommit
    {
        get
        {
            var version = typeof(UpdateCheck).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var plus = version?.IndexOf('+') ?? -1;
            var commit = plus >= 0 ? version![(plus + 1)..] : null;
            return commit is { Length: >= 7 } ? commit : null;
        }
    }

    public static string ShortVersion => LocalCommit is { } c ? $"build {c[..7]}" : "dev build";

    /// <summary>A newer build on GitHub: its commit and the changes since this one (newest first).</summary>
    public record Update(string Commit, List<string> Notes);

    /// <summary>The newer build on GitHub, or null. Fails quietly (offline, rate limit) as "no update".</summary>
    public static async Task<Update?> FindAsync(HttpClient http) => (await CheckAsync(http)).Update;

    /// <summary>The newer build (or null when this is the newest), or why GitHub couldn't be asked.</summary>
    public static async Task<(Update? Update, string? Error)> CheckAsync(HttpClient http)
    {
        var local = LocalCommit;
        if (local == null) return (null, "this is a dev build, so it can't tell which build is newer");
        try
        {
            using var response = await GitHubGetAsync(http, TagApi);
            if (!response.IsSuccessStatusCode)
            {
                var limited = response.Headers.TryGetValues("x-ratelimit-remaining", out var left) && left.FirstOrDefault() == "0";
                return (null, limited ? "GitHub's limit for this network is used up - try again in an hour"
                    : $"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var remote = doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
            if (remote == null || remote.Equals(local, StringComparison.OrdinalIgnoreCase)) return (null, null);
            return (new Update(remote, await NotesAsync(http, local, remote)), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (null, "no connection to GitHub");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>First lines of the commit messages between the two builds (the "what's new" list).</summary>
    private static async Task<List<string>> NotesAsync(HttpClient http, string local, string remote)
    {
        var notes = new List<string>();
        try
        {
            using var doc = await GetJsonAsync(http, $"{CompareApi}/{local}...{remote}");
            if (doc == null) return notes;
            foreach (var c in doc.RootElement.GetProperty("commits").EnumerateArray().Reverse())
            {
                var message = c.GetProperty("commit").GetProperty("message").GetString() ?? "";
                var line = message.Split('\n')[0].Trim();
                if (line.Length == 0 || line.StartsWith("Merge ", StringComparison.Ordinal)) continue;
                notes.Add(line);
                if (notes.Count == 15) break;
            }
        }
        catch (Exception)
        {
            // No notes is fine: the update itself still works.
        }
        return notes;
    }

    private static async Task<JsonDocument?> GetJsonAsync(HttpClient http, string url)
    {
        using var response = await GitHubGetAsync(http, url);
        return response.IsSuccessStatusCode ? JsonDocument.Parse(await response.Content.ReadAsStringAsync()) : null;
    }

    /// <summary>A GitHub API GET: it refuses requests without a User-Agent.</summary>
    internal static async Task<HttpResponseMessage> GitHubGetAsync(HttpClient http, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LanSkinTester-Launcher", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return await http.SendAsync(request);
    }
}
