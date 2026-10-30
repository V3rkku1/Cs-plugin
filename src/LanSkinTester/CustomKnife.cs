using System.Text.RegularExpressions;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Experimental: one knife model from a Workshop addon. MultiAddonManager (mm_extra_addons, set by the launcher)
/// mounts the addon and makes every player download it; this precaches the model when the map loads and puts it
/// on every human's knife, the world model and the first-person one, each time they pull the knife out.
/// </summary>
public partial class LanSkinTester
{
    private static readonly Regex CustomModel = new(@"^[a-z0-9_/.\-]+\.vmdl$");
    private static readonly Regex CustomKnifeLine = new(@"^\s*lst_custom_knife\s+""([^""]*)""", RegexOptions.Multiline);

    private string _customKnife = "";
    /// <summary>The model this map precached: a model that wasn't precached would show as the error model.</summary>
    private string _precachedKnife = "";

    /// <summary>The model the launcher's cfg asks for, read straight from the file: the cfg runs after precaching.</summary>
    internal static string CustomKnifeFromCfg(string text)
    {
        var model = CustomKnifeLine.Match(text) is { Success: true } m ? m.Groups[1].Value : "";
        return CustomModel.IsMatch(model) ? model : "";
    }

    private void LoadCustomKnife()
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            try { _precachedKnife = File.Exists(ServerSettingsPath) ? CustomKnifeFromCfg(File.ReadAllText(ServerSettingsPath)) : ""; }
            catch (IOException) { _precachedKnife = ""; }
            if (_precachedKnife != "") manifest.AddResource(_precachedKnife);
        });
        RegisterEventHandler<EventItemEquip>((@event, _) =>
        {
            if (_customKnife != "" && @event.Userid is { IsValid: true } player && IsKnife(@event.Item))
                Server.NextFrame(() => ApplyCustomKnife(player));
            return HookResult.Continue;
        });
    }

    [ConsoleCommand("lst_custom_knife", "Experimental: a knife model from a mounted Workshop addon (models/....vmdl), \"\" for off")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnCustomKnifeCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var model = cmd.ArgCount > 1 ? cmd.GetArg(1).Trim() : "";
        _customKnife = CustomModel.IsMatch(model) ? model : "";
        if (_customKnife != "" && _customKnife != _precachedKnife)
            Server.PrintToConsole($"[LanSkinTester] Custom knife {_customKnife} wasn't precached: it shows after the next map load.");
    }

    private static bool IsKnife(string item) => item.Contains("knife") || item.Contains("bayonet");

    private void ApplyCustomKnife(CCSPlayerController player)
    {
        if (_customKnife == "" || _customKnife != _precachedKnife || !SkinsAllowed() || !IsHumanPlayer(player)) return;
        var pawn = player.PlayerPawn.Value;
        var knife = pawn?.WeaponServices?.ActiveWeapon.Value;
        if (pawn is not { IsValid: true } || knife is not { IsValid: true } || !IsKnife(knife.DesignerName)) return;
        try
        {
            knife.SetModel(_customKnife);
            // The first-person knife is its own entity, owned by the pawn.
            foreach (var view in Utilities.FindAllEntitiesByDesignerName<CBaseModelEntity>("predicted_viewmodel"))
                if (view.OwnerEntity.Value?.Index == pawn.Index) view.SetModel(_customKnife);
        }
        catch (Exception e)
        {
            // Experimental: a CS2 update can move these; the normal knife stays.
            Logger.LogWarning("Custom knife failed: {Error}", e.Message);
        }
    }
}
