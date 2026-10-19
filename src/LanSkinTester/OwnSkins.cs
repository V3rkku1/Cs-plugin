using Microsoft.Extensions.Logging;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace LanSkinTester;

/// <summary>
/// A friend's own skins on the host's server: the friend's launcher writes cfg/lst_myskins.cfg (their
/// loadout as css_myskins PART TOTAL CHUNK lines) and their CS2 runs it after joining. Once every part is
/// in, that player gets their own loadout, even when the host shares theirs with everyone.
/// </summary>
public partial class LanSkinTester
{
    private readonly Dictionary<int, string[]> _ownParts = new();

    [GameEventHandler]
    public HookResult OnOwnSkinsDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        // Half-sent skins must not mix with the next player in this slot.
        if (@event.Userid is { IsValid: true } player) _ownParts.Remove(player.Slot);
        return HookResult.Continue;
    }

    private static string OwnKey(CCSPlayerController player) =>
        "own:" + (player.SteamID != 0 ? player.SteamID.ToString() : "name:" + player.PlayerName);

    [ConsoleCommand("css_myskins", "Sent by your launcher: your own skins on a friend's server")]
    [CommandHelper(minArgs: 3, usage: "PART TOTAL CHUNK", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnMySkinsCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        // SteamID 0 (LAN clients without Steam) would be known by a name anyone can take.
        if (player is not { IsValid: true } || IsBotPlayer(player) || player.SteamID == 0) return;
        if (!int.TryParse(cmd.GetArg(1), out var part) || !int.TryParse(cmd.GetArg(2), out var total)
            || total < 1 || total > LoadoutCodec.MaxChunks || part < 1 || part > total) return;
        if (!_ownParts.TryGetValue(player.Slot, out var parts) || parts.Length != total)
            _ownParts[player.Slot] = parts = new string[total];
        parts[part - 1] = cmd.GetArg(3).Trim('"');
        if (parts.Any(p => p == null)) return;
        _ownParts.Remove(player.Slot);

        var loadout = LoadoutCodec.Decode(parts);
        if (loadout == null)
        {
            Logger.LogWarning("css_myskins from {Name}: not a loadout", player.PlayerName);
            return;
        }
        // The same skins sent again (the launcher repeats it while you play): nothing to do.
        var key = OwnKey(player);
        if (_store.ChangedOnDisk()) _store.TryLoad(); // the launcher may have just saved: don't write over it
        if (_store.Find(key) is { } old && System.Text.Json.JsonSerializer.Serialize(old) == System.Text.Json.JsonSerializer.Serialize(loadout)) return;
        _store.Set(key, loadout);
        _store.Save();
        if (SkinsAllowed()) RefreshPlayer(player, "Your own skins from your launcher are on.");
    }
}
