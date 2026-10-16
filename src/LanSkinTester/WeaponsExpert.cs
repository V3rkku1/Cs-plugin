using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace LanSkinTester;

/// <summary>
/// Weapons Expert: every gun can be bought once per match. CS2 no longer ships the mode, so the
/// launcher runs competitive and this enforces the rule: buying a gun again takes it back and refunds it.
/// Bots are left alone (they would just keep re-buying).  lst_weapons_expert 0|1
/// </summary>
public partial class LanSkinTester
{
    private static readonly HashSet<string> Grenades = ["hegrenade", "flashbang", "smokegrenade", "molotov", "incgrenade", "decoy"];
    private bool _weaponsExpert;
    private bool _weaponsExpertListening;
    private readonly Dictionary<ulong, HashSet<string>> _boughtGuns = new();

    [ConsoleCommand("lst_weapons_expert", "1 = each gun can be bought once per match (Weapons Expert)")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnWeaponsExpertCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (!_weaponsExpertListening)
        {
            _weaponsExpertListening = true;
            RegisterListener<Listeners.OnMapStart>(_ => _boughtGuns.Clear());
        }
        _weaponsExpert = cmd.GetArg(1) == "1";
        if (!_weaponsExpert) _boughtGuns.Clear();
    }

    [GameEventHandler]
    public HookResult OnWeaponsExpertPurchase(EventItemPurchase @event, GameEventInfo info)
    {
        var player = @event.Userid;
        var item = @event.Weapon ?? "";
        if (!_weaponsExpert || player == null || !IsHumanPlayer(player) || !item.StartsWith("weapon_")
            || Grenades.Contains(item["weapon_".Length..])) return HookResult.Continue;

        var bought = _boughtGuns.TryGetValue(player.SteamID, out var set) ? set : _boughtGuns[player.SteamID] = new();
        if (bought.Add(item)) return HookResult.Continue;

        NextFrame(() =>
        {
            if (!player.IsValid || FindPlayerWeapon(player, w => w.DesignerName == item) is not { } weapon) return;
            var price = weapon.As<CCSWeaponBase>().VData?.Price ?? 0;
            weapon.AddEntityIOEvent("Kill", null, null, "", 0.01f);
            if (player.InGameMoneyServices is { } money && price > 0)
            {
                money.Account += price;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            }
            Reply(player, $"Weapons Expert: you already used the {item["weapon_".Length..]}. Pick another gun.");
        });
        return HookResult.Continue;
    }
}
