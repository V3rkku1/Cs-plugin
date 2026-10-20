using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// "Rifles" bots: with max money, bots buy the AWP every round. The launcher turns off bot sniping with
/// the bot_allow_* cvars, and the plugin swaps any sniper or rifle a bot gets that isn't in the allowed list
/// for an allowed one (its own side's first).  lst_bot_no_snipers 1|0 · lst_bot_rifles ak47,m4a1,m4a1_silencer
/// </summary>
public partial class LanSkinTester
{
    private static readonly string[] SwappableRifles =
        ["awp", "ssg08", "scar20", "g3sg1", "ak47", "m4a1", "m4a1_silencer", "famas", "galilar", "aug", "sg556"];
    private static readonly HashSet<string> TRifles = ["ak47", "galilar", "sg556", "g3sg1"];
    private bool _botNoSnipers;
    private string[] _botRifles = ["ak47", "m4a1", "m4a1_silencer"];

    [ConsoleCommand("lst_bot_no_snipers", "1 = bots only keep the rifles lst_bot_rifles allows (others are swapped)")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotNoSnipersCommand(CCSPlayerController? caller, CommandInfo cmd) => _botNoSnipers = cmd.GetArg(1) == "1";

    [ConsoleCommand("lst_bot_rifles", "Rifles bots may keep: lst_bot_rifles ak47,m4a1,m4a1_silencer")]
    [CommandHelper(minArgs: 1, usage: "<rifle,rifle,...>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotRiflesCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var rifles = cmd.ArgString.Replace("weapon_", "").Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Where(SwappableRifles.Contains).Distinct().ToArray();
        if (rifles.Length > 0) _botRifles = rifles;
    }

    /// <summary>What a bot on <paramref name="t"/> (or CT) gets instead: an allowed rifle of its side, else any allowed one.</summary>
    internal static string BotRifleFor(string[] allowed, bool t)
    {
        var own = allowed.Where(r => TRifles.Contains(r) == t).ToArray();
        var pick = own.Length > 0 ? own : allowed;
        return "weapon_" + pick[Random.Shared.Next(pick.Length)];
    }

    [GameEventHandler]
    public HookResult OnBotItemPickup(EventItemPickup @event, GameEventInfo info)
    {
        var bot = @event.Userid;
        var item = (@event.Item ?? "").Replace("weapon_", "");
        if (!_botNoSnipers || bot == null || !bot.IsValid || !SwappableRifles.Contains(item) || _botRifles.Contains(item) || !IsBotPlayer(bot))
            return HookResult.Continue;

        Server.NextFrame(() =>
        {
            if (!bot.IsValid || !bot.PawnIsAlive) return;
            Utilities.RemoveItemByDesignerName(bot, "weapon_" + item);
            // Don't hand out a second rifle if it already carries an allowed one.
            if (FindPlayerWeapon(bot, w => _botRifles.Contains(w.DesignerName.Replace("weapon_", ""))) == null)
                bot.GiveNamedItem(BotRifleFor(_botRifles, bot.Team == CsTeam.Terrorist));
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
        Server.NextFrame(() =>
        {
            var pawn = bot.IsValid ? bot.PlayerPawn.Value : null;
            if (pawn is not { IsValid: true } || pawn.AbsVelocity is not { } v || v.Z <= 0) return;
            pawn.Teleport(null, null, new Vector(v.X * 0.8f, v.Y * 0.8f, 0));
        });
        return HookResult.Continue;
    }
}
