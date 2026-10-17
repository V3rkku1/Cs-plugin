using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// lst_team_sizes T CT (0 0 = off): how many players fit on each side. The game's own limit comes from
/// the map's spawn points, so a Workshop map with few spawns says "team full" with no bots. Below the
/// limit we move the player ourselves; at the limit the join is refused.
/// </summary>
public partial class LanSkinTester
{
    private int _sizeT, _sizeCT;
    private bool _sizesListening;

    [ConsoleCommand("lst_team_sizes", "Team sizes: lst_team_sizes T CT (0 0 = off)")]
    [CommandHelper(minArgs: 2, usage: "T CT", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnTeamSizesCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (!_sizesListening) AddCommandListener("jointeam", OnJoinTeam);
        _sizesListening = true;
        _sizeT = int.TryParse(cmd.GetArg(1), out var t) ? Math.Clamp(t, 0, 32) : 0;
        _sizeCT = int.TryParse(cmd.GetArg(2), out var ct) ? Math.Clamp(ct, 0, 32) : 0;
    }

    private HookResult OnJoinTeam(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player is not { IsValid: true } || (_sizeT == 0 && _sizeCT == 0)) return HookResult.Continue;
        if (!int.TryParse(cmd.GetArg(1), out var wanted) || wanted is 1 or > 3) return HookResult.Continue; // spectators: the game's rule
        int Free(CsTeam team, int size) => size - Utilities.GetPlayers().Count(p => p.IsValid && p != player && p.Team == team);
        var t = Free(CsTeam.Terrorist, _sizeT);
        var ct = Free(CsTeam.CounterTerrorist, _sizeCT);
        var team = wanted == 2 ? CsTeam.Terrorist : wanted == 3 ? CsTeam.CounterTerrorist : ct > t ? CsTeam.CounterTerrorist : CsTeam.Terrorist; // 0 = auto: emptier side
        if ((team == CsTeam.Terrorist ? t : ct) <= 0)
        {
            player.PrintToChat(" Team is full.");
            return HookResult.Handled;
        }
        player.ChangeTeam(team);
        if (IsWarmup()) AddTimer(0.3f, () => { if (player.IsValid && !player.PawnIsAlive) player.Respawn(); });
        return HookResult.Handled;
    }
}
