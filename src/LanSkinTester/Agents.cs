using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Agents (player models) and collectible pins from the loadout. The technique (SetModel on the pawn on
/// spawn, the pin in the inventory rank slot 5) comes from cs2-WeaponPaints by Nereziel.
/// </summary>
public partial class LanSkinTester
{
    private static readonly Regex AgentModel = new(@"^agents/models/[a-z0-9_/]+\.vmdl$");

    private void ApplyAgentAndPin(CCSPlayerController player)
    {
        if (!player.IsValid || player.IsBot) return;
        var loadout = GetLoadout(player);

        var model = player.Team == CsTeam.CounterTerrorist ? loadout.AgentCT : player.Team == CsTeam.Terrorist ? loadout.AgentT : null;
        if (model != null && AgentModel.IsMatch(model))
        {
            Server.NextFrame(() =>
            {
                var pawn = player.IsValid ? player.PlayerPawn.Value : null;
                if (pawn != null && pawn.IsValid && player.PawnIsAlive) pawn.SetModel(model);
            });
        }

        try
        {
            if (player.InventoryServices is { } inventory && inventory.Rank.Length > 5)
            {
                inventory.Rank[5] = loadout.Pin > 0 ? (MedalRank_t)loadout.Pin : MedalRank_t.MEDAL_RANK_NONE;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
            }
        }
        catch (Exception)
        {
            // The inventory layout moved after a CS2 update: no pin, everything else still works.
        }
    }
}
