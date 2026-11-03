using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Hardest bots: lst_bot_hunt 1 = bots deal no damage to other bots, so every fight is with a person.
/// lst_bot_aimbot 0-100 = a bot that can see a person turns its view onto that person's head every tick
/// (100 snaps at once, lower turns part of the way). "Can see" is CS2's own spotted state, so no aiming
/// through walls.
/// </summary>
public partial class LanSkinTester
{
    private bool _botHunt;
    private int _botAimbot;
    private bool _botHuntListening;

    [ConsoleCommand("lst_bot_hunt", "1 = bots can't hurt other bots, only people")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotHuntCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _botHunt = cmd.GetArg(1) == "1";
        ListenForBotHunt();
    }

    [ConsoleCommand("lst_bot_aimbot", "0-100: how hard bots snap their aim onto a person's head they can see (0 = off)")]
    [CommandHelper(minArgs: 1, usage: "0-100", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotAimbotCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _botAimbot = int.TryParse(cmd.GetArg(1), out var v) ? Math.Clamp(v, 0, 100) : 0;
        ListenForBotHunt();
    }

    private void ListenForBotHunt()
    {
        if (_botHuntListening) return;
        _botHuntListening = true;
        RegisterListener<Listeners.OnEntityTakeDamagePre>(OnBotHuntDamage);
        RegisterListener<Listeners.OnTick>(OnBotAimbotTick);
    }

    private HookResult OnBotHuntDamage(CBaseEntity victim, CTakeDamageInfo damage)
    {
        if (!_botHunt || victim.DesignerName != "player") return HookResult.Continue;
        try
        {
            var attacker = damage.Attacker.Value;
            if (attacker == null || !attacker.IsValid || attacker.DesignerName != "player") return HookResult.Continue;
            if (!PawnIsBot(attacker) || !PawnIsBot(victim)) return HookResult.Continue;
            damage.Damage = 0;
            return HookResult.Handled;
        }
        catch (Exception)
        {
            return HookResult.Continue; // unknown damage layout: never break the game
        }
    }

    private static bool PawnIsBot(CBaseEntity pawn) =>
        pawn.As<CCSPlayerPawn>().Controller.Value?.As<CCSPlayerController>() is { IsValid: true } c && IsBotPlayer(c);

    private void OnBotAimbotTick()
    {
        if (_botAimbot == 0) return;
        var players = Utilities.GetPlayers();
        var ffa = CvarInt("mp_teammates_are_enemies") != 0;
        foreach (var bot in players)
        {
            if (!bot.IsValid || !bot.PawnIsAlive || !IsBotPlayer(bot)) continue;
            var pawn = bot.PlayerPawn.Value;
            if (pawn is not { IsValid: true } || pawn.AbsOrigin is not { } origin) continue;
            (float X, float Y, float Z) eye = (origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z);
            (float X, float Y, float Z)? best = null;
            var bestDistance = float.MaxValue;
            foreach (var person in players)
            {
                if (!person.IsValid || !person.PawnIsAlive || !IsHumanPlayer(person)) continue;
                if (!ffa && person.Team == bot.Team) continue;
                var target = person.PlayerPawn.Value;
                if (target is not { IsValid: true } || target.AbsOrigin is not { } at) continue;
                if (!SeenBy(target.EntitySpottedState.SpottedByMask, bot.Slot)) continue;
                (float X, float Y, float Z) head = (at.X, at.Y, at.Z + target.ViewOffset.Z);
                var d = (head.X - eye.X) * (head.X - eye.X) + (head.Y - eye.Y) * (head.Y - eye.Y) + (head.Z - eye.Z) * (head.Z - eye.Z);
                if (d < bestDistance) (best, bestDistance) = (head, d);
            }
            if (best is not { } aim) continue;
            var (pitch, yaw) = AimAngles(eye, aim);
            var now = pawn.EyeAngles;
            var t = _botAimbot / 100f;
            pawn.Teleport(null, new QAngle(Approach(now.X, pitch, t), Approach(now.Y, yaw, t), 0), null);
        }
    }

    /// <summary>The spotted mask has one bit per player slot, in 32-bit words.</summary>
    internal static bool SeenBy(ReadOnlySpan<uint> mask, int slot) =>
        slot >= 0 && slot / 32 < mask.Length && (mask[slot / 32] & (1u << (slot % 32))) != 0;

    /// <summary>Pitch (down is positive) and yaw, in degrees, to look from <paramref name="from"/> at <paramref name="to"/>.</summary>
    internal static (float Pitch, float Yaw) AimAngles((float X, float Y, float Z) from, (float X, float Y, float Z) to)
    {
        var (dx, dy, dz) = (to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        var flat = MathF.Sqrt(dx * dx + dy * dy);
        return (-MathF.Atan2(dz, flat) * 180 / MathF.PI, MathF.Atan2(dy, dx) * 180 / MathF.PI);
    }

    /// <summary>Turns <paramref name="t"/> (0-1) of the way from one angle to another, the short way round.</summary>
    internal static float Approach(float from, float to, float t)
    {
        var diff = ((to - from) % 360 + 540) % 360 - 180;
        return from + diff * t;
    }
}
