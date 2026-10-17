using System.Text;
using System.Text.RegularExpressions;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>
/// Live Server › Logs: the launcher's activity, the server console (csgo\console.log, -condebug) and
/// CounterStrikeSharp's newest log in one place, with the error lines picked out and a button that
/// copies all of it as one block to paste into a bug report or a chat.
/// </summary>
public partial class Api
{
    public record LogsState(List<string> Console, List<string> Plugin, string? ConsoleFile, string? PluginFile);

    private static readonly Regex ErrorLine = new(@"error|eror|\[err|\[wrn|fail|exception|crash|could not|couldn't|unable|missing|not found|denied|warning", RegexOptions.IgnoreCase);

    private string? PluginLogFile()
    {
        var dir = App.Game == null ? null : Path.Combine(App.Game.Addons, "counterstrikesharp", "logs");
        if (dir == null || !Directory.Exists(dir)) return null;
        return new DirectoryInfo(dir).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }

    /// <summary>The last lines of a file another process may still be writing.</summary>
    private static List<string> Tail(string? path, int lines)
    {
        if (path == null || !File.Exists(path)) return [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // shortcut: reads at most the last 512 KB, enough for a few thousand lines.
            if (stream.Length > 512 * 1024) stream.Seek(-512 * 1024, SeekOrigin.End);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim() != "").TakeLast(lines).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    private string? ConsoleLogFile => App.Game == null ? null : new TestSession(App.Game).ConsoleLog;

    [Call("live.logs")]
    private LogsState Logs() => new(Tail(ConsoleLogFile, 400), Tail(PluginLogFile(), 400), ConsoleLogFile, PluginLogFile());

    /// <summary>Opens a log in the PC's text editor: "console" or "plugin".</summary>
    [Call("live.openLog")]
    private void OpenLog(string which)
    {
        var path = which == "plugin" ? PluginLogFile() : ConsoleLogFile;
        if (path == null || !File.Exists(path)) throw new ApiException(which == "plugin" ? "No CounterStrikeSharp log yet." : "No console log yet: start the server once.");
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>Copies one report: what is set up, the error lines first, then the end of each log.</summary>
    [Call("live.copyLogs")]
    private async Task CopyLogs()
    {
        List<string> activity;
        lock (_log) activity = [.. _log];
        var console = Tail(ConsoleLogFile, 150);
        var plugin = Tail(PluginLogFile(), 80);
        var s = App.Settings.Server;
        var text = new StringBuilder()
            .AppendLine("=== LAN Skin Tester logs ===")
            .AppendLine($"Launcher {UpdateCheck.ShortVersion} · {Environment.OSVersion} · CS2 folder {(App.Game == null ? "not found" : "found")}" +
                $" · plugin {(App.Game?.PluginInstalled == true ? "installed" : "not installed")} · server {(App.ReadServerStatus() != null ? "running" : "not running")}")
            .AppendLine($"Mode {s.Mode} · map {s.Map}");
        var errors = activity.Concat(console).Concat(plugin).Where(l => ErrorLine.IsMatch(l)).Distinct().TakeLast(40).ToList();
        void Block(string title, IEnumerable<string> lines)
        {
            text.AppendLine().AppendLine($"--- {title} ---");
            foreach (var line in lines) text.AppendLine(line);
        }
        Block($"Errors and warnings ({errors.Count})", errors.Count > 0 ? errors : ["(none found)"]);
        Block("Launcher activity", activity.TakeLast(80));
        Block("Server console (console.log)", console.Count > 0 ? console : ["(no console.log yet - start the server once)"]);
        Block("CounterStrikeSharp log", plugin.Count > 0 ? plugin : ["(none)"]);
        var report = text.ToString();
        var ok = await OnUi(() =>
        {
            try { Clipboard.SetText(report); return true; }
            catch (Exception) { return false; }
        });
        Toast(ok ? "Logs copied. Paste them (Ctrl+V) where you ask for help." : "Could not use the clipboard.", ok ? "good" : "bad");
    }
}
