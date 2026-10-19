using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>Setup › CS:GO Legacy: install SourceMod and the skin plugins, then host a LAN game.</summary>
public partial class Api
{
    public record LegacyState(string? Folder, bool Busy, bool Running, List<SetupStep> Steps,
        string Mode, string Map, int Bots, List<object> Modes, string PluginPage);

    public record LegacyStart(string Mode, string Map, int Bots);

    private static readonly (string Id, string Title, string Detail)[] LegacySteps =
    [
        ("metamod", "Metamod:Source 1.12", "Loads SourceMod into the game"),
        ("sourcemod", "SourceMod 1.12", "Runs the skin plugins"),
        ("ptah", "PTaH extension", "Needed by the skin plugins"),
        ("weapons", "Weapons skin plugin", "!ws in chat: skin, wear, pattern, StatTrak, name tag; !knife"),
        ("gloves", "Gloves plugin", "!gloves in chat"),
    ];

    private LegacyGame? Legacy
    {
        get
        {
            var s = App.Settings;
            if (!LegacyGame.IsValid(s.LegacyPath) && LegacyGame.Find(AppState.SteamPath()) is { } found)
            {
                s.LegacyPath = found;
                App.SaveSettings();
            }
            return LegacyGame.IsValid(s.LegacyPath) ? new LegacyGame(s.LegacyPath!) : null;
        }
    }

    [Call("legacy.state")]
    private LegacyState LegacyStateNow()
    {
        var g = Legacy;
        var steps = new List<SetupStep>();
        foreach (var (id, title, detail) in LegacySteps)
        {
            var installed = g != null && id switch
            {
                "metamod" => g.MetamodInstalled,
                "sourcemod" => g.SourceModInstalled,
                "ptah" => g.PtahInstalled,
                _ => g.PluginInstalled(id),
            };
            var (state, text) = _stepResults.TryGetValue("legacy." + id, out var r) ? r
                : g == null ? ("missing", "Pick your CS:GO folder first")
                : installed ? ("ok", "Installed") : ("missing", "Not installed - " + detail);
            steps.Add(new SetupStep("legacy." + id, title, state, text, installed ? "Update" : "Install", g != null && !_setupBusy));
        }
        if (g != null && g.SourceModInstalled && g.BlocksSkins)
            steps.Add(new SetupStep("legacy.smConfig", "Skins allowed in SourceMod", "warning",
                "FollowCSGOServerGuidelines is on - skins won't show. Press Fix.", "Fix", !_setupBusy));

        var s = App.Settings;
        return new LegacyState(g?.Root, _setupBusy, LegacyGame.Running(), steps, s.LegacyMode, s.LegacyMap, s.LegacyBots,
            [.. LegacyGame.Modes.Select(m => new { value = m.Id, label = m.Name })], Installer.SkinPluginPage);
    }

    [Call("legacy.find")]
    private void FindLegacy()
    {
        App.Settings.LegacyPath = LegacyGame.Find(AppState.SteamPath());
        App.SaveSettings();
        App.NotifyChanged();
    }

    [Call("legacy.browse")]
    private async Task BrowseLegacy()
    {
        var path = await OnUi(() =>
        {
            using var dialog = new FolderBrowserDialog { Description = "Pick the CS:GO folder (the one with csgo.exe)" };
            return dialog.ShowDialog(Window) == DialogResult.OK ? dialog.SelectedPath : null;
        });
        if (path == null) return;
        if (!LegacyGame.IsValid(path))
            throw new ApiException("That folder doesn't contain csgo.exe and csgo\\gameinfo.txt. Pick the CS:GO legacy folder.");
        App.Settings.LegacyPath = path;
        App.SaveSettings();
        App.NotifyChanged();
    }

    /// <summary>One step ("legacy.metamod", ...), or every step for "legacy.all".</summary>
    [Call("legacy.step")]
    private Task RunLegacyStep(string id)
    {
        var g = Legacy ?? throw new ApiException("Pick your CS:GO folder first.");
        if (LegacyGame.Running()) throw new ApiException("Close CS:GO first (files are in use while it runs).");
        if (id == "legacy.smConfig") return Task.Run(() => { g.AllowSkins(); App.NotifyChanged(); });

        if (!id.StartsWith("legacy.")) throw new ApiException("Unknown step");
        var ids = id == "legacy.all" ? LegacySteps.Select(x => x.Id).ToList() : [id["legacy.".Length..]];
        return RunSetup(async i =>
        {
            foreach (var step in ids)
            {
                i.Progress = p => Push("setup.progress", new { id = "legacy." + step, p });
                await Step("legacy." + step, () => i.InstallLegacyAsync(g, step));
            }
            if (id == "legacy.all") App.Log("CS:GO legacy is ready. Press Start, then type !ws in chat.");
        }, needsGame: false);
    }

    [Call("legacy.start")]
    private void StartLegacy(LegacyStart start)
    {
        var g = Legacy ?? throw new ApiException("Pick your CS:GO folder first.");
        if (LegacyGame.Running()) throw new ApiException("CS:GO is already running. Close it to start a new game.");
        if (!g.SourceModInstalled) throw new ApiException("Install SourceMod and the skin plugins first.");
        if (g.BlocksSkins) g.AllowSkins();
        g.RemoveUpdater();
        try { g.Start(start.Mode, start.Map.Trim(), start.Bots); }
        catch (ArgumentException ex) { throw new ApiException(ex.Message); }

        var s = App.Settings;
        (s.LegacyMode, s.LegacyMap, s.LegacyBots) = (start.Mode, start.Map.Trim(), start.Bots);
        App.SaveSettings();
        App.Log($"Started CS:GO legacy on {s.LegacyMap}. Skins: !ws, knives: !knife, gloves: !gloves.");
        App.NotifyChanged();
    }
}
