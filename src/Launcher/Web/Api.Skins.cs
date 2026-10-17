using LanSkinTester.Launcher.Core;

namespace LanSkinTester.Launcher.Web;

/// <summary>The local loadout (loadouts.json): read it, and keep it to what the inventory owns.</summary>
public partial class Api
{
    private PlayerLoadout ReadLoadout()
    {
        if (App.Game is not { } g || !Directory.Exists(g.PluginDir)) return new PlayerLoadout();
        var store = new LoadoutStore(g.LoadoutFile);
        var loadout = store.Get(LoadoutStore.LocalKey);
        // Without the admin code, drop anything not in the inventory (e.g. left over from the old launcher).
        if (UnboxOnly && PruneUnowned(loadout)) store.Save();
        return loadout;
    }

    private bool PruneUnowned(PlayerLoadout l)
    {
        var skins = OwnedSkins();
        var stickers = OwnedIds("sticker");
        var charms = OwnedIds("charm");
        var pins = OwnedIds("pin");
        var kits = OwnedIds("music");
        var changed = false;
        foreach (var (def, s) in l.Skins.ToList())
        {
            // Vanilla guns (paint 0) are always fine; knives and gloves must be owned either way.
            if ((s.Paint != 0 || IsKnifeType(def) || IsGloveType(def)) && !skins.Contains((def, s.Paint))) { l.Skins.Remove(def); changed = true; continue; }
            changed |= s.Stickers?.RemoveAll(x => !stickers.Contains(x.Id)) > 0;
            if (s.Keychain is { } k && !charms.Contains(k.Id)) { s.Keychain = null; changed = true; }
        }
        if (l.Knife != 0 && !skins.Any(o => o.Def == l.Knife)) { l.Knife = 0; changed = true; }
        if (l.Gloves != 0 && !skins.Any(o => o.Def == l.Gloves)) { l.Gloves = 0; changed = true; }
        if (l.Pin != 0 && !pins.Contains(l.Pin)) { l.Pin = 0; changed = true; }
        if (l.MusicKit != 0 && !kits.Contains(l.MusicKit)) { l.MusicKit = 0; changed = true; }
        if (l.AgentT != null || l.AgentCT != null) { l.AgentT = l.AgentCT = null; changed = true; } // agents need the admin code
        return changed;
    }

    private GameFolder RequirePlugin() =>
        App.Game is { PluginInstalled: true } g ? g : throw new ApiException("Install the plugin first (Setup page).");

    private static bool IsKnifeType(ushort def) => ItemData.Knives.ContainsValue(def);
    private static bool IsGloveType(ushort def) => ItemData.Gloves.ContainsValue(def);
}
