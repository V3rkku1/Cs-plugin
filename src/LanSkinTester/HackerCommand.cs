using System.Runtime.InteropServices;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// Silent aim and the triggerbot edit the command the server is about to simulate. Writing the pawn
/// earlier is overwritten by that command, so the bullet never used the new angles or the attack button.
/// </summary>
public partial class LanSkinTester
{
    private MemoryFunctionVoid<CCSPlayerController, nint, int, bool, float>? _processUsercmds;
    private bool _commandHookLive;

    /// <summary>The command hook is installed and has found where the view angles live.</summary>
    private bool CommandAimReady => _commandHookLive && _commandLayout != null && !_commandAimBroken;
    private bool _commandAimBroken;
    private bool _commandLayoutLogged;
    private int _commandStride;
    private CmdLayout? _commandLayout;

    /// <summary>Where the view angles and buttons sit inside the command. Filled in once they match the pawn.</summary>
    private readonly record struct CmdLayout(
        int CmdBase, int BasePtr, int ViewPtr, int AngleFloat, int ButtonsPtr, int ButtonWord, int HistoryField, int Attack1);

    /// <summary>Likely layouts of the server command (vtable protobuf, then the client-shaped command).</summary>
    private static readonly CmdLayout[] LayoutCandidates =
    [
        new(0x10, 0x30, 0x40, 0x18, 0x38, 0x18, 0x18, 0x3C),
        new(0x10, 0x30, 0x48, 0x18, 0x40, 0x18, 0x18, 0x3C),
        new(0x10, 0x28, 0x48, 0x18, 0x40, 0x18, 0x08, 0x34),
        new(0x10, 0x28, 0x40, 0x18, 0x38, 0x18, 0x08, 0x34),
        new(0x18, 0x28, 0x48, 0x18, 0x40, 0x18, 0x08, 0x34),
        new(0x18, 0x30, 0x40, 0x18, 0x38, 0x18, 0x18, 0x3C),
    ];

    private static readonly int[] StrideCandidates = [0x88, 0x90, 0x98, 0xA0, 0xA8, 0xB0, 0xB8, 0xC0, 0xC8, 0xD0, 0xE0, 0xF0, 0x100];

    private void LoadCommandAim()
    {
        const string key = "CCSPlayerController_ProcessUsercmds";
        try
        {
            var path = Path.Combine(ModuleDirectory, "gamedata", "lanskintester.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
            var signature = doc.RootElement.GetProperty(key).GetProperty("signatures").GetProperty(os).GetString();
            if (string.IsNullOrWhiteSpace(signature)) throw new InvalidDataException($"empty {os} signature");

            _processUsercmds = new MemoryFunctionVoid<CCSPlayerController, nint, int, bool, float>(signature, Addresses.ServerPath);
            _processUsercmds.Hook(OnProcessUsercmds, HookMode.Pre);
            _processUsercmds.Hook(OnProcessUsercmdsPost, HookMode.Post);
            _commandHookLive = true;
            Logger.LogInformation("Silent aim and triggerbot will edit the player command before it is simulated");
        }
        catch (Exception ex)
        {
            _commandHookLive = false;
            Logger.LogWarning(ex, "Could not hook player commands. Silent aim and the triggerbot stay on the pawn path.");
        }
    }

    private void UnloadCommandAim()
    {
        if (_processUsercmds == null) return;
        try
        {
            _processUsercmds.Unhook(OnProcessUsercmds, HookMode.Pre);
            _processUsercmds.Unhook(OnProcessUsercmdsPost, HookMode.Post);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Unhook player commands");
        }
        _processUsercmds = null;
        _commandHookLive = false;
    }

    private HookResult OnProcessUsercmds(DynamicHook hook)
    {
        if (!_commandHookLive || _commandAimBroken) return HookResult.Continue;
        try
        {
            var hacker = hook.GetParam<CCSPlayerController>(0);
            var cmds = hook.GetParam<nint>(1);
            var count = hook.GetParam<int>(2);
            if (hacker is not { IsValid: true, PawnIsAlive: true } || !IsHacker(hacker)) return HookResult.Continue;
            if (cmds == 0 || count is < 1 or > 64) return HookResult.Continue;
            var pawn = hacker.PlayerPawn.Value;
            if (pawn is not { IsValid: true }) return HookResult.Continue;
            if (!TryReadView(pawn, out var pawnPitch, out var pawnYaw)) return HookResult.Continue;

            if (_commandLayout is not { } layout)
            {
                if (!TryLockCommandLayout(cmds, pawnPitch, pawnYaw)) return HookResult.Continue;
                layout = _commandLayout!.Value;
            }

            var stride = count == 1 ? 0 : EnsureCommandStride(cmds, count, layout, pawnPitch, pawnYaw);
            var lastIndex = stride == 0 ? 0 : count - 1;
            var last = cmds + lastIndex * (stride == 0 ? 0 : stride);
            if (!TryReadCommandAngles(last, layout, out var pitch, out var yaw)) return HookResult.Continue;

            var menu = _cheatMenus.ContainsKey(hacker.Slot);
            var triggerFire = !menu && Has(hacker, Power.Trigger) && TriggerbotAt(hacker, pawn, pitch, yaw);
            if (menu || !Has(hacker, Power.Trigger)) _triggers.Remove(hacker.Slot);

            var commandAttack = CommandAttacking(last, layout);
            var fire = triggerFire;
            var replaceAngles = false;
            if (!menu && Has(hacker, Power.Aimbot) && AimPressed(hacker))
            {
                fire |= Aim(hacker, pawn, triggerFire, pitch, yaw, commandAttack);
                replaceAngles = _pendingAim.ContainsKey(hacker.Slot);
            }
            else
            {
                _aimLocks.Remove(hacker.Slot);
                _pendingAim.Remove(hacker.Slot);
            }

            var attacking = fire || CommandAttacking(last, layout);
            if (!replaceAngles && !attacking) 
            {
                SetForcedAttack(hacker, false);
                return HookResult.Continue;
            }

            var aimPitch = pitch;
            var aimYaw = yaw;
            if (replaceAngles && _pendingAim.TryGetValue(hacker.Slot, out var aim))
            {
                aimPitch = aim.Pitch;
                aimYaw = aim.Yaw;
            }

            var steps = stride == 0 ? 1 : count;
            for (var i = 0; i < steps; i++)
            {
                var cmd = cmds + i * (stride == 0 ? 0 : stride);
                if (replaceAngles) WriteCommandAngles(cmd, layout, aimPitch, aimYaw);
                if (attacking && (fire || CommandAttacking(cmd, layout))) WriteCommandAttack(cmd, layout);
            }
            SetForcedAttack(hacker, fire);
        }
        catch (Exception ex)
        {
            _commandAimBroken = true;
            Logger.LogWarning(ex, "Player command aim failed. Silent aim and the triggerbot are off until the plugin is updated.");
        }
        return HookResult.Continue;
    }

    /// <summary>The shot already used the command. Put a silent aimer's own view back on the pawn.</summary>
    private HookResult OnProcessUsercmdsPost(DynamicHook hook)
    {
        if (_silentRestore.Count == 0) return HookResult.Continue;
        try
        {
            var hacker = hook.GetParam<CCSPlayerController>(0);
            if (hacker is not { IsValid: true }) return HookResult.Continue;
            if (!_silentRestore.TryGetValue(hacker.Slot, out var angles)) return HookResult.Continue;
            var pawn = hacker.PlayerPawn.Value;
            if (pawn is { IsValid: true }) WriteView(pawn, angles.Pitch, angles.Yaw);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Restore silent view");
        }
        return HookResult.Continue;
    }

    private bool TryLockCommandLayout(nint cmd, float pawnPitch, float pawnYaw)
    {
        foreach (var candidate in LayoutCandidates)
        {
            try
            {
                if (!TryReadCommandAngles(cmd, candidate, out var pitch, out var yaw)) continue;
                if (Math.Abs(AngleDiff(pitch, pawnPitch)) > 0.85f || Math.Abs(AngleDiff(yaw, pawnYaw)) > 0.85f) continue;
                if (!CommandButtonsLookReal(cmd, candidate)) continue;
                _commandLayout = candidate;
                if (!_commandLayoutLogged)
                {
                    _commandLayoutLogged = true;
                    Logger.LogInformation("Player command layout matched the live view (base +{Base:X}, angles +{Angles:X})",
                        candidate.BasePtr, candidate.ViewPtr);
                }
                return true;
            }
            catch (Exception)
            {
                // This guess pointed at unreadable memory. Try the next one.
            }
        }
        return false;
    }

    private int EnsureCommandStride(nint cmds, int count, CmdLayout layout, float pitch, float yaw)
    {
        if (_commandStride != 0) return _commandStride;
        var best = 0;
        var bestScore = 12f;
        foreach (var stride in StrideCandidates)
        {
            try
            {
                if (!TryReadCommandAngles(cmds + stride, layout, out var nextPitch, out var nextYaw)) continue;
                if (nextPitch is < -89.5f or > 89.5f || !float.IsFinite(nextYaw)) continue;
                var score = Math.Abs(AngleDiff(nextPitch, pitch)) + Math.Abs(AngleDiff(nextYaw, yaw));
                if (score >= bestScore) continue;
                bestScore = score;
                best = stride;
            }
            catch (Exception)
            {
                // Not this spacing.
            }
        }
        if (best != 0) _commandStride = best;
        return _commandStride;
    }

    private static bool TryReadCommandAngles(nint cmd, CmdLayout layout, out float pitch, out float yaw)
    {
        pitch = yaw = 0;
        if (!TryBase(cmd, layout, out var view)) return false;
        pitch = ReadFloat(view + layout.AngleFloat);
        yaw = ReadFloat(view + layout.AngleFloat + 4);
        return float.IsFinite(pitch) && float.IsFinite(yaw) && pitch is >= -89.5f and <= 89.5f;
    }

    private static void WriteCommandAngles(nint cmd, CmdLayout layout, float pitch, float yaw)
    {
        if (!TryBase(cmd, layout, out var view)) return;
        WriteQAngle(view, layout.AngleFloat, pitch, yaw);
        if (TryPointer(cmd, layout, layout.BasePtr, out var baseCmd)) MarkHasBit(baseCmd + 0x10, 0x4);
        WriteHistoryAngles(cmd, layout, pitch, yaw);
        ClearSubtickAngleDeltas(cmd, layout);
    }

    private static void WriteHistoryAngles(nint cmd, CmdLayout layout, float pitch, float yaw)
    {
        if (!TryPointer(cmd, layout, layout.BasePtr, out _)) return;
        var obj = cmd + layout.CmdBase;
        var field = obj + layout.HistoryField;
        var count = Marshal.ReadInt32(field + 8);
        if (count is < 1 or > 24) return;
        var rep = Marshal.ReadIntPtr(field + 0x10);
        if (!Plausible(rep)) return;
        for (var i = 0; i < count; i++)
        {
            var entry = Marshal.ReadIntPtr(rep + 8 + i * IntPtr.Size);
            if (!Plausible(entry)) continue;
            var angles = Marshal.ReadIntPtr(entry + 0x18);
            if (!Plausible(angles)) continue;
            var oldPitch = ReadFloat(angles + layout.AngleFloat);
            if (oldPitch is < -89.5f or > 89.5f) continue;
            WriteQAngle(angles, layout.AngleFloat, pitch, yaw);
        }
    }

    private static void ClearSubtickAngleDeltas(nint cmd, CmdLayout layout)
    {
        if (!TryPointer(cmd, layout, layout.BasePtr, out var baseCmd)) return;
        var field = baseCmd + 0x18;
        var count = Marshal.ReadInt32(field + 8);
        if (count is < 1 or > 24) return;
        var rep = Marshal.ReadIntPtr(field + 0x10);
        if (!Plausible(rep)) return;
        for (var i = 0; i < count; i++)
        {
            var step = Marshal.ReadIntPtr(rep + 8 + i * IntPtr.Size);
            if (!Plausible(step)) continue;
            var pitch = ReadFloat(step + 0x30);
            var yaw = ReadFloat(step + 0x34);
            if (!float.IsFinite(pitch) || !float.IsFinite(yaw) || Math.Abs(pitch) > 180f || Math.Abs(yaw) > 180f) continue;
            WriteFloat(step + 0x30, 0);
            WriteFloat(step + 0x34, 0);
        }
    }

    private static bool CommandAttacking(nint cmd, CmdLayout layout)
    {
        if (!TryButtonWord(cmd, layout, out var word)) return false;
        return (word & (ulong)PlayerButtons.Attack) != 0;
    }

    private static void WriteCommandAttack(nint cmd, CmdLayout layout)
    {
        if (TryButtonAddress(cmd, layout, out var word))
        {
            var bits = (ulong)Marshal.ReadInt64(word);
            Marshal.WriteInt64(word, (long)(bits | (ulong)PlayerButtons.Attack));
            var changed = (ulong)Marshal.ReadInt64(word + 8);
            Marshal.WriteInt64(word + 8, (long)(changed | (ulong)PlayerButtons.Attack));
            MarkHasBit(word - layout.ButtonWord + 0x10, 0x7);
        }

        var obj = cmd + layout.CmdBase;
        var history = Marshal.ReadInt32(obj + layout.HistoryField + 8);
        var index = history is > 0 and <= 24 ? history - 1 : 0;
        Marshal.WriteInt32(obj + layout.Attack1, index);
        MarkHasBit(obj + 0x10, 0x20);
    }

    private static bool CommandButtonsLookReal(nint cmd, CmdLayout layout)
    {
        if (!TryButtonWord(cmd, layout, out var word)) return false;
        const ulong known = (1ul << 36) - 1;
        return (word & ~known) == 0;
    }

    private static bool TryButtonWord(nint cmd, CmdLayout layout, out ulong word)
    {
        word = 0;
        if (!TryButtonAddress(cmd, layout, out var address)) return false;
        word = (ulong)Marshal.ReadInt64(address);
        return true;
    }

    private static bool TryButtonAddress(nint cmd, CmdLayout layout, out nint word)
    {
        word = 0;
        if (!TryPointer(cmd, layout, layout.BasePtr, out var baseCmd)) return false;
        var buttons = Marshal.ReadIntPtr(baseCmd + layout.ButtonsPtr);
        if (!Plausible(buttons)) return false;
        word = buttons + layout.ButtonWord;
        return true;
    }

    private static bool TryBase(nint cmd, CmdLayout layout, out nint view)
    {
        view = 0;
        if (!TryPointer(cmd, layout, layout.BasePtr, out var baseCmd)) return false;
        view = Marshal.ReadIntPtr(baseCmd + layout.ViewPtr);
        return Plausible(view);
    }

    private static bool TryPointer(nint cmd, CmdLayout layout, int field, out nint value)
    {
        value = 0;
        if (cmd == 0) return false;
        value = Marshal.ReadIntPtr(cmd + layout.CmdBase + field);
        return Plausible(value);
    }

    private static void WriteQAngle(nint angles, int floatOffset, float pitch, float yaw)
    {
        WriteFloat(angles + floatOffset, pitch);
        WriteFloat(angles + floatOffset + 4, yaw);
        WriteFloat(angles + floatOffset + 8, 0);
        MarkHasBit(angles + floatOffset - 8, 0x7);
    }

    private static void MarkHasBit(nint address, int bits)
    {
        var current = Marshal.ReadInt32(address);
        if (current is < 0 or > 0x7FFFF) return;
        Marshal.WriteInt32(address, current | bits);
    }

    private static bool Plausible(nint pointer)
    {
        var value = (ulong)pointer;
        if ((value & 7) != 0 || value < 0x10000) return false;
        return value <= 0x00007FFFFFFFFFFF || value >= 0xFFFF800000000000;
    }

    private static float ReadFloat(nint address) => BitConverter.Int32BitsToSingle(Marshal.ReadInt32(address));

    private static void WriteFloat(nint address, float value) => Marshal.WriteInt32(address, BitConverter.SingleToInt32Bits(value));
}
