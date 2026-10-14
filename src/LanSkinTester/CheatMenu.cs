using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;

namespace LanSkinTester;

/// <summary>
/// In-game cheat menu on CS2's center-screen HTML panel, driven with the movement keys:
/// W / S move, D or E switches, A switches back, R closes. Open it with !cheats
/// (or bind a key: bind F3 css_cheats). You stand still while it's open.
/// </summary>
public partial class LanSkinTester
{
    private sealed class CheatMenuState
    {
        public int Cursor;
        public PlayerButtons LastButtons;
        public MoveType_t PreviousMoveType = MoveType_t.MOVETYPE_WALK;
        public bool Dirty = true;
        public string Html = "";
    }

    private sealed record MenuItem(string Label, Func<CCSPlayerController, string> Value, Action<CCSPlayerController, bool> Activate);

    private readonly Dictionary<int, CheatMenuState> _cheatMenus = new();
    private readonly HashSet<int> _thirdPerson = new();
    private List<MenuItem> _menuItems = null!;

    private static readonly float[] FovChoices = [10f, 20f, 35f];
    private static readonly (string Name, float Value)[] SmoothChoices = [("Strong", 0.10f), ("Medium", 0.18f), ("Light", 0.35f)];
    // CS2's center panel drops messages that are too long, so the menu stays short: a window of rows
    // and as little markup as possible.
    private const int MenuWindow = 5;

    private void LoadCheatMenu()
    {
        _menuItems =
        [
            Toggle("Wallhack", Power.Wallhack),
            Toggle("Aimbot", Power.Aimbot),
            new MenuItem("Aim area", p => $"{AimSettingsOf(p).Fov:0}°", (p, forward) =>
            {
                var a = AimSettingsOf(p);
                var i = Array.IndexOf(FovChoices, a.Fov);
                _aimSettings[p.Slot] = (FovChoices[Cycle(i, FovChoices.Length, forward)], a.Smooth);
            }),
            new MenuItem("Aim smoothing", p => SmoothChoices.FirstOrDefault(c => Math.Abs(c.Value - AimSettingsOf(p).Smooth) < 0.001f).Name ?? "Medium", (p, forward) =>
            {
                var a = AimSettingsOf(p);
                var i = Array.FindIndex(SmoothChoices, c => Math.Abs(c.Value - a.Smooth) < 0.001f);
                _aimSettings[p.Slot] = (a.Fov, SmoothChoices[Cycle(i, SmoothChoices.Length, forward)].Value);
            }),
            Toggle("Enemy info", Power.EspInfo),
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

    private static int Cycle(int index, int count, bool forward) =>
        index < 0 ? 0 : (index + (forward ? 1 : count - 1)) % count;

    private static string OnOff(bool on) => on ? "<font color='#3f3'>ON</font>" : "<font color='#f55'>off</font>";

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
        if (player == null || !player.IsValid || player.IsBot) return;
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
        if (_hackMode == HackMode.Off && !PracticeAllowed(player)) return;
        if (!player.PawnIsAlive)
        {
            Reply(player, "Open the cheat menu while you're alive.");
            return;
        }

        var state = new CheatMenuState { LastButtons = player.Buttons };
        if (player.PlayerPawn.Value is { IsValid: true } pawn)
        {
            // Stand still while the movement keys drive the menu.
            state.PreviousMoveType = pawn.MoveType;
            pawn.MoveType = pawn.ActualMoveType = MoveType_t.MOVETYPE_NONE;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }
        _cheatMenus[player.Slot] = state;
        Reply(player, "Cheat menu open (center of the screen): W/S move, D switch, A back, R close.");
    }

    private void CloseCheatMenu(CCSPlayerController player)
    {
        if (!_cheatMenus.Remove(player.Slot, out var state)) return;
        if (player.IsValid && player.PlayerPawn.Value is { IsValid: true } pawn && pawn.MoveType == MoveType_t.MOVETYPE_NONE)
        {
            pawn.MoveType = pawn.ActualMoveType = state.PreviousMoveType == MoveType_t.MOVETYPE_NONE ? MoveType_t.MOVETYPE_WALK : state.PreviousMoveType;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        }
        if (player.IsValid)
        {
            player.PrintToCenterHtml($"<font color='#39ff14'>Cheats:</font> <font color='#cccccc'>{DescribePowers(player)}</font>", 3);
            // Walls may have been switched on for someone who wasn't a hacker before.
            if (GlowsNeeded && _glows.Count == 0) RebuildGlows();
        }
    }

    /// <summary>Called every tick from the hacker-mode tick.</summary>
    private void TickCheatMenus()
    {
        if (_cheatMenus.Count == 0) return;
        foreach (var slot in _cheatMenus.Keys.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player == null || !player.IsValid || !player.PawnIsAlive)
            {
                if (player != null) CloseCheatMenu(player);
                else _cheatMenus.Remove(slot);
                continue;
            }

            var state = _cheatMenus[slot];
            var buttons = player.Buttons;
            var pressed = buttons & ~state.LastButtons;
            state.LastButtons = buttons;

            if ((pressed & PlayerButtons.Reload) != 0)
            {
                CloseCheatMenu(player);
                continue;
            }
            if ((pressed & PlayerButtons.Forward) != 0) Move(state, -1);
            if ((pressed & PlayerButtons.Back) != 0) Move(state, +1);
            if ((pressed & (PlayerButtons.Moveright | PlayerButtons.Use)) != 0) Activate(player, state, forward: true);
            else if ((pressed & PlayerButtons.Moveleft) != 0) Activate(player, state, forward: false);
            if (!_cheatMenus.ContainsKey(slot)) continue; // "Close" was picked

            // Send the (unchanged) text every tick, like CounterStrikeSharp's own center menus: refreshing
            // less often lets the panel fade out and back in, which flickers.
            if (state.Dirty)
            {
                state.Html = RenderMenu(player, state);
                state.Dirty = false;
            }
            player.PrintToCenterHtml(state.Html, 1);
        }
    }

    private void Move(CheatMenuState state, int delta)
    {
        state.Cursor = (state.Cursor + delta + _menuItems.Count) % _menuItems.Count;
        state.Dirty = true;
    }

    private void Activate(CCSPlayerController player, CheatMenuState state, bool forward)
    {
        _menuItems[state.Cursor].Activate(player, forward);
        state.Dirty = true;
    }

    private string RenderMenu(CCSPlayerController player, CheatMenuState state)
    {
        var html = new System.Text.StringBuilder();
        html.Append($"<font color='#3f3'>H4X MENU</font> {state.Cursor + 1}/{_menuItems.Count}<br>");

        // A window of rows around the cursor.
        var first = Math.Clamp(state.Cursor - MenuWindow / 2, 0, Math.Max(0, _menuItems.Count - MenuWindow));
        var last = Math.Min(_menuItems.Count, first + MenuWindow);
        for (var i = first; i < last; i++)
        {
            var item = _menuItems[i];
            var value = item.Value(player);
            html.Append(i == state.Cursor ? "<font color='#fff'>&gt; " : "<font color='#999'>");
            html.Append(item.Label).Append("</font>");
            if (value != "") html.Append(": ").Append(value);
            html.Append("<br>");
        }
        html.Append("<font color='#888'>W/S D R</font>");
        return html.ToString();
    }
}
