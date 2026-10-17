using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Buy period for the plugin retakes style. B3none's plugin hands every player a rifle on round
/// post-start. With this on, humans are stripped after that and get a freeze-time buy. Bots keep theirs.
/// Whatever a human finishes the buy holding is remembered per side and equipped on later rounds.
/// </summary>
public partial class LanSkinTester
{
    private bool _retakeBuy;
    private int _retakeBuySeconds = 12;
    private bool _retakeBuyListening, _retakeKeepGuns;
    // How often a round is a full buy, a force buy or an eco (weights), and the money each one gets.
    private int _wFull = 1, _wForce, _wEco;
    private enum BuyRound { Full, Force, Eco }
    private static readonly int[] BuyMoney = [16000, 2900, 800];
    private readonly Dictionary<(ulong Id, CsTeam Team), RetakeLoadout> _retakeLoadouts = new();

    private sealed class RetakeLoadout
    {
        public List<string> Weapons { get; } = new();
        public bool Helmet;
        public bool Kevlar;
        public bool Defuser;
    }

    [ConsoleCommand("lst_retake_buy", "Plugin retakes buy period: lst_retake_buy 0|1 [seconds] [keepGuns] [full] [force] [eco]")]
    [CommandHelper(minArgs: 1, usage: "0|1 [seconds] [keepGuns] [full] [force] [eco]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeBuyCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (!_retakeBuyListening)
        {
            _retakeBuyListening = true;
            RegisterListener<Listeners.OnMapStart>(_ => _retakeLoadouts.Clear());
        }

        var on = cmd.GetArg(1) == "1";
        if (!on) _retakeLoadouts.Clear();
        _retakeBuy = on;
        if (cmd.ArgCount > 2 && int.TryParse(cmd.GetArg(2), out var seconds))
            _retakeBuySeconds = Math.Clamp(seconds, 4, 20);
        _retakeKeepGuns = cmd.ArgCount > 3 && cmd.GetArg(3) == "1";
        if (cmd.ArgCount > 6)
        {
            int W(int i) => int.TryParse(cmd.GetArg(i), out var n) ? Math.Clamp(n, 0, 100) : 0;
            (_wFull, _wForce, _wEco) = (W(4), W(5), W(6));
            if (_wFull + _wForce + _wEco == 0) _wFull = 1;
        }
    }

    private BuyRound PickBuyRound()
    {
        var roll = Random.Shared.Next(_wFull + _wForce + _wEco);
        return roll < _wFull ? BuyRound.Full : roll < _wFull + _wForce ? BuyRound.Force : BuyRound.Eco;
    }

    [GameEventHandler]
    public HookResult OnRetakeBuyRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        if (!_retakeBuy) return HookResult.Continue;
        // Allocation runs on round_poststart, just after this event. Open the buy after that.
        AddTimer(0.6f, OpenRetakeBuy);
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnRetakeBuyFreezeEnd(EventRoundFreezeEnd @event, GameEventInfo info)
    {
        if (!_retakeBuy) return HookResult.Continue;
        RememberRetakeBuys();
        Server.ExecuteCommand("mp_buy_anywhere 0");
        Server.ExecuteCommand("mp_buytime 0");
        return HookResult.Continue;
    }

    private void OpenRetakeBuy()
    {
        if (!_retakeBuy) return;
        Server.ExecuteCommand("mp_maxmoney 16000");
        Server.ExecuteCommand("mp_startmoney 16000");
        Server.ExecuteCommand("mp_buy_anywhere 1");
        Server.ExecuteCommand($"mp_buytime {_retakeBuySeconds}");
        Server.ExecuteCommand($"mp_freezetime {_retakeBuySeconds}");

        // Same round type for everyone. Eco and force rounds are enforced by the money: 800 buys pistols and
        // armor only, 2900 a SMG or a cheap rifle. Allocator guns stay; the money is just for extras.
        var round = PickBuyRound();
        foreach (var player in Humans())
        {
            if (!player.PawnIsAlive) continue;
            if (!_retakeKeepGuns) StripGuns(player);
            var money = player.InGameMoneyServices;
            if (money != null)
            {
                money.Account = BuyMoney[(int)round];
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            }

            if (round == BuyRound.Eco && !_retakeKeepGuns)
                player.GiveNamedItem(player.Team == CsTeam.CounterTerrorist ? "weapon_hkp2000" : "weapon_glock");
            if (round == BuyRound.Full && !_retakeKeepGuns && EquipRemembered(player))
                Reply(player, "Your guns are back. Buy if you want to change them.");
            else
                Reply(player, $"{round} buy round: ${BuyMoney[(int)round]}. {_retakeBuySeconds} seconds.");
        }
    }

    private void RememberRetakeBuys()
    {
        foreach (var player in Humans())
        {
            if (!player.IsValid || !player.PawnIsAlive) continue;
            if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) continue;
            var loadout = CaptureRetakeLoadout(player);
            var key = (RetakePlayerId(player), player.Team);
            // Armor alone is the plugin's free helmet. A remembered buy needs a gun or a grenade.
            if (loadout.Weapons.Count == 0)
                _retakeLoadouts.Remove(key);
            else
                _retakeLoadouts[key] = loadout;
        }
    }

    private bool EquipRemembered(CCSPlayerController player)
    {
        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) return false;
        if (!_retakeLoadouts.TryGetValue((RetakePlayerId(player), player.Team), out var loadout)) return false;
        if (loadout.Weapons.Count == 0) return false;

        foreach (var name in loadout.Weapons)
            player.GiveNamedItem(name);
        if (loadout.Helmet) player.GiveNamedItem("item_assaultsuit");
        else if (loadout.Kevlar) player.GiveNamedItem("item_kevlar");
        if (loadout.Defuser && player.Team == CsTeam.CounterTerrorist) player.GiveNamedItem("item_defuser");
        player.ExecuteClientCommandFromServer(loadout.Weapons.Any(IsPrimaryGun) ? "slot1" : "slot2");
        return true;
    }

    private static RetakeLoadout CaptureRetakeLoadout(CCSPlayerController player)
    {
        var loadout = new RetakeLoadout();
        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons != null)
        {
            foreach (var handle in weapons)
            {
                var name = handle.Value?.DesignerName;
                if (string.IsNullOrEmpty(name) || ItemData.IsKnifeClassname(name) || name.Contains("c4")) continue;
                loadout.Weapons.Add(name);
            }
        }

        var pawn = player.PlayerPawn.Value;
        if (pawn != null && pawn.IsValid && pawn.ItemServices != null)
        {
            var items = new CCSPlayer_ItemServices(pawn.ItemServices.Handle);
            loadout.Helmet = items.HasHelmet;
            loadout.Kevlar = items.HasHelmet || pawn.ArmorValue > 0;
            loadout.Defuser = items.HasDefuser;
        }
        return loadout;
    }

    private static void StripGuns(CCSPlayerController player)
    {
        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons == null) return;
        var names = new List<string>();
        foreach (var handle in weapons)
        {
            var name = handle.Value?.DesignerName;
            // Keep the knife, and the bomb when auto-plant is off and a terrorist still has to plant.
            if (string.IsNullOrEmpty(name) || ItemData.IsKnifeClassname(name) || name.Contains("c4")) continue;
            names.Add(name);
        }
        foreach (var name in names)
            Utilities.RemoveItemByDesignerName(player, name);
    }

    private static ulong RetakePlayerId(CCSPlayerController player) =>
        player.SteamID != 0 ? player.SteamID : (ulong)player.Slot + 1;

    private static bool IsPrimaryGun(string name) => name is not (
        "weapon_deagle" or "weapon_elite" or "weapon_fiveseven" or "weapon_glock" or "weapon_hkp2000"
        or "weapon_p250" or "weapon_tec9" or "weapon_usp_silencer" or "weapon_cz75a" or "weapon_revolver"
        or "weapon_taser" or "weapon_hegrenade" or "weapon_flashbang" or "weapon_smokegrenade"
        or "weapon_molotov" or "weapon_incgrenade" or "weapon_decoy");
}
