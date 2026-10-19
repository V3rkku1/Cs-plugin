using System.Net.Sockets;
using System.Text;

namespace LanSkinTester.Launcher.Core;

/// <summary>
/// Your skins on a friend's server: cfg\lst_myskins.cfg holds your loadout as css_myskins lines, and the
/// launcher types "exec lst_myskins" into CS2's console (-netconport) while you're on their server, so
/// their plugin gives you your own skins. Repeating it is harmless: the plugin ignores the same loadout.
/// If CS2 was started without the console port, typing exec lst_myskins in the console does the same.
/// </summary>
public static class ClientConsole
{
    public const int Port = 29015;

    /// <summary>
    /// -netconpassword: the console port shouldn't take commands from anyone else on the network. Kept in a file so a
    /// restarted launcher still matches the CS2 it started earlier.
    /// </summary>
    public static readonly string Password = LoadPassword();

    private static string LoadPassword()
    {
        var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LanSkinTester", "netcon.key");
        try
        {
            if (File.Exists(file) && File.ReadAllText(file).Trim() is { Length: >= 16 } saved) return saved;
            var fresh = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, fresh);
            return fresh;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
        }
    }
    public const string SkinsCfg = "lst_myskins";
    private static CancellationTokenSource? _sending;

    /// <summary>Writes your loadout into the cfg CS2 runs. False when there is nothing to send.</summary>
    public static bool WriteSkinsCfg(GameFolder game, PlayerLoadout loadout)
    {
        var chunks = LoadoutCodec.Encode(loadout);
        if (chunks.Count > LoadoutCodec.MaxChunks) return false;
        var lines = chunks.Select((c, i) => $"css_myskins {i + 1} {chunks.Count} \"{c}\"");
        var dir = Path.Combine(game.Csgo, "cfg");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, SkinsCfg + ".cfg"), string.Join("\n", lines) + "\n");
        return true;
    }

    /// <summary>Types the command into CS2's console. False when CS2 isn't listening (not started by the launcher).</summary>
    public static async Task<bool> SendAsync(string command)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await tcp.ConnectAsync("127.0.0.1", Port, timeout.Token);
            var bytes = Encoding.UTF8.GetBytes($"PASS {Password}\n{command}\n");
            await tcp.GetStream().WriteAsync(bytes, timeout.Token);
            return true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or IOException) { return false; }
    }

    /// <summary>
    /// After joining a friend: every 10 seconds while CS2 runs, refresh the cfg (what you equip in the
    /// launcher meanwhile goes along). It runs in CS2 for the first 2 minutes (while you connect and load)
    /// and after that only when the cfg changed. Stops when CS2 closes or you start your own server.
    /// </summary>
    public static void StartSendingSkins(Func<bool> stillJoined, Func<string?> refreshCfg, Action<string> log)
    {
        _sending?.Cancel();
        var cts = _sending = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cts.Token).ConfigureAwait(false); // CS2 starting and connecting
            var until = DateTime.UtcNow.AddMinutes(2);
            string? sent = null;
            var warned = false;
            while (!cts.IsCancellationRequested && stillJoined())
            {
                string? cfg = null;
                try { cfg = refreshCfg(); } catch (Exception) { /* next round */ }
                if (cfg != null && (DateTime.UtcNow < until || cfg != sent))
                {
                    if (await SendAsync("exec " + SkinsCfg)) sent = cfg;
                    else if (!warned)
                    {
                        warned = true;
                        log($"Couldn't reach CS2's console to send your skins. In CS2: console (~) → exec {SkinsCfg}");
                    }
                }
                await Task.Delay(TimeSpan.FromSeconds(10), cts.Token).ConfigureAwait(false);
            }
        }, cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    public static void StopSendingSkins() => _sending?.Cancel();
}
