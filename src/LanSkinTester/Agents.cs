using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Agents (player models), collectible pins and music kits from the loadout. The technique (SetModel on the
/// pawn on spawn, the pin in the inventory rank slot 5, the music kit id on the controller and in the
/// inventory) comes from cs2-WeaponPaints by Nereziel.
/// </summary>
public partial class LanSkinTester
{
    private static readonly Regex AgentModel = new(@"^agents/models/[a-z0-9_/]+\.vmdl$");

    /// <summary>Slots we gave a music kit, so picking "default" again can take it away.</summary>
    private readonly HashSet<int> _musicKitSlots = new();

    /// <summary>Agent models precached for this map. Only these are safe to switch to.</summary>
    private readonly HashSet<string> _precachedAgents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Precaches every agent model (agents.txt from the launcher, plus any in a saved loadout).</summary>
    private void PrecacheAgents(ResourceManifest manifest)
    {
        try
        {
            _precachedAgents.Clear();
            var models = new List<string>();
            try
            {
                var file = Path.Combine(ModuleDirectory, "agents.txt");
                if (File.Exists(file)) models.AddRange(File.ReadAllLines(file));
            }
            catch (IOException) { /* the loadouts below still work */ }
            models.AddRange(_store.AllAgentModels());
            foreach (var model in models.Select(m => m.Trim()).Where(m => AgentModel.IsMatch(m)).Distinct())
            {
                manifest.AddResource(model);
                _precachedAgents.Add(model);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Could not precache the agent models");
        }
    }

    private void ApplyAgentAndPin(CCSPlayerController player)
    {
        if (!IsHumanPlayer(player)) return;
        var loadout = GetLoadout(player);

        var model = player.Team == CsTeam.CounterTerrorist ? loadout.AgentCT : player.Team == CsTeam.Terrorist ? loadout.AgentT : null;
        if (model != null && AgentModel.IsMatch(model) && _precachedAgents.Contains(model))
        {
            NextFrame(() =>
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

        ApplyMusicKit(player, loadout.MusicKit);
    }

    private void ApplyMusicKit(CCSPlayerController player, ushort kit)
    {
        if (kit == 0 && !_musicKitSlots.Remove(player.Slot)) return;
        if (kit != 0) _musicKitSlots.Add(player.Slot);
        try
        {
            if (player.InventoryServices is { } inventory)
            {
                inventory.MusicID = kit;
                Utilities.SetStateChanged(player, "CCSPlayerController", "m_pInventoryServices");
            }
            player.MusicKitID = kit;
            Utilities.SetStateChanged(player, "CCSPlayerController", "m_iMusicKitID");
        }
        catch (Exception)
        {
            // Same as the pin: a moved field after a CS2 update only costs the music kit.
        }
    }
}
