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

    /// <summary>True when GitHub has a newer build. Fails quietly (offline, rate limit) as "no update".</summary>
    public static async Task<bool> NewerAvailableAsync(HttpClient http)
    {
        var local = LocalCommit;
        if (local == null) return false;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TagApi);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LanSkinTester-Launcher", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return false;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var remote = doc.RootElement.GetProperty("object").GetProperty("sha").GetString();
            return remote != null && !remote.Equals(local, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}
