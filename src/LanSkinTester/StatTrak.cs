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
    // Kills counted since the last save: re-added if the launcher changed the file meanwhile (its change wins).
    private readonly List<(CCSPlayerController Player, ushort Def, byte Team, int Paint, int Seed)> _statTrakUnsaved = new();

    [GameEventHandler]
    public HookResult OnStatTrakKill(EventPlayerDeath @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (attacker is not { IsValid: true } || victim is not { IsValid: true } || !IsHumanPlayer(attacker)
            || !IsEnemy(attacker, victim) || !SkinsAllowed()) return HookResult.Continue;
        // A grenade or fire kill isn't the gun in hand's.
        var used = @event.Weapon ?? "";
        if (IsGrenade(used) || used is "inferno" or "planted_c4" or "world" or "") return HookResult.Continue;
        // Only a counter that is really theirs: the host's own, or a friend's own skins (not the shared loadout).
        if (!IsHost(attacker) && _store.Find(OwnKey(attacker)) == null) return HookResult.Continue;
        if (attacker.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value is not { IsValid: true } weapon) return HookResult.Continue;
        var item = weapon.AttributeManager.Item;
        var skin = GetLoadout(attacker).SkinFor(item.ItemDefinitionIndex, attacker.TeamNum);
        if (skin is not { StatTrak: true }) return HookResult.Continue;

        skin.StatTrakCount++;
        _statTrakUnsaved.Add((attacker, item.ItemDefinitionIndex, attacker.TeamNum, skin.Paint, skin.Seed));
        // "kill eater" holds the count as raw int bits inside a float, like when the skin is applied.
        var kills = BitConverter.Int32BitsToSingle(skin.StatTrakCount);
        SetAttribute(item.NetworkedDynamicAttributes, "kill eater", kills);
        SetAttribute(item.AttributeList, "kill eater", kills);

        if (!_statTrakSavePending)
        {
            _statTrakSavePending = true;
            AddTimer(5f, SaveStatTrak);
        }
        return HookResult.Continue;
    }

    private void SaveStatTrak()
    {
        _statTrakSavePending = false;
        // The launcher wrote the file since (an equip, a preset): load that first, then add the kills again.
        if (_store.ChangedOnDisk() && _store.TryLoad())
            foreach (var (player, def, team, paint, seed) in _statTrakUnsaved.Where(k => k.Player.IsValid))
                // Only onto the same gun: a different skin equipped since starts from its own count.
                if (GetLoadout(player).SkinFor(def, team) is { StatTrak: true } skin && skin.Paint == paint && skin.Seed == seed)
                    skin.StatTrakCount++;
        _statTrakUnsaved.Clear();
        _store.Save();
    }
}
