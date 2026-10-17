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
        // A fresh name each time: an old download may still be open (virus scan) and can't be overwritten.
        foreach (var old in Directory.GetFiles(dir, "LanSkinTesterLauncher-*.exe"))
        {
            try { File.Delete(old); } catch (Exception) { /* still in use: removed next time */ }
        }
        var target = Path.Combine(dir, $"LanSkinTesterLauncher-{DateTime.UtcNow:yyyyMMddHHmmss}.exe");

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

    /// <summary>Written by the swap script: "ok" or the reason it failed. Read by the next launcher start.</summary>
    public static string ResultFile => Path.Combine(Path.GetTempPath(), "LanSkinTester-update", "result.txt");

    /// <summary>
    /// Starts a hidden PowerShell script that waits for this process to exit, replaces the exe and
    /// starts the new one (or the old one again if the swap keeps failing). The caller then exits.
    /// PowerShell gets the script as UTF-16 (-EncodedCommand), so folders with letters like ä, ö or
    /// spaces work; a .cmd file would read them in the console code page and break.
    /// </summary>
    public static void ReplaceAfterExit(string newExe)
    {
        static string Q(string text) => "'" + text.Replace("'", "''") + "'";
        var current = CurrentExe;
        var script = $$"""
            $ErrorActionPreference = 'SilentlyContinue'
            $new = {{Q(newExe)}}; $cur = {{Q(current)}}; $result = {{Q(ResultFile)}}
            Wait-Process -Id {{Environment.ProcessId}} -Timeout 60
            $ok = $false; $why = ''
            for ($i = 0; $i -lt 40 -and -not $ok; $i++) {
                try { Copy-Item -LiteralPath $new -Destination $cur -Force -ErrorAction Stop; $ok = $true }
                catch { $why = $_.Exception.Message; Start-Sleep -Milliseconds 500 }
            }
            if ($ok) { Set-Content -LiteralPath $result -Value 'ok' -Encoding UTF8; Remove-Item -LiteralPath $new }
            else { Set-Content -LiteralPath $result -Value ('failed: ' + $why) -Encoding UTF8 }
            Start-Process -FilePath $cur
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        try { File.Delete(ResultFile); } catch (Exception) { /* not there */ }
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    /// <summary>The last swap's outcome ("ok" / "failed: ..."), once; null when there was none.</summary>
    public static string? TakeResult()
    {
        try
        {
            if (!File.Exists(ResultFile)) return null;
            var text = File.ReadAllText(ResultFile).Trim();
            File.Delete(ResultFile);
            return text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
