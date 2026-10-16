using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace LanSkinTester;

/// <summary>
/// Match rules the launcher can switch on:
///   lst_hsonly 1    only headshots (and knives) do damage between players
///   lst_pistols 1   pistols only: other guns can't be bought or picked up (bought ones are refunded)
/// </summary>
public partial class LanSkinTester
{
    private bool _headshotOnly;
    private bool _pistolsOnly;
    private bool _rulesListening;

    [ConsoleCommand("lst_hsonly", "1 = only headshots (and knives) hurt players")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnHeadshotOnlyCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        ListenForRules();
        _headshotOnly = cmd.GetArg(1) == "1";
    }

    [ConsoleCommand("lst_pistols", "1 = pistols only (no rifles, SMGs, heavy or snipers)")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnPistolsOnlyCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        ListenForRules();
        _pistolsOnly = cmd.GetArg(1) == "1";
        if (_pistolsOnly)
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive)) DropNonPistols(player, refund: false);
    }

    private void ListenForRules()
    {
        if (_rulesListening) return;
        _rulesListening = true;
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnRulesTakeDamage);
    }

    private HookResult OnRulesTakeDamage(CBaseEntity victim, CTakeDamageInfo damage)
    {
        if (!_headshotOnly || victim.DesignerName != "player") return HookResult.Continue;
        try
        {
            var attacker = damage.Attacker.Value;
            if (attacker == null || !attacker.IsValid || attacker.DesignerName != "player") return HookResult.Continue; // falls, the world
            if (damage.GetHitGroup() == HitGroup_t.HITGROUP_HEAD) return HookResult.Continue;
            var weapon = damage.Ability.Value?.DesignerName ?? damage.Inflictor.Value?.DesignerName ?? "";
            if (weapon.Contains("knife") || weapon.Contains("bayonet")) return HookResult.Continue;
            damage.Damage = 0;
            return HookResult.Handled;
        }
        catch (Exception)
        {
            return HookResult.Continue; // unknown damage layout: never break the game
        }
    }

    private static bool IsNonPistolGun(string classname)
    {
        var def = ItemData.Weapons.FirstOrDefault(w => w.Value == classname).Key;
        return def != 0 && !ItemData.Pistols.Contains(def) && !ItemData.IsKnifeClassname(classname) && classname != "weapon_taser";
    }

    [GameEventHandler]
    public HookResult OnRulesPurchase(EventItemPurchase @event, GameEventInfo info)
    {
        if (!_pistolsOnly || @event.Userid is not { IsValid: true } player) return HookResult.Continue;
        var item = @event.Weapon ?? "";
        if (IsNonPistolGun(item)) Server.NextFrame(() => DropNonPistols(player, refund: true));
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRulesPickup(EventItemPickup @event, GameEventInfo info)
    {
        if (!_pistolsOnly || @event.Userid is not { IsValid: true } player) return HookResult.Continue;
        if (IsNonPistolGun("weapon_" + (@event.Item ?? "").Replace("weapon_", "")))
            Server.NextFrame(() => DropNonPistols(player, refund: false));
        return HookResult.Continue;
    }

    /// <summary>Removes every rifle / SMG / heavy / sniper the player carries; refunds them when they were just bought.</summary>
    private void DropNonPistols(CCSPlayerController player, bool refund)
    {
        if (!player.IsValid || !player.PawnIsAlive) return;
        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons == null) return;
        var removed = 0;
        foreach (var handle in weapons.ToList())
        {
            var weapon = handle.Value;
            if (weapon == null || !weapon.IsValid || !IsNonPistolGun(weapon.DesignerName)) continue;
            if (refund && player.InGameMoneyServices is { } money)
                money.Account += weapon.As<CCSWeaponBase>().VData?.Price ?? 0;
            weapon.AddEntityIOEvent("Kill", null, null, "", 0.01f);
            removed++;
        }
        if (removed == 0) return;
        if (refund) Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
        if (!player.IsBot) Reply(player, "Pistols only on this server: Deagle, Tec-9, Five-SeveN, USP-S, Glock, P250, CZ75...");
        player.ExecuteClientCommand("slot2");
    }
}
