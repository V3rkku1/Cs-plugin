using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

/// <summary>
/// In-game cheat menu drawn with world text (point_worldtext): a board that appears in front of you,
/// visible only to you. Unlike the center-screen HTML panel it doesn't fade or flicker, and it only
/// changes when you press a key. Driven with the movement keys: W / S move, D or E switches (or turns
/// a slider up), A turns it down, R closes. Open with !cheats (or bind a key: bind F3 css_cheats).
/// You stand still while it's open.
/// </summary>
public partial class LanSkinTester
{
    private sealed class CheatMenuState
    {
        public int Cursor;
        public PlayerButtons LastButtons;
        public MoveType_t PreviousMoveType = MoveType_t.MOVETYPE_WALK;
        public CPointWorldText? Title;
        public CPointWorldText? Body;
        public string Shown = "";
    }

    private sealed record MenuItem(string Label, Func<CCSPlayerController, string> Value, Action<CCSPlayerController, bool> Activate);

    private readonly Dictionary<int, CheatMenuState> _cheatMenus = new();
    private readonly HashSet<int> _thirdPerson = new();
    private List<MenuItem> _menuItems = null!;

    // Board placement and size (world units). The board stands MenuDistance in front of the eyes.
    private const float MenuDistance = 48f;
    private const float MenuUnitsPerPx = 0.065f;
    private const float MenuFontSize = 26f;
    private const int MenuWindow = 9;

    private void LoadCheatMenu()
    {
        _menuItems =
        [
            Toggle("Chams", Power.Wallhack),
            Toggle("Box ESP", Power.BoxEsp),
            Toggle("Skeleton ESP", Power.BoneEsp),
            Toggle("Name tags", Power.Tags),
            Toggle("Enemy info", Power.EspInfo),
            Toggle("Radar", Power.Radar),
            Toggle("Aimbot", Power.Aimbot),
            Toggle("Aim through walls", Power.AimWalls),
            new MenuItem("Aim style", p => AimStyleNames[(int)AimStyleOf(p)], (p, up) =>
                _aimStyles[p.Slot] = Cycle(AimStyleOf(p), AimStyleNames.Length, up)),
            new MenuItem("Aim bone", p => AimBoneNames[(int)AimBoneOf(p)], (p, up) =>
                _aimBones[p.Slot] = Cycle(AimBoneOf(p), AimBoneNames.Length, up)),
            new MenuItem("Aggression", p => Bar(AggressionOf(p)), (p, up) =>
                _aggression[p.Slot] = Math.Clamp(AggressionOf(p) + (up ? 1 : -1), 1, 10)),
            new MenuItem("Aim key", p => AimKeyNames[(int)AimKeyOf(p)], (p, up) =>
                _aimKeys[p.Slot] = Cycle(AimKeyOf(p), AimKeyNames.Length, up)),
            new MenuItem("Aim target", p => AimTargetNames[(int)AimTargetOf(p)], (p, up) =>
                _aimTargets[p.Slot] = Cycle(AimTargetOf(p), AimTargetNames.Length, up)),
            Toggle("Triggerbot", Power.Trigger),
            Toggle("Recoil control", Power.NoRecoil),
            Toggle("Speedhack", Power.Speed),
            Toggle("Bunnyhop", Power.Bhop),
            Toggle("Infinite ammo", Power.Ammo),
            Toggle("No flash", Power.NoFlash),
            Toggle("Damage boost", Power.OneTap),
            new MenuItem("Third person", p => OnOff(_thirdPerson.Contains(p.Slot)), (p, _) =>
            {
                var on = _thirdPerson.Add(p.Slot);
                if (!on) _thirdPerson.Remove(p.Slot);
                if (on && ConVar.Find("sv_cheats")?.GetPrimitiveValue<bool>() != true) Server.ExecuteCommand("sv_cheats 1");
                p.ExecuteClientCommandFromServer(on ? "thirdperson" : "firstperson");
            }),
            new MenuItem("Everything off", _ => "", (p, _) => SetPowers(p, Power.None)),
            new MenuItem("Close", _ => "", (p, _) => CloseCheatMenu(p)),
        ];
    }

    /// <summary>A text slider: [■■■■■□□□□□] 5</summary>
    private static string Bar(int level) => "[" + new string('■', level) + new string('□', 10 - level) + $"] {level}";

    private static string OnOff(bool on) => on ? "ON" : "off";

    private static T Cycle<T>(T current, int count, bool up) where T : struct, Enum
    {
        var next = ((Convert.ToInt32(current) + (up ? 1 : -1)) + count) % count;
        return (T)Enum.ToObject(typeof(T), next);
    }

    private MenuItem Toggle(string label, Power power) =>
        new(label, p => OnOff(Has(p, power)), (p, _) => SetPowers(p, PowersOf(p) ^ power));

    private void SetPowers(CCSPlayerController player, Power powers)
    {
        var before = PowersOf(player);
        _personal[player.Slot] = powers;
        if (((before ^ powers) & Power.Wallhack) != 0) RebuildGlows();
        if (((before ^ powers) & Power.Speed) != 0) ResetSpeed();
        if ((powers & Power.Aimbot) == 0) _aimLocks.Remove(player.Slot);
    }

    [ConsoleCommand("css_cheats", "Open the cheat menu (W/S move, D/E switch, A back, R close)")]
    public void OnCheatsCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !IsHumanPlayer(player)) return;
        if (_cheatMenus.ContainsKey(player.Slot))
        {
            CloseCheatMenu(player);
            return;
        }
        if (_hackMode == HackMode.OneRandom && !IsHacker(player))
        {
            Reply(player, "Only this round's hacker gets the cheat menu. Catch them!");
            return;
        }
        if (_hackMode == HackMode.Off)
        {
            Reply(player, "The cheat menu only works in hacker mode. The host turns it on in the launcher (Play page, Hacker Mode).");
            return;
        }
        if (!player.PawnIsAlive || player.PlayerPawn.Value is not { IsValid: true } pawn)
        {
            Reply(player, "Open the cheat menu while you're alive.");
            return;
        }

        var state = new CheatMenuState { LastButtons = player.Buttons };
        // Stand still while the movement keys drive the menu (and so the board stays in front of you).
        state.PreviousMoveType = pawn.MoveType;
        pawn.MoveType = pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        pawn.Teleport(null, null, new Vector(0, 0, 0));

        _cheatMenus[player.Slot] = state;
        if (!CreateBoard(player, pawn, state))
        {
            CloseCheatMenu(player);
            Reply(player, "Could not create the menu board on this map.");
            return;
        }
        Reply(player, "Cheat menu open in front of you: W/S move, D switch / up, A down, R close.");
    }

    private void CloseCheatMenu(CCSPlayerController player)
    {
        if (!_cheatMenus.Remove(player.Slot, out var state)) return;
        if (state.Title is { IsValid: true }) state.Title.Remove();
        if (state.Body is { IsValid: true }) state.Body.Remove();
        if (player.IsValid && player.PlayerPawn.Value is { IsValid: true } pawn && pawn.MoveType == MoveType_t.MOVETYPE_NONE)
        {
            pawn.MoveType = pawn.ActualMoveType = state.PreviousMoveType == MoveType_t.MOVETYPE_NONE ? MoveType_t.MOVETYPE_WALK : state.PreviousMoveType;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }
        if (player.IsValid)
        {
            Reply(player, $"Cheats: {DescribePowers(player)}");
            // Walls may have been switched on for someone who wasn't a hacker before.
            if (GlowsNeeded && _glows.Count == 0) RebuildGlows();
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The board
    // ---------------------------------------------------------------------------------------------

    private bool CreateBoard(CCSPlayerController player, CCSPlayerPawn pawn, CheatMenuState state)
    {
        if (pawn.AbsOrigin is not { } origin) return false;
        try
        {
            // Straight ahead (ignoring up/down look), at eye height, facing the player.
            TryReadView(pawn, out _, out var yaw);
            var rad = yaw * Math.PI / 180;
            var forward = new Vector((float)Math.Cos(rad), (float)Math.Sin(rad), 0);
            var right = new Vector((float)Math.Sin(rad), (float)-Math.Cos(rad), 0);
            var eyeZ = origin.Z + EyeHeight(pawn);
            var left = new Vector(
                origin.X + forward.X * MenuDistance - right.X * 16f,
                origin.Y + forward.Y * MenuDistance - right.Y * 16f,
                eyeZ);
            var facing = new QAngle(0, yaw + 270, 90);

            state.Title = CreateText("H4X MENU", Color.FromArgb(255, 57, 255, 20), MenuFontSize * 1.25f);
            state.Body = CreateText(RenderBody(player, state), Color.FromArgb(255, 235, 235, 235), MenuFontSize);
            if (state.Title == null || state.Body == null) return false;
            state.Shown = state.Body.MessageText;

            state.Title.Teleport(new Vector(left.X, left.Y, left.Z + 11f), facing, null);
            state.Body.Teleport(new Vector(left.X, left.Y, left.Z + 7.5f), facing, null);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not create the cheat menu board");
            return false;
        }
    }

    private static CPointWorldText? CreateText(string text, Color color, float fontSize)
    {
        var entity = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (entity == null) return null;
        entity.MessageText = text;
        entity.Enabled = true;
        entity.FontSize = fontSize;
        entity.FontName = "Arial Bold";
        entity.Fullbright = true;
        entity.WorldUnitsPerPx = MenuUnitsPerPx;
        entity.Color = color;
        entity.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_LEFT;
        entity.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_TOP;
        entity.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_NONE;
        entity.DispatchSpawn();
        return entity;
    }

    private string RenderBody(CCSPlayerController player, CheatMenuState state)
    {
        var lines = new List<string>();
        var first = Math.Clamp(state.Cursor - MenuWindow / 2, 0, Math.Max(0, _menuItems.Count - MenuWindow));
        var last = Math.Min(_menuItems.Count, first + MenuWindow);
        lines.Add(first > 0 ? "    ▲" : " ");
        for (var i = first; i < last; i++)
        {
            var item = _menuItems[i];
            var value = item.Value(player);
            lines.Add((i == state.Cursor ? "► " : "    ") + item.Label + (value != "" ? "   " + value : ""));
        }
        lines.Add(last < _menuItems.Count ? "    ▼" : " ");
        lines.Add("W/S move · D/A change · R close");
        return string.Join("\n", lines);
    }

    /// <summary>Called every tick from the hacker-mode tick: keys in, text out only when it changed.</summary>
    private void TickCheatMenus()
    {
        if (_cheatMenus.Count == 0) return;
        foreach (var slot in _cheatMenus.Keys.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player == null || !player.IsValid || !player.PawnIsAlive)
            {
                if (player != null) CloseCheatMenu(player);
                else if (_cheatMenus.Remove(slot, out var gone))
                {
                    if (gone.Title is { IsValid: true }) gone.Title.Remove();
                    if (gone.Body is { IsValid: true }) gone.Body.Remove();
                }
                continue;
            }

            var state = _cheatMenus[slot];
            var buttons = player.Buttons;
            var pressed = buttons & ~state.LastButtons;
            state.LastButtons = buttons;
            if (pressed == 0) continue;

            if ((pressed & PlayerButtons.Reload) != 0)
            {
                CloseCheatMenu(player);
                continue;
            }
            if ((pressed & PlayerButtons.Forward) != 0) state.Cursor = (state.Cursor - 1 + _menuItems.Count) % _menuItems.Count;
            if ((pressed & PlayerButtons.Back) != 0) state.Cursor = (state.Cursor + 1) % _menuItems.Count;
            if ((pressed & (PlayerButtons.Moveright | PlayerButtons.Use)) != 0) _menuItems[state.Cursor].Activate(player, true);
            else if ((pressed & PlayerButtons.Moveleft) != 0) _menuItems[state.Cursor].Activate(player, false);
            if (!_cheatMenus.ContainsKey(slot)) continue; // "Close" was picked

            var text = RenderBody(player, state);
            if (text != state.Shown && state.Body is { IsValid: true } body)
            {
                body.AcceptInput("SetMessage", body, body, text);
                state.Shown = text;
            }
        }
    }

    /// <summary>Only the menu's owner receives its board.</summary>
    private void HideOtherMenus(CCheckTransmitInfo info, CCSPlayerController? viewer)
    {
        foreach (var (slot, state) in _cheatMenus)
        {
            if (viewer != null && viewer.Slot == slot) continue;
            if (state.Title is { IsValid: true }) info.TransmitEntities.Remove(state.Title);
            if (state.Body is { IsValid: true }) info.TransmitEntities.Remove(state.Body);
        }
    }
}
