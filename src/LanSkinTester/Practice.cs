using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Practice quality-of-life on the local server: damage in chat, god mode, saved spots, grenade rethrow,
/// grenade lineups saved per map (!savenade, kept in lineups/&lt;map&gt;.json), a grenade clear, armor and nades, team swap and round wins. Same rule as the other practice commands:
/// only on a LAN or private server. The launcher drives the lst_* commands; players can use the chat ones.
/// </summary>
public partial class LanSkinTester
{
    private bool _showDamage;
    private readonly HashSet<int> _god = new();
    private readonly Dictionary<int, Dictionary<string, Spot>> _spots = new();
    private readonly Dictionary<int, ThrownGrenade> _throws = new();

    private void LoadPractice()
    {
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _spots.Clear();
            _throws.Clear();
        });
    }

    private readonly record struct Spot(float X, float Y, float Z, float Pitch, float Yaw);

    private readonly record struct ThrownGrenade(Spot From, string Weapon);

    private static readonly string[] GrenadeEntities =
    [
        "smokegrenade_projectile", "flashbang_projectile", "hegrenade_projectile",
        "molotov_projectile", "decoy_projectile", "inferno",
    ];

    // ---------------------------------------------------------------------------------------------
    // Launcher commands
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("lst_show_damage", "Damage in chat: 1 on, 0 off, or toggle")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnShowDamageCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var arg = cmd.ArgCount > 1 ? cmd.GetArg(1) : "toggle";
        _showDamage = arg switch { "1" => true, "0" => false, _ => !_showDamage };
    }

    [ConsoleCommand("lst_god", "Toggle god mode for every human")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnGodAllCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var player in Humans()) ToggleGod(player, quiet: true);
    }

    [ConsoleCommand("lst_noclip", "Toggle noclip for every human")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnNoclipAllCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var player in Humans()) ToggleNoclip(player, quiet: true);
    }

    [ConsoleCommand("lst_armor", "Give every human armor, a helmet and (CTs) a kit")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnArmorCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var player in Humans().Where(p => p.PawnIsAlive)) GiveArmor(player);
    }

    [ConsoleCommand("lst_nades", "Give every human a set of grenades")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnNadesCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var player in Humans().Where(p => p.PawnIsAlive)) GiveNades(player);
    }

    [ConsoleCommand("lst_clear", "Remove smokes, fires and grenades in the world")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnClearCommand(CCSPlayerController? caller, CommandInfo cmd) => ClearGrenades();

    [ConsoleCommand("lst_rethrow", "Put every human back where they last threw, with that grenade")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRethrowAllCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var player in Humans()) Rethrow(player);
    }

    [ConsoleCommand("lst_spot", "Save or load a practice spot for every human: lst_spot save|load [name]")]
    [CommandHelper(minArgs: 1, usage: "save|load [name]", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSpotCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var load = cmd.GetArg(1).StartsWith("l", StringComparison.OrdinalIgnoreCase);
        var name = cmd.ArgCount > 2 ? cmd.GetArg(2) : "";
        foreach (var player in Humans())
        {
            if (load) LoadSpot(player, name);
            else SaveSpot(player, name);
        }
    }

    [ConsoleCommand("lst_swap", "Swap the two teams")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSwapCommand(CCSPlayerController? caller, CommandInfo cmd) => SwapTeams();

    [ConsoleCommand("lst_endround", "End the round: lst_endround ct|t")]
    [CommandHelper(minArgs: 1, usage: "ct|t", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnEndRoundCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var tWin = cmd.GetArg(1).StartsWith("t", StringComparison.OrdinalIgnoreCase);
        EndRound(tWin);
    }

    [ConsoleCommand("lst_score", "Set the score: lst_score <ct> <t>")]
    [CommandHelper(minArgs: 2, usage: "<ct> <t>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnScoreCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        if (!int.TryParse(cmd.GetArg(1), out var ct) || !int.TryParse(cmd.GetArg(2), out var t)) return;
        SetScore(ct, t);
    }

    [ConsoleCommand("lst_pause", "Pause or resume the match: lst_pause on|off")]
    [CommandHelper(minArgs: 1, usage: "on|off", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnPauseCommand(CCSPlayerController? caller, CommandInfo cmd) =>
        Server.ExecuteCommand(cmd.GetArg(1) == "off" ? "mp_unpause_match" : "mp_pause_match");

    // ---------------------------------------------------------------------------------------------
    // Chat
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("css_god", "Toggle god mode")]
    public void OnGodChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        ToggleGod(player!, quiet: false);
    }

    [ConsoleCommand("css_save", "Save where you stand: !save [name]")]
    public void OnSaveChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        SaveSpot(player!, cmd.ArgCount > 1 ? cmd.GetArg(1) : "");
    }

    [ConsoleCommand("css_load", "Go back to a saved spot: !load [name]")]
    public void OnLoadChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        LoadSpot(player!, cmd.ArgCount > 1 ? cmd.GetArg(1) : "");
    }

    [ConsoleCommand("css_spots", "List your saved spots")]
    public void OnSpotsChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (!_spots.TryGetValue(player!.Slot, out var spots) || spots.Count == 0)
        {
            Reply(player, "No saved spots on this map. !save name");
            return;
        }
        Reply(player, "Spots: " + string.Join(", ", spots.Keys.OrderBy(n => n)));
    }

    [ConsoleCommand("css_rethrow", "Go back to your last grenade throw")]
    public void OnRethrowChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        Rethrow(player!);
    }

    [ConsoleCommand("css_savenade", "Save your last grenade throw as a lineup for this map: !savenade <name>")]
    public void OnSaveNadeChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (!_throws.TryGetValue(player!.Slot, out var thrown))
        {
            Reply(player, "Throw a grenade first, then !savenade name.");
            return;
        }
        var name = CleanSpot(cmd.ArgCount > 1 ? cmd.GetArg(1) : "");
        var lineups = Lineups();
        lineups[name] = thrown;
        SaveLineups(lineups);
        Reply(player, $"Saved lineup \"{name}\". !lineup {name} brings you back.");
    }

    [ConsoleCommand("css_lineups", "List this map's saved grenade lineups")]
    public void OnLineupsChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        var lineups = Lineups();
        Reply(player!, lineups.Count == 0 ? "No lineups on this map yet. Throw, then !savenade name."
            : "Lineups: " + string.Join(", ", lineups.OrderBy(l => l.Key).Select(l => $"{l.Key} ({l.Value.Weapon.Replace("weapon_", "")})")));
    }

    [ConsoleCommand("css_lineup", "Go to a saved grenade lineup with that grenade: !lineup <name>")]
    [CommandHelper(minArgs: 1, usage: "<name>")]
    public void OnLineupChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        var name = CleanSpot(cmd.GetArg(1));
        if (!Lineups().TryGetValue(name, out var lineup))
        {
            Reply(player!, $"No lineup named \"{name}\". !lineups lists them.");
            return;
        }
        GoToThrow(player!, lineup);
        Reply(player!, $"Lineup \"{name}\": throw!");
    }

    [ConsoleCommand("css_delnade", "Delete a saved grenade lineup: !delnade <name>")]
    [CommandHelper(minArgs: 1, usage: "<name>")]
    public void OnDelNadeChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        var name = CleanSpot(cmd.GetArg(1));
        var lineups = Lineups();
        if (lineups.Remove(name)) SaveLineups(lineups);
        Reply(player!, $"Deleted \"{name}\".");
    }

    [ConsoleCommand("css_damage", "Toggle the damage report in chat")]
    public void OnDamageChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        _showDamage = !_showDamage;
        Reply(player!, _showDamage ? "Damage report on." : "Damage report off.");
    }

    [ConsoleCommand("css_armor", "Armor, helmet and a kit")]
    public void OnArmorChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player) || !Alive(player!)) return;
        GiveArmor(player!);
        Reply(player!, "Armor on.");
    }

    [ConsoleCommand("css_nades", "A full set of grenades")]
    public void OnNadesChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player) || !Alive(player!)) return;
        GiveNades(player!);
        Reply(player!, "Grenades given.");
    }

    [ConsoleCommand("css_clear", "Remove smokes, fires and grenades")]
    public void OnClearChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        ClearGrenades();
        Reply(player!, "Cleared.");
    }

    [ConsoleCommand("css_swap", "Swap the two teams")]
    public void OnSwapChat(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        SwapTeams();
    }

    // ---------------------------------------------------------------------------------------------

    [GameEventHandler]
    public HookResult OnPracticeSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !player.IsValid || !_god.Contains(player.Slot)) return HookResult.Continue;
        AddTimer(0.15f, () =>
        {
            var pawn = player.IsValid ? player.PlayerPawn.Value : null;
            if (pawn != null && pawn.IsValid) pawn.TakesDamage = false;
        });
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnPracticeHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!_showDamage) return HookResult.Continue;
        var attacker = @event.Attacker;
        var victim = @event.Userid;
        if (attacker == null || victim == null || !attacker.IsValid || !victim.IsValid) return HookResult.Continue;
        if (!IsHumanPlayer(attacker) || attacker.Slot == victim.Slot || @event.DmgHealth <= 0) return HookResult.Continue;

        var where = @event.Hitgroup switch
        {
            1 => "head",
            2 => "chest",
            3 => "stomach",
            4 => "left arm",
            5 => "right arm",
            6 => "left leg",
            7 => "right leg",
            _ => "body",
        };
        var left = @event.Health <= 0 ? "killed" : $"{@event.Health} hp";
        Reply(attacker, $"{@event.DmgHealth} to {victim.PlayerName}'s {where} ({left})");
        return HookResult.Continue;
    }

    [GameEventHandler]
    public HookResult OnGrenadeThrown(EventWeaponFire @event, GameEventInfo info)
    {
        var player = @event.Userid;
        var weapon = @event.Weapon ?? "";
        if (player == null || !IsHumanPlayer(player) || !IsGrenade(weapon)) return HookResult.Continue;
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid || pawn.AbsOrigin == null) return HookResult.Continue;
        _throws[player.Slot] = new ThrownGrenade(SpotOf(pawn), NormalizeWeapon(weapon));
        return HookResult.Continue;
    }

    private void ToggleGod(CCSPlayerController player, bool quiet)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid)
        {
            if (!quiet) Reply(player, "You must be alive.");
            return;
        }
        var on = !_god.Contains(player.Slot);
        if (on) _god.Add(player.Slot);
        else _god.Remove(player.Slot);
        pawn.TakesDamage = !on;
        if (!quiet) Reply(player, on ? "God mode on." : "God mode off.");
    }

    private void ToggleNoclip(CCSPlayerController player, bool quiet)
    {
        var pawn = player.PlayerPawn.Value;
        if (!player.PawnIsAlive || pawn == null || !pawn.IsValid)
        {
            if (!quiet) Reply(player, "You must be alive.");
            return;
        }
        if (ConVar.Find("sv_cheats")?.GetPrimitiveValue<bool>() != true) Server.ExecuteCommand("sv_cheats 1");
        var flying = pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP;
        pawn.MoveType = pawn.ActualMoveType = flying ? MoveType_t.MOVETYPE_WALK : MoveType_t.MOVETYPE_NOCLIP;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        if (!quiet) Reply(player, flying ? "Noclip off." : "Noclip on.");
    }

    private static void GiveArmor(CCSPlayerController player)
    {
        player.GiveNamedItem("item_assaultsuit");
        if (player.Team == CsTeam.CounterTerrorist) player.GiveNamedItem("item_defuser");
    }

    private static void GiveNades(CCSPlayerController player)
    {
        player.GiveNamedItem("weapon_hegrenade");
        player.GiveNamedItem("weapon_flashbang");
        player.GiveNamedItem("weapon_flashbang");
        player.GiveNamedItem("weapon_smokegrenade");
        player.GiveNamedItem(player.Team == CsTeam.Terrorist ? "weapon_molotov" : "weapon_incgrenade");
    }

    private static void ClearGrenades()
    {
        foreach (var name in GrenadeEntities)
        {
            foreach (var entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(name))
            {
                if (entity.IsValid) entity.AcceptInput("Kill");
            }
        }
    }

    private void SaveSpot(CCSPlayerController player, string name)
    {
        var pawn = player.PlayerPawn.Value;
        if (!player.PawnIsAlive || pawn == null || !pawn.IsValid || pawn.AbsOrigin == null)
        {
            Reply(player, "You must be alive.");
            return;
        }
        name = CleanSpot(name);
        var spots = _spots.TryGetValue(player.Slot, out var existing) ? existing : _spots[player.Slot] = new();
        spots[name] = SpotOf(pawn);
        Reply(player, name == "default" ? "Spot saved. !load sends you back." : $"Saved spot \"{name}\".");
    }

    private void LoadSpot(CCSPlayerController player, string name)
    {
        name = CleanSpot(name);
        if (!_spots.TryGetValue(player.Slot, out var spots) || !spots.TryGetValue(name, out var spot))
        {
            Reply(player, name == "default" ? "No spot saved yet. !save" : $"No spot named \"{name}\".");
            return;
        }
        void Go()
        {
            var pawn = player.IsValid ? player.PlayerPawn.Value : null;
            if (pawn == null || !pawn.IsValid) return;
            pawn.Teleport(new Vector(spot.X, spot.Y, spot.Z), new QAngle(spot.Pitch, spot.Yaw, 0), new Vector(0, 0, 0));
        }
        if (!player.PawnIsAlive && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
        {
            player.Respawn();
            AddTimer(0.2f, Go);
        }
        else Go();
        Reply(player, name == "default" ? "Back at your saved spot." : $"Loaded \"{name}\".");
    }

    private void Rethrow(CCSPlayerController player)
    {
        if (!_throws.TryGetValue(player.Slot, out var thrown))
        {
            Reply(player, "Throw a grenade first, then !rethrow.");
            return;
        }
        GoToThrow(player, thrown);
        Reply(player, "Back to your last throw.");
    }

    private void GoToThrow(CCSPlayerController player, ThrownGrenade thrown)
    {
        void Go()
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid) return;
            var spot = thrown.From;
            pawn.Teleport(new Vector(spot.X, spot.Y, spot.Z), new QAngle(spot.Pitch, spot.Yaw, 0), new Vector(0, 0, 0));
            if (FindPlayerWeapon(player, w => w.DesignerName == thrown.Weapon) == null)
                player.GiveNamedItem(thrown.Weapon);
        }
        if (!player.PawnIsAlive && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
        {
            player.Respawn();
            AddTimer(0.2f, Go);
        }
        else Go();
    }

    // Lineups are shared by everyone on the server and kept across restarts, one file per map.
    private string LineupFile() => Path.Combine(ModuleDirectory, "lineups",
        new string(Server.MapName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + ".json");

    private Dictionary<string, ThrownGrenade> Lineups()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, ThrownGrenade>>(File.ReadAllText(LineupFile())) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private void SaveLineups(Dictionary<string, ThrownGrenade> lineups)
    {
        var file = LineupFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(lineups));
    }

    private static void SwapTeams()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsHLTV) continue;
            if (player.Team == CsTeam.Terrorist) player.SwitchTeam(CsTeam.CounterTerrorist);
            else if (player.Team == CsTeam.CounterTerrorist) player.SwitchTeam(CsTeam.Terrorist);
        }
    }

    private static void EndRound(bool tWin)
    {
        var rules = Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules").FirstOrDefault()?.GameRules;
        if (rules == null) return;
        rules.TerminateRound(1.0f, tWin ? RoundEndReason.TerroristsWin : RoundEndReason.CTsWin);
    }

    private static void SetScore(int ct, int t)
    {
        foreach (var team in Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager"))
        {
            if (!team.IsValid) continue;
            if (team.TeamNum == (int)CsTeam.CounterTerrorist) team.Score = Math.Clamp(ct, 0, 999);
            else if (team.TeamNum == (int)CsTeam.Terrorist) team.Score = Math.Clamp(t, 0, 999);
            else continue;
            Utilities.SetStateChanged(team, "CTeam", "m_iScore");
        }
    }

    private static Spot SpotOf(CCSPlayerPawn pawn)
    {
        var origin = pawn.AbsOrigin!;
        var angles = pawn.EyeAngles;
        return new Spot(origin.X, origin.Y, origin.Z, angles.X, angles.Y);
    }

    private static string CleanSpot(string name)
    {
        var clean = new string((name ?? "").Trim().ToLowerInvariant().Where(c => char.IsLetterOrDigit(c)).Take(16).ToArray());
        return clean.Length == 0 ? "default" : clean;
    }

    private static bool IsGrenade(string weapon)
    {
        var w = weapon.ToLowerInvariant();
        return w.Contains("grenade") || w.Contains("molotov") || w.Contains("incendiary") || w.Contains("flashbang") || w.Contains("decoy");
    }

    private static string NormalizeWeapon(string weapon)
    {
        weapon = weapon.ToLowerInvariant();
        return weapon.StartsWith("weapon_") ? weapon : "weapon_" + weapon;
    }

    private bool Alive(CCSPlayerController player)
    {
        if (player.PawnIsAlive) return true;
        Reply(player, "You must be alive.");
        return false;
    }
}
