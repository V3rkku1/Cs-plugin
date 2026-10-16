using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// "Rifles" bots: with max money, bots buy the AWP every round. The launcher turns off bot sniping with
/// the bot_allow_* cvars, and as a safety net the plugin swaps any sniper rifle a bot still gets for an
/// AK-47 (T) or M4A4 (CT).  lst_bot_no_snipers 1|0
/// </summary>
public partial class LanSkinTester
{
    private static readonly HashSet<string> SniperRifles = ["awp", "ssg08", "scar20", "g3sg1"];
    private bool _botNoSnipers;

    [ConsoleCommand("lst_bot_no_snipers", "1 = bots never keep a sniper rifle (it's swapped for an AK-47 / M4A4)")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotNoSnipersCommand(CCSPlayerController? caller, CommandInfo cmd) => _botNoSnipers = cmd.GetArg(1) == "1";

    [GameEventHandler]
    public HookResult OnBotItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        var bot = @event.Userid;
        var item = (@event.Item ?? "").Replace("weapon_", "");
        if (!_botNoSnipers || bot == null || !bot.IsValid || !bot.IsBot || !SniperRifles.Contains(item)) return HookResult.Continue;

        NextFrame(() =>
        {
            if (!bot.IsValid || !bot.PawnIsAlive) return;
            Utilities.RemoveItemByDesignerName(bot, "weapon_" + item);
            var rifle = bot.Team == CsTeam.Terrorist ? "weapon_ak47" : "weapon_m4a1";
            // Don't hand out a second rifle if it already carries one.
            if (FindPlayerWeapon(bot, w => w.DesignerName is "weapon_ak47" or "weapon_m4a1" or "weapon_m4a1_silencer") == null)
                bot.GiveNamedItem(rifle);
        });
        return HookResult.Continue;
    }

    // ---------------------------------------------------------------------------------------------
    // Calm bots: lst_bot_calm 1 stops bots from bunny-hopping around. A bot still gets one jump every
    // few seconds (to climb a box or get unstuck); jumps in between are cancelled as they start.
    // ---------------------------------------------------------------------------------------------

    private bool _botCalm;
    private readonly Dictionary<int, DateTime> _botLastJump = new();
    private static readonly TimeSpan BotJumpGap = TimeSpan.FromSeconds(3);

    [ConsoleCommand("lst_bot_calm", "1 = bots jump at most once every few seconds")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotCalmCommand(CCSPlayerController? caller, CommandInfo cmd) => _botCalm = cmd.GetArg(1) == "1";

    [GameEventHandler]
    public HookResult OnBotJump(EventPlayerJump @event, GameEventInfo info)
    {
        var bot = @event.Userid;
        if (!_botCalm || bot is not { IsValid: true } || !IsBotPlayer(bot)) return HookResult.Continue;
        var now = DateTime.UtcNow;
        if (!_botLastJump.TryGetValue(bot.Slot, out var last) || now - last >= BotJumpGap)
        {
            _botLastJump[bot.Slot] = now;
            return HookResult.Continue;
        }
        // Too soon after the last jump: take the upward speed away so it stays on the ground.
        NextFrame(() =>
        {
            var pawn = bot.IsValid ? bot.PlayerPawn.Value : null;
            if (pawn is not { IsValid: true } || pawn.AbsVelocity is not { } v || v.Z <= 0) return;
            pawn.Teleport(null, null, new Vector(v.X * 0.8f, v.Y * 0.8f, 0));
        });
        return HookResult.Continue;
    }
}
