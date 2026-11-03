using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace LanSkinTester;

/// <summary>
/// Hardest bots: lst_bot_hunt 1 = bots deal no damage to other bots, so every fight is with a person.
/// lst_bot_aimbot 0-100 = a bot that can see a person turns its view onto that person's head every tick
/// (100 snaps at once, lower turns part of the way). "Can see" is CS2's own spotted state, so no aiming
/// through walls. lst_bot_reaction ms = how long a bot must see you before it aims (your window to shoot
/// first). lst_bot_onetap 1 = any bullet from a bot that hits a person kills.
/// </summary>
public partial class LanSkinTester
{
    private bool _botHunt;
    private int _botAimbot;
    private bool _botHuntListening;
    private bool _botOneTap;
    private int _botReactionMs;
    /// <summary>Bot slot: when it started seeing the person it aims at (server time).</summary>
    private readonly Dictionary<int, float> _botSeenSince = new();
    /// <summary>Off from a map change until the new map has run a few seconds: pawns are being torn down and made.</summary>
    private bool _botAimLive = true;

    [ConsoleCommand("lst_bot_onetap", "1 = a bot's bullet that hits a person kills")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotOneTapCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _botOneTap = cmd.GetArg(1) == "1";
        ListenForBotHunt();
    }

    [ConsoleCommand("lst_bot_reaction", "Milliseconds a bot must see a person before the aimbot turns it (0 = at once)")]
    [CommandHelper(minArgs: 1, usage: "0-2000", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotReactionCommand(CCSPlayerController? caller, CommandInfo cmd) =>
        _botReactionMs = int.TryParse(cmd.GetArg(1), out var v) ? Math.Clamp(v, 0, 2000) : 0;

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
        RegisterListener<Listeners.OnMapEnd>(() => _botAimLive = false);
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _botAimLive = false;
            _botSeenSince.Clear();
            AddTimer(5.0f, () => _botAimLive = true, TimerFlags.STOP_ON_MAPCHANGE);
        });
    }

    private HookResult OnBotHuntDamage(CBaseEntity victim, CTakeDamageInfo damage)
    {
        if ((!_botHunt && !_botOneTap) || victim.DesignerName != "player") return HookResult.Continue;
        try
        {
            var attacker = damage.Attacker.Value;
            if (attacker == null || !attacker.IsValid || attacker.DesignerName != "player" || !PawnIsBot(attacker))
                return HookResult.Continue;
            if (PawnIsBot(victim))
            {
                if (!_botHunt) return HookResult.Continue;
                damage.Damage = 0;
                return HookResult.Handled;
            }
            if (!_botOneTap || !IsOneTapWeapon(damage.Ability.Value?.DesignerName ?? damage.Inflictor.Value?.DesignerName))
                return HookResult.Continue;
            damage.Damage = 1000; // through any armor
            return HookResult.Changed;
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
        if (_botAimbot == 0 || !_botAimLive) return;
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
            if (best is not { } aim)
            {
                _botSeenSince.Remove(bot.Slot);
                continue;
            }
            // A time from the last map (the clock restarts) counts as not seen yet.
            var time = Server.CurrentTime;
            var since = _botSeenSince.TryGetValue(bot.Slot, out var seen) && seen <= time ? seen : _botSeenSince[bot.Slot] = time;
            if (!Reacted(since, time, _botReactionMs)) continue;
            var (pitch, yaw) = AimAngles(eye, aim);
            var view = pawn.EyeAngles;
            var t = _botAimbot / 100f;
            var (newPitch, newYaw) = (Approach(view.X, pitch, t), Approach(view.Y, yaw, t));
            if (MathF.Abs(newPitch - view.X) < 0.05f && MathF.Abs(newYaw - view.Y) < 0.05f) continue; // already on target
            pawn.Teleport(null, new QAngle(newPitch, newYaw, 0), null);
        }
    }

    /// <summary>Guns only: not knives, grenades or fire.</summary>
    internal static bool IsOneTapWeapon(string? classname) =>
        classname != null && classname.StartsWith("weapon_") && !ItemData.IsKnifeClassname(classname) &&
        classname is not ("weapon_hegrenade" or "weapon_molotov" or "weapon_incgrenade" or "weapon_taser");

    internal static bool Reacted(float seenSince, float now, int reactionMs) => (now - seenSince) * 1000 >= reactionMs;

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
