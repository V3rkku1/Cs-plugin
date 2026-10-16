using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>
/// Play and Live Server pages: quick starts, start / apply / stop the local server, the server settings
/// and the live controls. Every way of starting the server (Start, F5, quick starts, Friends) goes
/// through <see cref="StartServerAsync"/>.
/// </summary>
public partial class Api
{
    // What the running server was started with (null when this launcher didn't start it).
    private string? _runningMode, _runningMap, _runningRetakeKey;
    private bool _runningOnline;      // started with sv_lan 0 (can download Workshop maps)
    private bool _runningForFriends;

    private bool _playBusy;           // opening the router port / downloading a retakes plugin
    private string? _startError;      // the last "could not start", shown on the hero until the next start
    private Process? _reportedServer;
    private string? _lastTick;

    // Workshop start: the server boots on a normal map, and once it's up (and logged on to Steam) it
    // is switched to the Workshop map. Asking for it on the command line happens before Steam is
    // ready, and the server then sits idle with no map. CS2 starts once the Workshop map is loaded.
    private string? _pendingWorkshopId, _bootstrapMap;
    private bool _startGameAfterWorkshop;
    private DateTime? _serverUpSince, _workshopSentAt;
    private bool WaitingForWorkshop => _pendingWorkshopId != null;

    partial void Init()
    {
        // The old pages polled every 2 seconds; one timer on the window thread does it for all of them
        // and only tells the page when something it shows has changed.
        var poll = new System.Windows.Forms.Timer { Interval = 2000 };
        poll.Tick += (_, _) => PollServer();
        poll.Start();
        RouterPort.Changed += App.NotifyChanged;
    }

    private List<string>? _knownPlayers;

    private void PollServer()
    {
        var status = App.ReadServerStatus();
        AnnouncePlayers(status?.Players);
        HandleWorkshopStartup(status);
        CheckServerCrash();
        _ = RenewRouterPortAsync();
        var g = App.Game;
        var id = App.Settings.Server.WorkshopMapId;
        var tick = JsonSerializer.Serialize(new
        {
            status?.Map, status?.GameType, status?.GameMode, status?.Humans, status?.Bots, status?.Players,
            status?.SkinsAllowed, status?.DisabledReason, on = App.TestModeOn, w = _pendingWorkshopId, s = _workshopSentAt,
            sub = g != null && id != null && WorkshopMaps.FindDownloaded(g, id) != null,
        });
        if (tick == _lastTick) return;
        _lastTick = tick;
        Push("play.tick", null);
    }

    /// <summary>A toast when a friend joins or leaves the running server (not for the first list after a start).</summary>
    private void AnnouncePlayers(List<string>? players)
    {
        if (players == null)
        {
            _knownPlayers = null;
            return;
        }
        if (_knownPlayers != null)
        {
            foreach (var name in players.Except(_knownPlayers)) Toast($"{name} joined the server", "good");
            foreach (var name in _knownPlayers.Except(players)) Toast($"{name} left the server");
        }
        _knownPlayers = [.. players];
    }

    private static async Task RenewRouterPortAsync()
    {
        try { await Upnp.RenewIfNeededAsync(); }
        catch (Exception) { /* try again on the next tick */ }
    }

    // ---------------------------------------------------------------------------------------------
    // State
    // ---------------------------------------------------------------------------------------------

    public record PresetTile(string Title, string Subtitle, bool Saved);
    public record ModeInfo(string Name, string[] Maps);
    public record LiveStatus(string Map, string Mode, int Humans, int Bots, List<string> Players, bool SkinsAllowed,
        string DisabledReason, string Version);
    public record InviteInfo(string? Address, string How, string Text);
    public record WorkshopInfo(string? Id, string Note, string Kind);
    public record GiveItem(string Classname, string Name);

    public record PlayState(bool HasGame, bool Ready, bool TestMode, bool Busy, string? StartError, ServerSettings Settings,
        LiveStatus? Status, string LiveTitle, string LiveDetail, string LiveKind, InviteInfo Invite, List<PresetTile> Presets,
        List<ModeInfo> Modes, WorkshopInfo Workshop, string HackerDetail, string[] Difficulties, string[] BotTeams,
        string[] BotWeapons, string[] RetakeStyles, List<GiveItem> Give, int Port);

    private static readonly List<GiveItem> GiveItems =
    [
        .. ItemData.DisplayNames.OrderBy(d => d.Value).Select(d => new GiveItem(ItemData.Weapons[d.Key], d.Value)),
        new GiveItem("weapon_knife", "Knife (equipped)"),
    ];

    [Call("play.state")]
    private PlayState PlayStateNow()
    {
        var s = App.Settings.Server;
        var status = App.ReadServerStatus();
        var live = status == null ? null : new LiveStatus(status.Map, ModeName(status, withFallback: true), status.Humans,
            status.Bots, status.Players, status.SkinsAllowed, status.DisabledReason, status.Version);

        string title, detail, kind;
        if (status == null)
        {
            title = !App.TestModeOn ? "Server not running" : WaitingForWorkshop ? "Starting the server for the Workshop map…" : "Server starting…";
            detail = !App.TestModeOn ? "Press Start or pick a quick start."
                : WaitingForWorkshop ? "Then it downloads the map. CS2 starts by itself when it's loaded."
                : "Loading the map can take a minute. Live controls unlock when it's up.";
            kind = App.TestModeOn ? "busy" : "off";
        }
        else
        {
            title = $"{status.Map} · {live!.Mode}";
            detail = status.SkinsAllowed
                ? $"{PluralOf(status.Humans, "player")} · {PluralOf(status.Bots, "bot")} · skins working · plugin {status.Version}"
                : status.DisabledReason;
            kind = status.SkinsAllowed ? "good" : "warning";
            if (WaitingForWorkshop)
            {
                detail = _workshopSentAt == null ? "Server is up - loading the Workshop map next…"
                    : "Downloading and loading the Workshop map… CS2 starts by itself when it's ready.";
                kind = "busy";
            }
        }

        var presets = ServerSettings.Presets.Select(p => new PresetTile(p.Title, p.Subtitle, false))
            .Concat((App.Settings.SavedSetups ??= []).Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Settings != null)
                .Select(x => new PresetTile(x.Name, x.Settings.Summary(), true)))
            .ToList();

        return new PlayState(App.Game != null, App.Ready, App.TestModeOn, _playBusy, _startError, s, live, title, detail, kind,
            InviteFor(s), presets, ServerSettings.Modes.Select(m => new ModeInfo(m.Name, m.Maps)).ToList(), WorkshopFor(s),
            s.HackerMode switch
            {
                1 => "On · everyone hacks",
                2 => "On · one random hacker per round",
                _ => "Off · aimbot, wallhack, radar and more",
            },
            ServerSettings.Difficulties, ServerSettings.BotTeams, ServerSettings.BotWeaponChoices, ServerSettings.RetakeStyles,
            GiveItems, ServerSettings.Port);
    }

    private static string PluralOf(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

    private static InviteInfo InviteFor(ServerSettings s)
    {
        var address = FriendsInvite.HostAddress(s);
        var how = s.Friends switch
        {
            ServerSettings.FriendsNetwork.Internet => "Internet",
            ServerSettings.FriendsNetwork.Vpn => "Radmin VPN",
            _ => "same Wi-Fi",
        };
        var text = address != null ? $"{address} · {how}"
            : s.Friends == ServerSettings.FriendsNetwork.Internet ? "Router address shows after Start"
            : $"No {how} address found · see the Friends page";
        return new InviteInfo(address, how, text);
    }

    private WorkshopInfo WorkshopFor(ServerSettings s)
    {
        var id = s.WorkshopMapId;
        if (id == null)
            return string.IsNullOrWhiteSpace(s.WorkshopMap)
                ? new WorkshopInfo(null, "Optional · overrides the map", "muted")
                : new WorkshopInfo(null, "Paste the map's ID or Workshop link", "warning");
        var subscribed = App.Game != null && WorkshopMaps.FindDownloaded(App.Game, id) != null;
        return subscribed
            ? new WorkshopInfo(id, "Subscribed · the server loads it from the Workshop", "good")
            : new WorkshopInfo(id, "The server downloads it (needs internet)", "muted");
    }

    /// <summary>Plugin retakes runs as competitive (0/1). Show Retakes while that style is the one we started.</summary>
    private string ModeName(ServerStatus status, bool withFallback)
    {
        var pluginSession = _runningRetakeKey is { } key && key is not ("valve" or "off")
            || (_runningRetakeKey == null && App.Game != null
                && Installer.ThirdPartyRetakesLoaded(App.Game) && App.Settings.Server.IsPluginRetakes);
        if (status.GameType == 0 && status.GameMode == 1 && pluginSession)
            return "Retakes";
        if (status.GameType == 0 && status.GameMode == 1 && App.Settings.Server.Mode == ServerSettings.WeaponsExpertMode)
            return ServerSettings.WeaponsExpertMode;
        var known = ServerSettings.Modes.FirstOrDefault(m => m.GameType == status.GameType && m.Mode == status.GameMode)?.Name;
        if (!string.IsNullOrEmpty(known)) return known;
        return withFallback ? $"mode {status.GameType}/{status.GameMode}" : "";
    }

    // ---------------------------------------------------------------------------------------------
    // Asking first: the page shows the question and calls again with confirmed = true. A pushed event
    // (not a return value) so F5 / Shift+F5 from any page get the question too.
    // ---------------------------------------------------------------------------------------------

    public record AskArgs(bool Confirmed = false, bool ForFriends = false);

    private void Ask(string title, string message, string ok, string call, object? args = null, bool danger = false) =>
        Push("play.ask", new { title, message, ok, call, args, danger });

    // ---------------------------------------------------------------------------------------------
    // Start / apply / stop
    // ---------------------------------------------------------------------------------------------

    /// <summary>F5: start, or apply the settings to the running server.</summary>
    [Call("play.startOrApply")]
    private Task StartOrApply()
    {
        if (App.TestModeOn) return ApplyToServer(new AskArgs());
        if (App.Ready) return StartServerAsync(new AskArgs());
        throw new ApiException("Install first (Setup page).");
    }

    [Call("play.start")]
    private Task StartCall(AskArgs? a) => StartServerAsync(a ?? new AskArgs());

    /// <summary>
    /// In "Internet" friends mode it first opens the port on the router (UPnP) and makes sure there's a
    /// password. Any friends mode also counts as hosting friends.
    /// </summary>
    private async Task StartServerAsync(AskArgs a)
    {
        if (App.Game == null) throw new ApiException("Pick your CS2 folder first (Setup page).");
        if (_playBusy) return;
        if (App.TestModeOn) throw new ApiException("The server is already running - press Apply Changes, or Stop first.");
        if (!App.Ready) throw new ApiException("Install first (Setup page).");
        var server = App.Settings.Server;
        var forFriends = a.ForFriends || server.Friends != ServerSettings.FriendsNetwork.Lan;

        _playBusy = true;
        App.NotifyChanged();
        try
        {
            if (server.Friends == ServerSettings.FriendsNetwork.Internet)
            {
                // Open to the whole internet: always use a password.
                if (string.IsNullOrWhiteSpace(server.Password))
                {
                    server.Password = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(3)).ToLowerInvariant();
                    SavePlaySettings();
                    App.Log($"Internet mode: set password \"{server.Password}\" (it's in the Friends page invite).");
                }
                if (!a.Confirmed && await RouterPort.OpenAsync(App.Log) is { } error)
                {
                    Ask("Couldn't open the port", error + $"\n\nStart the server anyway? Friends can only join if you forward UDP port {ServerSettings.Port} to this PC in your router yourself.",
                        "Start Anyway", "play.start", a with { Confirmed = true });
                    return;
                }
            }
            if (!await PrepareRetakesAsync(server)) return;
            StartTest(forFriends);
        }
        finally
        {
            _playBusy = false;
            App.NotifyChanged();
        }
    }

    private void StartTest(bool forFriends)
    {
        var game = App.Game!;
        var server = App.Settings.Server;
        _startError = null;
        try
        {
            App.AllowSkinsInCss();
            // A newer launcher carries a newer plugin: put it in before the server loads it.
            if (!Installer.PluginMatchesBundled(game))
            {
                new Installer(game, App.Http, App.Log).InstallPlugin();
                App.Log($"Updated the plugin to v{Installer.BundledPluginVersion()?.ToString(3) ?? "?"} (the one in this launcher).");
            }
            var gameStarted = new TestSession(game).Start(server, forFriends);
            ClearWorkshopWait();
            if (server.WorkshopNeedsDownload)
            {
                _pendingWorkshopId = server.WorkshopMapId;
                _bootstrapMap = server.Map;
                _startGameAfterWorkshop = server.AutoStartGame;
            }
            _runningMode = server.Mode;
            _runningMap = server.WorkshopMapId ?? server.Map;
            _runningOnline = server.PrivateServer;
            _runningForFriends = forFriends;
            _runningRetakeKey = server.PluginRetakeKey();
            var map = server.WorkshopMapId != null ? $"Workshop map {server.WorkshopMapId}" : server.Map;
            if (server.WorkshopNeedsDownload)
                App.Log("Workshop map: the server goes online (unlisted) and loads it from the Workshop - the first time takes " +
                        "longer while it downloads. Players' games fetch it automatically when they join.");
            var startedAs = server.IsPluginRetakes ? "Plugin retakes" : server.Mode;
            App.Log($"Started {startedAs} on {map} with {server.Bots} {ServerSettings.Difficulties[Math.Clamp(server.Difficulty, 0, 3)].ToLowerInvariant()} bots.");
            Toast($"Starting {startedAs} on {map}…");
            if (server.Friends != ServerSettings.FriendsNetwork.Lan)
                App.Log($"Friends mode: {(server.Friends == ServerSettings.FriendsNetwork.Vpn ? "Radmin / VPN" : "internet (UPnP)")} (sv_lan 0, unlisted, VAC off).");
            App.Log(!server.AutoStartGame ? "Server only - press \"Open CS2\" when you want to join."
                : gameStarted ? $"CS2 joins when the server has loaded. If it doesn't: console (~) → connect 127.0.0.1:{ServerSettings.Port}"
                : "CS2 starts by itself as soon as the server has downloaded and loaded the Workshop map.");
        }
        catch (Exception ex)
        {
            App.Log("Could not start: " + ex.Message);
            _startError = ex.Message;
            // Nothing is running: put gameinfo.gi back now instead of waiting for the auto-restore.
            if (App.TestModeOn && !TestSession.Cs2Running())
            {
                try { new TestSession(game).Stop(); }
                catch (Exception stop) { App.Log("Could not restore gameinfo.gi: " + stop.Message); }
            }
            throw new ApiException("Could not start - the details are on the Play page and in the log.");
        }
    }

    [Call("play.apply")]
    private Task ApplyCall(AskArgs? a) => ApplyToServer(a ?? new AskArgs());

    private async Task ApplyToServer(AskArgs a)
    {
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first.");
        if (!App.TestModeOn) throw new ApiException("The server isn't running - press Start.");
        var server = App.Settings.Server;
        var map = server.WorkshopMapId ?? server.Map;
        var reload = _runningMode == null || _runningMode != server.Mode || _runningMap != map;
        if (RetakesNeedRestart(game, server))
        {
            if (!a.Confirmed)
            {
                var why = Installer.RetakePluginsMismatch(game, server) || Installer.ExtraPluginsMismatch(game, server)
                    ? "A server plugin has to load or unload, so the server needs a restart."
                    : "These retakes settings are read when the plugin loads, so the server needs a restart.";
                Ask("Restart the server?", why, "Restart", "play.apply", new AskArgs(Confirmed: true));
                return;
            }
            App.Log("Restarting so the server plugins match these settings.");
            await RestartServerAsync();
            return;
        }
        _runningRetakeKey ??= server.PluginRetakeKey();

        try
        {
            var warning = server.ApplyToRunningServer(game, reload, _runningOnline, _runningForFriends);
            if (warning != null)
            {
                App.Log(warning);
                Toast("Workshop map not downloaded - see the activity log.", "warning");
                return;
            }
            _runningMode = server.Mode;
            _runningMap = map;
            _runningRetakeKey = server.PluginRetakeKey();
            App.Log(reload ? $"Switching to {server.Mode} on {map}..." : "Applied bots and options to the running server.");
            Toast(reload ? $"Switching to {server.Mode} on {map}…" : "Applied to the running server.", "good");
        }
        catch (IOException ex)
        {
            App.Log("Could not apply: " + ex.Message);
            throw new ApiException("Could not apply: " + ex.Message);
        }
    }

    /// <summary>Stops the running server, waits for CS2 to close, then starts again with the current settings.</summary>
    private async Task RestartServerAsync()
    {
        new TestSession(App.Game!).Stop();
        _runningMode = _runningMap = null;
        App.NotifyChanged();
        for (var i = 0; i < 20 && TestSession.Cs2Running(); i++) await Task.Delay(500);
        await StartServerAsync(new AskArgs(ForFriends: _runningForFriends));
    }

    [Call("play.openGame")]
    private void OpenGame()
    {
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first.");
        try
        {
            new TestSession(game).StartGame(App.Settings.Server);
            App.Log("Starting CS2 (VAC off)...");
            Toast("Starting CS2…");
        }
        catch (Exception ex)
        {
            App.Log("Could not start CS2: " + ex.Message);
            throw new ApiException("Could not start CS2: " + ex.Message);
        }
    }

    [Call("play.stop")]
    private void StopTest(AskArgs? a)
    {
        var game = App.Game;
        if (game == null || !App.TestModeOn) return;
        if (TestSession.Cs2Running() && a?.Confirmed != true)
        {
            Ask("Stop the skin test?", "This closes CS2 and the test server and puts CS2 back to normal.", "Stop", "play.stop",
                new AskArgs(Confirmed: true), danger: true);
            return;
        }
        try
        {
            new TestSession(game).Stop();
            _runningMode = _runningMap = _runningRetakeKey = null;
            _startError = null;
            ClearWorkshopWait();
            File.Delete(Path.Combine(game.PluginDir, ServerStatus.FileName));
            App.Log("Stopped. CS2 is back to normal - you can play online again.");
            Toast("Stopped. CS2 is back to normal.", "good");
        }
        catch (Exception ex)
        {
            App.Log("Could not restore gameinfo.gi: " + ex.Message);
            App.Log("   -> Steam > CS2 > Properties > Installed Files > Verify integrity also restores it.");
            Toast("Could not restore gameinfo.gi - see the log.", "bad");
        }
        App.NotifyChanged();
    }

    /// <summary>CS2 and the server were closed without Stop: restore quietly.</summary>
    private void AutoRestore()
    {
        if (App.Game == null) return;
        new TestSession(App.Game).Stop();
        _runningMode = _runningMap = _runningRetakeKey = null;
        ClearWorkshopWait();
        try { File.Delete(Path.Combine(App.Game.PluginDir, ServerStatus.FileName)); }
        catch (IOException) { /* stale file is ignored anyway */ }
        App.Log("CS2 was closed - restored gameinfo.gi automatically. You can play online again.");
        App.NotifyChanged();
    }

    /// <summary>Called when the window closes while test mode is on.</summary>
    private void StopFromClose()
    {
        if (App.Game == null) return;
        new TestSession(App.Game).Stop();
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Retakes plugins
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Downloads B3none's cs2-retakes the first time Plugin style is used, then loads or parks it.
    /// The plugin reads its JSON when it loads, so the file is written before the server starts.
    /// </summary>
    private async Task<bool> PrepareRetakesAsync(ServerSettings server)
    {
        var game = App.Game!;
        if (server.IsPluginRetakes)
        {
            await DownloadRetakePlugin(game, "the retakes plugin", () => Installer.RetakesInstalled(game),
                i => i.InstallRetakesAsync(), "Check your internet connection, or switch Style to Valve.");
            if (server.RetakeInstadefuse)
                await DownloadRetakePlugin(game, "instadefuse", () => Installer.InstadefuseInstalled(game),
                    i => i.InstallInstadefuseAsync(), "Check your internet connection, or turn Instadefuse off.");
            if (server.UsesAllocator)
                await DownloadRetakePlugin(game, "the retakes allocator", () => Installer.AllocatorInstalled(game),
                    i => i.InstallAllocatorAsync(), "Check your internet connection, or switch Guns to Buy.");
        }

        if (server.BotHider)
            await DownloadRetakePlugin(game, "Bot Hider", () => Installer.BotHiderInstalled(game),
                i => i.InstallBotHiderAsync(), "Check your internet connection, or turn Bot Hider off (Live Server › Server Plugins).");
        if (server.UsesDeathmatchPlugin)
            await DownloadRetakePlugin(game, "the Deathmatch plugin", () => Installer.DeathmatchInstalled(game),
                i => i.InstallDeathmatchAsync(), "Check your internet connection, or turn the Deathmatch plugin off.");

        // A running CS2 refuses to start anyway. Leave its plugin folder alone until it is closed.
        if (TestSession.Cs2Running()) return true;
        try
        {
            Installer.SetExtraPlugins(game, server);
            Installer.SetRetakesSuite(game, server);
            if (server.IsPluginRetakes) server.WriteRetakesConfig(game);
        }
        catch (Exception ex)
        {
            App.Log("Could not set up the retakes plugin: " + ex.Message);
            throw new ApiException("Could not set up the retakes plugin: " + ex.Message);
        }
        return true;
    }

    /// <summary>Downloads one retakes add-on the first time it is needed (throws with a hint when it can't).</summary>
    private async Task DownloadRetakePlugin(GameFolder game, string label, Func<bool> installed, Func<Installer, Task> download, string hint)
    {
        if (installed()) return;
        if (TestSession.Cs2Running())
            throw new ApiException($"Close CS2 first, then start again. {label} installs into the game folder.");
        try
        {
            await download(new Installer(game, App.Http, App.Log));
        }
        catch (Exception ex)
        {
            App.Log($"Could not download {label}: " + ex.Message);
            throw new ApiException($"{label} could not be downloaded: {ex.Message}. {hint}");
        }
    }

    /// <summary>
    /// True when the running server must restart. Loading or unloading the plugin, and the plugin's
    /// JSON settings, only take effect on the next start. Buy time and the bomb timer do not.
    /// A launcher that just opened onto an already-running server does not restart on its own.
    /// </summary>
    private bool RetakesNeedRestart(GameFolder game, ServerSettings server)
    {
        var mismatch = Installer.RetakePluginsMismatch(game, server) || Installer.ExtraPluginsMismatch(game, server);
        var key = server.PluginRetakeKey();
        if (_runningRetakeKey == null && !mismatch) return false;
        var pluginSettingsChanged = _runningRetakeKey != null && _runningRetakeKey != key
            && (server.IsPluginRetakes || _runningRetakeKey is not ("valve" or "off"));
        return mismatch || pluginSettingsChanged;
    }

    // ---------------------------------------------------------------------------------------------
    // Workshop start and crash report (from the poll)
    // ---------------------------------------------------------------------------------------------

    private void ClearWorkshopWait()
    {
        _pendingWorkshopId = _bootstrapMap = null;
        _serverUpSince = _workshopSentAt = null;
    }

    private void HandleWorkshopStartup(ServerStatus? status)
    {
        if (_pendingWorkshopId is not { } id || App.Game == null) return;
        if (!App.TestModeOn)
        {
            ClearWorkshopWait(); // stopped meanwhile
            return;
        }

        var now = DateTime.UtcNow;
        if (_workshopSentAt == null)
        {
            if (status == null)
            {
                _serverUpSince = null;
                return;
            }
            // Give it a few seconds on the first map to finish logging on to Steam.
            _serverUpSince ??= now;
            if (now - _serverUpSince < TimeSpan.FromSeconds(6)) return;
            try
            {
                ServerSettings.SendCommands(App.Game, $"host_workshop_map {id}");
                _workshopSentAt = now;
                App.Log($"Server is up - loading Workshop map {id} (it downloads first, this can take a while)...");
            }
            catch (IOException ex)
            {
                App.Log("Could not send the Workshop map command: " + ex.Message);
            }
            return;
        }

        if (status != null && !string.Equals(status.Map, _bootstrapMap, StringComparison.OrdinalIgnoreCase))
        {
            App.Log($"Workshop map loaded: {status.Map}.");
            Toast($"{status.Map} loaded" + (_startGameAfterWorkshop ? " - starting CS2…" : "."));
            if (_startGameAfterWorkshop)
            {
                try { new TestSession(App.Game).StartGame(App.Settings.Server); }
                catch (Exception ex) { App.Log("Could not start CS2: " + ex.Message); }
            }
            ClearWorkshopWait();
        }
        else if (now - _workshopSentAt > TimeSpan.FromMinutes(15))
        {
            ClearWorkshopWait();
            App.Log("The Workshop map still hasn't loaded after 15 minutes. Check the server window (is it downloading?), " +
                    "or press \"Open CS2\" to join the server on its first map.");
        }
    }

    /// <summary>The server we started is gone without Stop: say so and show its last console lines.</summary>
    private void CheckServerCrash()
    {
        var server = TestSession.Server;
        if (App.Game == null || server == null || TestSession.StoppedOnPurpose || server == _reportedServer) return;
        bool exited;
        try { exited = server.HasExited; }
        catch (Exception) { return; }
        if (!exited) return;

        _reportedServer = server;
        int? code = null;
        try { code = server.ExitCode; } catch (Exception) { /* unknown */ }
        App.Log($"The server closed unexpectedly{(code is { } c && c != 0 ? $" (exit code 0x{c:X8})" : "")}. Its last console lines:");
        var tail = new TestSession(App.Game).ConsoleTail();
        if (tail.Length == 0) App.Log("   (no console log - it closed before writing one)");
        foreach (var line in tail) App.Log("   " + line);
        _startError = "The server closed unexpectedly - its last console lines are in the activity log.";
        Toast("The server closed unexpectedly - see the activity log on Live Server.", "bad");
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Quick starts and saved setups
    // ---------------------------------------------------------------------------------------------

    public record PresetArgs(string Title, bool Saved);

    [Call("play.preset")]
    private async Task RunPreset(PresetArgs a)
    {
        var s = App.Settings.Server;
        if (a.Saved)
        {
            var setup = App.Settings.SavedSetups.FirstOrDefault(x => x.Name.Equals(a.Title, StringComparison.OrdinalIgnoreCase))
                ?? throw new ApiException("That saved setup is gone.");
            s.CopyPracticeFrom(setup.Settings);
        }
        else
        {
            var preset = ServerSettings.Presets.FirstOrDefault(p => p.Title == a.Title) ?? throw new ApiException("Unknown quick start.");
            if (preset.Title.StartsWith(ServerSettings.RushMode) && ServerSettings.Modes.All(m => m.Name != ServerSettings.RushMode))
                throw new ApiException("Rush wasn't found in your CS2 files. Update CS2 in Steam (Rush came with the Rush Hour update), then restart the launcher.");
            preset.Apply(s);
        }
        SavePlaySettings();
        App.Log($"Quick start: {a.Title}");
        App.NotifyChanged();

        if (App.TestModeOn) await ApplyToServer(new AskArgs());
        else if (App.Ready) await StartServerAsync(new AskArgs());
        else App.Log("Install first (Setup page), then pick the quick start again.");
    }

    public record SaveSetupArgs(string Name, bool Confirmed = false);

    [Call("play.saveSetup")]
    private void SaveSetup(SaveSetupArgs a)
    {
        var name = (a.Name ?? "").Trim();
        if (name == "") return;
        if (name.Length > 24) name = name[..24];
        if (ServerSettings.Presets.Any(p => p.Title.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ApiException("That name is already a built-in quick start.");

        var list = App.Settings.SavedSetups ??= [];
        var existing = list.FindIndex(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0 && !a.Confirmed)
        {
            Ask("Replace setup?", $"Replace the saved setup \"{name}\" with the current settings?", "Replace", "play.saveSetup",
                new SaveSetupArgs(name, true));
            return;
        }
        var setup = new SavedSetup { Name = name, Settings = App.Settings.Server.Clone() };
        if (existing >= 0) list[existing] = setup;
        else list.Add(setup);
        SavePlaySettings();
        App.Log($"Saved setup \"{name}\".");
        Toast($"Saved setup \"{name}\".", "good");
        App.NotifyChanged();
    }

    /// <summary>The page asks "are you sure" first.</summary>
    [Call("play.deleteSetup")]
    private void DeleteSetup(string name)
    {
        (App.Settings.SavedSetups ??= []).RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        SavePlaySettings();
        Toast($"Deleted \"{name}\".");
        App.NotifyChanged();
    }

    // ---------------------------------------------------------------------------------------------
    // Live Server: settings
    // ---------------------------------------------------------------------------------------------

    private static readonly Dictionary<string, PropertyInfo> SettingProps = typeof(ServerSettings)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<System.Text.Json.Serialization.JsonIgnoreAttribute>() == null)
        .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Changes some server settings: { "bots": 8, "mode": "Casual" }. Apply sends them to a running server.</summary>
    [Call("live.set")]
    private void SetServer(Dictionary<string, JsonElement> changes)
    {
        var s = App.Settings.Server;
        var oldMode = s.Mode;
        foreach (var (key, value) in changes)
        {
            if (!SettingProps.TryGetValue(key, out var prop)) throw new ApiException($"Unknown setting: {key}");
            object? v;
            try { v = value.Deserialize(prop.PropertyType, WebHost.Json); }
            catch (JsonException) { throw new ApiException($"Bad value for {key}"); }
            prop.SetValue(s, v ?? (prop.PropertyType == typeof(string) ? "" : null));
        }

        if (ServerSettings.Modes.All(m => m.Name != s.Mode)) s.Mode = oldMode;
        // A new mode keeps the map only when that mode has it.
        if (s.Mode != oldMode && !changes.ContainsKey("map") && s.GetMode().Maps is { Length: > 0 } maps && !maps.Contains(s.Map))
            s.Map = maps[0];
        s.Map = string.IsNullOrWhiteSpace(s.Map) ? s.GetMode().Maps[0] : s.Map.Trim();
        s.Bots = Math.Clamp(s.Bots, 0, 20);
        s.Difficulty = Math.Clamp(s.Difficulty, 0, 3);
        s.BotTeam = Math.Clamp(s.BotTeam, 0, 2);
        s.BotWeapons = Math.Clamp(s.BotWeapons, 0, ServerSettings.BotWeaponChoices.Length - 1);
        s.RoundTime = Math.Clamp(s.RoundTime, 0, 60);
        s.RetakeStyle = s.RetakeStyle == 1 ? 1 : 0;
        s.RetakeMaxPlayers = Math.Clamp(s.RetakeMaxPlayers, 2, 10);
        s.RetakeTerroristPercent = Math.Clamp(s.RetakeTerroristPercent, 10, 80);
        s.RetakeScrambleRounds = Math.Clamp(s.RetakeScrambleRounds, 1, 30);
        s.RetakeWeapons = s.RetakeWeapons == 1 ? 1 : 0;
        s.RetakeBuy = s.RetakeWeapons != 1;
        s.RetakeBuySeconds = Math.Clamp(s.RetakeBuySeconds, 4, 20);
        s.RetakeBombTimer = Math.Clamp(s.RetakeBombTimer, 15, 60);
        s.WorkshopMap = (s.WorkshopMap ?? "").Trim();
        s.GameArgs = (s.GameArgs ?? "").Trim();
        s.Password = (s.Password ?? "").Replace("\"", "").Trim();
        SavePlaySettings();
        App.NotifyChanged();
    }

    /// <summary>The page asks first. Keeps friends, password, shared skins, launch options and the aim key.</summary>
    [Call("live.reset")]
    private void ResetServer()
    {
        var old = App.Settings.Server;
        App.Settings.Server = new ServerSettings
        {
            Friends = old.Friends, Password = old.Password, SharedSkins = old.SharedSkins,
            GameArgs = old.GameArgs, AutoStartGame = old.AutoStartGame, AimBindKey = old.AimBindKey,
        };
        SavePlaySettings();
        Toast("Settings reset.");
        App.NotifyChanged();
    }

    [Call("live.subscribe")]
    private void SubscribeWorkshop()
    {
        var id = App.Settings.Server.WorkshopMapId ?? throw new ApiException("Paste the map's ID or Workshop link first.");
        try
        {
            Process.Start(new ProcessStartInfo(WorkshopMaps.SubscribeUrl(id)) { UseShellExecute = true });
            App.Log("Opened the map in Steam: press Subscribe there, wait for the download, then Start (or Apply).");
        }
        catch (Exception ex)
        {
            App.Log("Could not open Steam: " + ex.Message);
            throw new ApiException("Could not open Steam: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Live Server: commands
    // ---------------------------------------------------------------------------------------------

    /// <summary>Any server console command (first line only).</summary>
    [Call("live.command")]
    private void SendCommand(string command)
    {
        command = (command ?? "").Replace("\r", "").Split('\n')[0].Trim();
        if (command.Length == 0) return;
        var game = App.Game ?? throw new ApiException("Pick your CS2 folder first.");
        if (App.ReadServerStatus() == null) throw new ApiException("The server isn't running yet.");
        try
        {
            ServerSettings.SendCommands(game, command);
            App.Log($"> {command}");
            Toast("Sent to the server.");
        }
        catch (IOException ex)
        {
            App.Log("Could not send: " + ex.Message);
            throw new ApiException("Could not send: " + ex.Message);
        }
    }

    [Call("live.give")]
    private void GiveWeapon(string classname)
    {
        if (GiveItems.All(i => i.Classname != classname)) throw new ApiException("Unknown weapon.");
        SendCommand($"lst_give {classname}");
    }

    /// <summary>Toggles the damage report and tells the running server.</summary>
    [Call("live.damage")]
    private void ToggleDamage()
    {
        var s = App.Settings.Server;
        s.ShowDamage = !s.ShowDamage;
        SavePlaySettings();
        if (App.Game != null)
        {
            s.WriteCfg(App.Game);
            SendCommand($"lst_show_damage {(s.ShowDamage ? 1 : 0)}");
        }
        App.NotifyChanged();
    }

    private void SavePlaySettings()
    {
        try { App.Settings.Save(); }
        catch (IOException ex) { App.Log("Could not save settings: " + ex.Message); }
    }
}
