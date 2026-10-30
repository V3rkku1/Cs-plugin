using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Timers;

namespace LanSkinTester;

/// <summary>
/// StatTrak™ that counts: a kill with a StatTrak gun (or knife) adds one to its counter in the loadout, shown
/// on the gun at once and saved a few seconds later (the launcher copies the host's back to the inventory item).
/// </summary>
public partial class LanSkinTester
{
    private bool _statTrakSavePending;

    [GameEventHandler]
    public HookResult OnStatTrakKill(EventPlayerDeath @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (attacker is not { IsValid: true } || victim is not { IsValid: true } || !IsHumanPlayer(attacker)
            || !IsEnemy(attacker, victim) || !SkinsAllowed()) return HookResult.Continue;
        // Only a counter that is really theirs: the host's own, or a friend's own skins (not the shared loadout).
        if (!IsHost(attacker) && _store.Find(OwnKey(attacker)) == null) return HookResult.Continue;
        if (attacker.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value is not { IsValid: true } weapon) return HookResult.Continue;
        var item = weapon.AttributeManager.Item;
        var skin = GetLoadout(attacker).SkinFor(item.ItemDefinitionIndex, attacker.TeamNum);
        if (skin is not { StatTrak: true }) return HookResult.Continue;

        skin.StatTrakCount++;
        // "kill eater" holds the count as raw int bits inside a float, like when the skin is applied.
        var kills = BitConverter.Int32BitsToSingle(skin.StatTrakCount);
        SetAttribute(item.NetworkedDynamicAttributes, "kill eater", kills);
        SetAttribute(item.AttributeList, "kill eater", kills);

        if (!_statTrakSavePending)
        {
            _statTrakSavePending = true;
            AddTimer(5f, () =>
            {
                _statTrakSavePending = false;
                _store.Save();
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        return HookResult.Continue;
    }
}
