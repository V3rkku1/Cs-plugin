using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Experimental: models from Workshop addons for the knife, a gun or the T / CT agent. MultiAddonManager
/// (mm_extra_addons, set by the launcher) mounts the addons and makes every player download them; this precaches
/// the models when the map loads and puts them on every human's knife or gun (world and first-person model) each
/// time it's pulled out, and on their player model at spawn.
/// </summary>
public partial class LanSkinTester
{
    private static readonly Regex CustomModelPath = new(@"^[a-z0-9_/.\-]+\.vmdl$");
    private static readonly Regex CustomSlot = new(@"^(knife|agent_t|agent_ct|weapon_[a-z0-9_]+)$");
    private static readonly Regex CustomModelLine = new(@"^\s*lst_custom_model\s+(\S+)\s+""([^""]*)""", RegexOptions.Multiline);

    /// <summary>What it replaces ("knife", "agent_t", "agent_ct", "weapon_ak47") -> model.</summary>
    private readonly Dictionary<string, string> _customModels = new();
    /// <summary>The models this map precached: one that wasn't would show as the error model.</summary>
    private HashSet<string> _precachedModels = new();

    /// <summary>The models the launcher's cfg asks for, read straight from the file: the cfg runs after precaching.</summary>
    internal static Dictionary<string, string> CustomModelsFromCfg(string text) =>
        CustomModelLine.Matches(text)
            .Where(m => CustomSlot.IsMatch(m.Groups[1].Value) && CustomModelPath.IsMatch(m.Groups[2].Value))
            .GroupBy(m => m.Groups[1].Value)
            .ToDictionary(g => g.Key, g => g.Last().Groups[2].Value);

    private void LoadCustomModels()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            try { _precachedModels = File.Exists(ServerSettingsPath) ? CustomModelsFromCfg(File.ReadAllText(ServerSettingsPath)).Values.ToHashSet() : new(); }
            catch (IOException) { _precachedModels = new(); }
            foreach (var model in _precachedModels) manifest.AddResource(model);
        });
        RegisterEventHandler<EventItemEquip>((@event, _) =>
        {
            if (_customModels.Count > 0 && @event.Userid is { IsValid: true } player)
                Server.NextFrame(() => ApplyCustomWeaponModel(player));
            return HookResult.Continue;
        });
    }

    [ConsoleCommand("lst_custom_model", "Experimental: lst_custom_model clear | <knife|agent_t|agent_ct|weapon_ak47> \"models/....vmdl\"")]
    [CommandHelper(minArgs: 1, whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnCustomModelCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var slot = cmd.GetArg(1).Trim().ToLowerInvariant();
        if (slot == "clear") { _customModels.Clear(); return; }
        var model = cmd.ArgCount > 2 ? cmd.GetArg(2).Trim() : "";
        if (!CustomSlot.IsMatch(slot) || !CustomModelPath.IsMatch(model)) return;
        _customModels[slot] = model;
        if (!_precachedModels.Contains(model))
            Server.PrintToConsole($"[LanSkinTester] Custom model {model} wasn't precached: it shows after the next map load.");
    }

    /// <summary>A custom model for this slot that the map has precached, or null.</summary>
    private string? CustomModelFor(string slot) =>
        _customModels.TryGetValue(slot, out var model) && _precachedModels.Contains(model) ? model : null;

    /// <summary>The custom agent for the player's side, or null (Agents.cs then uses the loadout's).</summary>
    private string? CustomAgentFor(CCSPlayerController player) =>
        player.Team switch
        {
            CsTeam.Terrorist => CustomModelFor("agent_t"),
            CsTeam.CounterTerrorist => CustomModelFor("agent_ct"),
            _ => null,
        };

    private void ApplyCustomWeaponModel(CCSPlayerController player)
    {
        if (!player.IsValid || !SkinsAllowed() || !IsHumanPlayer(player)) return;
        var pawn = player.PlayerPawn.Value;
        var weapon = pawn?.WeaponServices?.ActiveWeapon.Value;
        if (pawn is not { IsValid: true } || weapon is not { IsValid: true }) return;
        // The definition index, not the designer name: the M4A1-S, USP-S, CZ75 and R8 report their base gun's name.
        var slot = ItemData.IsKnifeClassname(weapon.DesignerName) ? "knife"
            : ItemData.Weapons.GetValueOrDefault(weapon.AttributeManager.Item.ItemDefinitionIndex);
        if (slot == null || CustomModelFor(slot) is not { } model) return;
        try
        {
            weapon.SetModel(model);
            // The first-person model is its own entity, owned by the pawn.
            foreach (var view in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("predicted_viewmodel"))
                if (view.OwnerEntity.Value?.Index == pawn.Index) view.SetModel(model);
        }
        catch (Exception e)
        {
            // Experimental: a CS2 update can move these; the normal model stays.
            Logger.LogWarning("Custom model failed: {Error}", e.Message);
        }
    }
}
