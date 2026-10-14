using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Self-update: downloads the newest LanSkinTesterLauncher.exe from the "latest" GitHub release and
/// swaps it in after this launcher exits (a small hidden script waits for the exit, replaces the exe
/// and starts it again). Works for the installed launcher (per-user folder) and the portable exe.
/// </summary>
public static class Updater
{
    private const string ReleaseApi = "https://api.github.com/repos/V3rkku1/Cs-plugin/releases/tags/latest";
    private const string AssetName = "LanSkinTesterLauncher.exe";

    public static string CurrentExe => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>Can this launcher replace its own exe (not under a read-only Program Files folder)?</summary>
    public static bool CanSelfUpdate()
    {
        try
        {
            var dir = Path.GetDirectoryName(CurrentExe)!;
            var probe = Path.Combine(dir, ".lst-write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Downloads the new exe and returns its path. Throws with a readable message on failure.</summary>
    public static async Task<string> DownloadAsync(HttpClient http, Action<double>? progress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LanSkinTester-Launcher", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = doc.RootElement.GetProperty("assets").EnumerateArray()
            .Where(a => a.GetProperty("name").GetString() == AssetName)
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault() ?? throw new InvalidOperationException("The latest release has no " + AssetName + ".");

        var dir = Path.Combine(Path.GetTempPath(), "LanSkinTester-update");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, AssetName);

        using (var download = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            download.EnsureSuccessStatusCode();
            var total = download.Content.Headers.ContentLength ?? -1;
            await using var input = await download.Content.ReadAsStreamAsync();
            await using var output = File.Create(target);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read));
                done += read;
                if (total > 0) progress?.Invoke((double)done / total);
            }
        }

        // Sanity check: a real Windows exe of a plausible size.
        var info = new FileInfo(target);
        using (var check = File.OpenRead(target))
        {
            if (info.Length < 1_000_000 || check.ReadByte() != 'M' || check.ReadByte() != 'Z')
                throw new InvalidDataException("The downloaded file isn't a valid launcher.");
        }
        return target;
    }

    /// <summary>
    /// Starts a hidden script that waits for this process to exit, replaces the exe and starts the new
    /// one. The caller then closes the launcher.
    /// </summary>
    public static void ReplaceAfterExit(string newExe)
    {
        var current = CurrentExe;
        var pid = Environment.ProcessId;
        var script = Path.Combine(Path.GetDirectoryName(newExe)!, "update.cmd");
        var text = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine(":wait")
            .AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (ping -n 2 127.0.0.1 >nul & goto wait)")
            .AppendLine("set tries=0")
            .AppendLine(":move")
            .AppendLine($"move /y \"{newExe}\" \"{current}\" >nul 2>&1")
            .AppendLine("if not errorlevel 1 goto start")
            .AppendLine("set /a tries+=1")
            .AppendLine("if %tries% geq 10 goto start") // give up: start the old one
            .AppendLine("ping -n 2 127.0.0.1 >nul")
            .AppendLine("goto move")
            .AppendLine(":start")
            .AppendLine($"start \"\" \"{current}\"")
            .AppendLine("del \"%~f0\"")
            .ToString();
        File.WriteAllText(script, text);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
