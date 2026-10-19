using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Host;

/// <summary>The local loadout (loadouts.json): read it, and keep it to what the inventory owns.</summary>
public partial class Api
{
    private PlayerLoadout ReadLoadout()
    {
        var store = new LoadoutStore(LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        // Without the admin code, drop anything not in the inventory (e.g. left over from the old launcher).
        if (UnboxOnly && PruneUnowned(loadout)) store.Save();
        return loadout;
    }

    private bool PruneUnowned(PlayerLoadout l)
    {
        var skins = OwnedSkins();
        // Applying a sticker uses it up, so the ones stuck on unboxed guns count as owned too.
        var stickers = OwnedIds("sticker").Concat(Inventory().SelectMany(i => i.Stickers ?? []).Select(st => st.Id)).ToHashSet();
        var charms = OwnedIds("charm");
        var pins = OwnedIds("pin");
        var kits = OwnedIds("music");
        var changed = false;
        foreach (var side in new[] { l.SkinsT, l.SkinsCT })
            foreach (var (def, s) in side.ToList())
                if (s.Paint != 0 && !skins.Contains((def, s.Paint))) { side.Remove(def); changed = true; }
        foreach (var (def, s) in l.Skins.ToList())
        {
            // Vanilla guns (paint 0) are always fine; knives and gloves must be owned either way.
            if ((s.Paint != 0 || IsKnifeType(def) || IsGloveType(def)) && !skins.Contains((def, s.Paint))) { l.Skins.Remove(def); changed = true; continue; }
            changed |= s.Stickers?.RemoveAll(x => !stickers.Contains(x.Id)) > 0;
            if (s.Keychain is { } k && !charms.Contains(k.Id)) { s.Keychain = null; changed = true; }
        }
        if (l.Knife != 0 && !skins.Any(o => o.Def == l.Knife)) { l.Knife = 0; changed = true; }
        if (l.Gloves != 0 && !skins.Any(o => o.Def == l.Gloves)) { l.Gloves = 0; changed = true; }
        foreach (var (get, set) in new (Func<ushort>, Action)[] { (() => l.KnifeT, () => l.KnifeT = 0), (() => l.KnifeCT, () => l.KnifeCT = 0),
                     (() => l.GlovesT, () => l.GlovesT = 0), (() => l.GlovesCT, () => l.GlovesCT = 0) })
            if (get() != 0 && !skins.Any(o => o.Def == get())) { set(); changed = true; }
        if (l.Pin != 0 && !pins.Contains(l.Pin)) { l.Pin = 0; changed = true; }
        if (l.MusicKit != 0 && !kits.Contains(l.MusicKit)) { l.MusicKit = 0; changed = true; }
        if (l.AgentT != null || l.AgentCT != null) { l.AgentT = l.AgentCT = null; changed = true; } // agents need the admin code
        return changed;
    }

    /// <summary>CS:GO edition: the launcher keeps the loadout itself and copies it into kgns' plugins (<see cref="SyncKgns"/>).</summary>
    private static string LoadoutFile => Path.Combine(AppState.LauncherDataDir, "loadout.json");

    private string? _kgnsSynced;

    /// <summary>
    /// After every inventory / loadout call: when the loadout changed, write it into kgns' tables and reload their
    /// plugins in a running CS:GO (they read the tables when a player joins or the plugin loads).
    /// </summary>
    private void SyncKgns()
    {
        if (Legacy is not { } g) return;
        string text;
        try { text = File.Exists(LoadoutFile) ? File.ReadAllText(LoadoutFile) : ""; }
        catch (IOException) { return; }
        if (text == _kgnsSynced) return;
        var account = ServerSettings.ActiveSteamAccount();
        if (account == 0) return; // Steam not running: next time
        try
        {
            var (weapons, gloves) = KgnsSync.Write(KgnsSync.DatabaseFile(g), KgnsSync.Steam2(account), new LoadoutStore(LoadoutFile).Get(LoadoutStore.LocalKey));
            if (!weapons && !gloves) return; // the plugins haven't made their tables yet: after the first start
            _kgnsSynced = text;
            if (LegacyGame.Running()) _ = ClientConsole.SendAsync("sm plugins reload weapons; sm plugins reload gloves");
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or UnauthorizedAccessException)
        {
            App.Log("Could not put your skins into CS:GO yet: " + ex.Message);
        }
    }

    private static bool IsKnifeType(ushort def) => ItemData.Knives.ContainsValue(def);
    private static bool IsGloveType(ushort def) => ItemData.Gloves.ContainsValue(def);
}
