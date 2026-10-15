using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Buy period for the plugin retakes style. B3none's plugin hands every player a rifle on round
/// post-start. With this on, humans are stripped after that and get a freeze-time buy. Bots keep theirs.
/// </summary>
public partial class LanSkinTester
{
    private bool _retakeBuy;
    private int _retakeBuySeconds = 12;

    [ConsoleCommand("lst_retake_buy", "Plugin retakes buy period: lst_retake_buy 0|1 [seconds]")]
    [CommandHelper(minArgs: 1, usage: "0|1 [seconds]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRetakeBuyCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _retakeBuy = cmd.GetArg(1) == "1";
        if (cmd.ArgCount > 2 && int.TryParse(cmd.GetArg(2), out var seconds))
            _retakeBuySeconds = Math.Clamp(seconds, 4, 20);
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

        foreach (var player in Humans())
        {
            if (!player.PawnIsAlive) continue;
            StripGuns(player);
            var money = player.InGameMoneyServices;
            if (money != null)
            {
                money.Account = 16000;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInGameMoneyServices");
            }
            Reply(player, $"Buy your guns. {_retakeBuySeconds} seconds.");
        }
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
}
