using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace LanSkinTester;

public class LanSkinTesterConfig : BasePluginConfig
{
    /// <summary>Only apply skins when the server runs with sv_lan 1. Keeps this a local testing tool.</summary>
    public bool RequireSvLan { get; set; } = true;

    public string ChatPrefix { get; set; } = "[Skins]";

    /// <summary>
    /// Everyone on the server shares one loadout (the one the launcher edits). Turn off to give
    /// each player their own loadout, saved by SteamID.
    /// </summary>
    public bool SingleLoadout { get; set; } = true;
}

public partial class LanSkinTester : BasePlugin, IPluginConfig<LanSkinTesterConfig>
{
    public override string ModuleName => "LAN Skin Tester";
    public override string ModuleVersion => "2.4.0";
    public override string ModuleAuthor => "Cs-plugin";
    public override string ModuleDescription => "Preview CS2 weapon, knife and glove skins on your own LAN / offline server.";

    // Item IDs above what real inventories use, so the client treats the item as a custom econ item.
    private const ulong FirstCustomItemId = 65578;

    public LanSkinTesterConfig Config { get; set; } = new();

    private LoadoutStore _store = null!;
    private SkinCatalog _catalog = null!;
    private MemoryFunctionVoid<nint, string, float>? _setAttribute;
    private ulong _nextItemId = FirstCustomItemId;
    private readonly HashSet<int> _warnedSlots = new();

    // Set by the launcher's Friends page through lanskintester_server.cfg (server console only).
    private bool? _sharedLoadout;
    private bool _privateServer;
    private uint _hostAccountId;

    private bool SharedLoadout => _sharedLoadout ?? Config.SingleLoadout;

    public void OnConfigParsed(LanSkinTesterConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        _store = new LoadoutStore(Path.Combine(ModuleDirectory, "loadouts.json"));
        _catalog = new SkinCatalog(Path.Combine(ModuleDirectory, "skins"));
        _setAttribute = LoadSetAttributeFunction();

        Logger.LogInformation("Loaded {Count} skin names from the skins folder", _catalog.Count);
        if (CoreConfig.FollowCS2ServerGuidelines) Logger.LogError(GuidelinesMessage);

        RegisterListener<Listeners.OnMapStart>(_ => _warnedSlots.Clear());

        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        AddTimer(1.0f, CheckLauncherChanges, TimerFlags.REPEAT);
        AddTimer(1.0f, RunLauncherCommands, TimerFlags.REPEAT);
        AddTimer(2.0f, WriteStatus, TimerFlags.REPEAT);
        RegisterListener<Listeners.OnMapStart>(_ => AddTimer(2.0f, ExecServerSettings, TimerFlags.STOP_ON_MAPCHANGE));
        VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
        LoadHackerMode();
    }

    public override void Unload(bool hotReload)
    {
        VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
        UnloadHackerMode();
    }

    // ---------------------------------------------------------------------------------------------
    // Hooks
    // ---------------------------------------------------------------------------------------------

    private HookResult OnGiveNamedItemPost(DynamicHook hook)
    {
        try
        {
            var itemServices = hook.GetParam<CCSPlayer_ItemServices>(0);
            var weapon = hook.GetReturn<CBasePlayerWeapon>();
            if (weapon == null || !weapon.IsValid || weapon.DesignerName?.Contains("weapon") != true)
                return HookResult.Continue;

            var pawn = itemServices.Pawn.Value;
            var player = pawn?.Controller.Value?.As<CCSPlayerController>();
            if (player != null && IsHumanPlayer(player))
                ApplyWeaponSkin(player, weapon);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not apply a skin (GiveNamedItem)");
        }

        return HookResult.Continue;
    }

    // Covers weapons that are not created through GiveNamedItem (e.g. spawn loadouts on some maps).
    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (entity.DesignerName?.StartsWith("weapon_") != true) return;

        Server.NextWorldUpdate(() =>
        {
            try
            {
                var weapon = new CBasePlayerWeapon(entity.Handle);
                if (!weapon.IsValid) return;

                var player = FindWeaponOwner(weapon);
                if (player != null && IsHumanPlayer(player))
                    ApplyWeaponSkin(player, weapon);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not apply a skin (entity spawn)");
            }
        });
    }

    // The launcher edits loadouts.json while the server runs; pick up its changes and re-skin everyone.
    private void CheckLauncherChanges()
    {
        if (!SkinsAllowed() || !_store.ChangedOnDisk() || !_store.TryLoad()) return;
        RefreshEveryone("Loadout updated from the launcher.");
    }

    /// <summary>Gives every living human fresh copies of their weapons so the current loadout shows.</summary>
    private void RefreshEveryone(string message)
    {
        foreach (var player in Utilities.GetPlayers().Where(p => IsHumanPlayer(p) && p.PawnIsAlive))
        {
            var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
            if (weapons == null) continue;

            var skinnable = weapons
                .Select(h => h.Value)
                .Where(w => w != null && w.IsValid &&
                            (ItemData.Weapons.ContainsKey(w.AttributeManager.Item.ItemDefinitionIndex) || ItemData.IsKnifeClassname(w.DesignerName)))
                .ToList();

            foreach (var weapon in skinnable) RefreshWeapon(player, weapon!);
            ApplyGloves(player);
            Reply(player, message);
        }
    }

    private void WriteStatus()
    {
        try
        {
            var players = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsHLTV).ToList();
            var status = new ServerStatus
            {
                Version = ModuleVersion,
                Map = Server.MapName,
                GameType = CvarInt("game_type"),
                GameMode = CvarInt("game_mode"),
                Humans = players.Count(p => !p.IsBot),
                Bots = players.Count(p => p.IsBot),
                Players = players.Where(p => !p.IsBot).Select(p => p.PlayerName).ToList(),
                SharedLoadout = SharedLoadout,
                PrivateServer = _privateServer,
                SkinsAllowed = SkinsAllowed(),
                DisabledReason = DisabledReason() ?? "",
                Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            };

            var path = Path.Combine(ModuleDirectory, ServerStatus.FileName);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(status));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch (IOException)
        {
            // The launcher is reading it; next write in 2 seconds.
        }
    }

    private static int CvarInt(string name)
    {
        var cvar = ConVar.Find(name);
        if (cvar == null) return 0;
        try { return cvar.GetPrimitiveValue<int>(); }
        catch { return int.TryParse(cvar.StringValue, out var v) ? v : 0; }
    }

    // ---------------------------------------------------------------------------------------------
    // Quick actions (sent by the launcher through server_commands.txt, or typed in the server console)
    // ---------------------------------------------------------------------------------------------

    private IEnumerable<CCSPlayerController> Humans() => Utilities.GetPlayers().Where(IsHumanPlayer);

    [ConsoleCommand("lst_give", "Give a weapon to every human player: lst_give weapon_ak47")]
    [CommandHelper(minArgs: 1, usage: "<weapon_classname>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnGiveCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var classname = cmd.GetArg(1);
        if (!classname.StartsWith("weapon_")) classname = "weapon_" + classname;
        var def = ItemData.Weapons.FirstOrDefault(w => w.Value == classname).Key;
        if (def == 0 && !classname.StartsWith("weapon_knife"))
        {
            cmd.ReplyToCommand($"Unknown weapon {classname}");
            return;
        }

        foreach (var player in Humans().Where(p => p.PawnIsAlive))
            GiveOrRefresh(player, def, classname.StartsWith("weapon_knife"));
    }

    [ConsoleCommand("lst_view", "Switch human players to third or first person: lst_view third|first")]
    [CommandHelper(minArgs: 1, usage: "third|first", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnViewCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var third = cmd.GetArg(1).StartsWith("t", StringComparison.OrdinalIgnoreCase);
        // thirdperson is a cheat command: it needs sv_cheats 1 (the launcher's "Allow cheats").
        if (third && ConVar.Find("sv_cheats")?.GetPrimitiveValue<bool>() != true) Server.ExecuteCommand("sv_cheats 1");
        foreach (var player in Humans()) player.ExecuteClientCommandFromServer(third ? "thirdperson" : "firstperson");
    }

    [ConsoleCommand("lst_respawn", "Respawn dead human players")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRespawnCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        foreach (var player in Humans().Where(p => !p.PawnIsAlive && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
            player.Respawn();
    }

    [ConsoleCommand("lst_refresh", "Re-apply skins to every human player")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRefreshCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        _store.TryLoad();
        RefreshEveryone("Skins refreshed.");
    }

    // ---------------------------------------------------------------------------------------------
    // Practice commands for players (chat). Same rule as skins: only on a LAN / private server.
    // ---------------------------------------------------------------------------------------------

    private bool PracticeAllowed(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return false;
        if (!Config.RequireSvLan || IsLanServer() || _privateServer) return true;
        Reply(player, "Practice commands only work on a LAN / private server.");
        return false;
    }

    [ConsoleCommand("css_give", "Give yourself a weapon: !give ak47")]
    [CommandHelper(minArgs: 1, usage: "<weapon>")]
    public void OnGiveChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (!player!.PawnIsAlive)
        {
            Reply(player, "You must be alive.");
            return;
        }

        var query = cmd.GetArg(1).ToLowerInvariant().Replace("weapon_", "").Replace("-", "").Replace(" ", "");
        if (query.StartsWith("knife"))
        {
            GiveOrRefresh(player, 0, true);
            return;
        }
        // Classname ("ak47") or display name ("AK-47", "deagle" → Desert Eagle).
        var match = ItemData.Weapons.FirstOrDefault(w => w.Value == "weapon_" + query);
        if (match.Key == 0)
            match = ItemData.Weapons.FirstOrDefault(w =>
                ItemData.DisplayNames.TryGetValue(w.Key, out var name) && name.ToLowerInvariant().Replace("-", "").Replace(" ", "").Contains(query));
        if (match.Key == 0)
        {
            Reply(player, $"Unknown weapon \"{cmd.GetArg(1)}\". Try ak47, awp, deagle, m4a1_silencer, usp_silencer...");
            return;
        }
        GiveOrRefresh(player, match.Key, false);
        Reply(player, $"Here's your {ItemData.NameOf(match.Key)}.");
    }

    [ConsoleCommand("css_3p", "Third person view")]
    public void OnThirdPersonChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (ConVar.Find("sv_cheats")?.GetPrimitiveValue<bool>() != true) Server.ExecuteCommand("sv_cheats 1");
        player!.ExecuteClientCommandFromServer("thirdperson");
    }

    [ConsoleCommand("css_fp", "First person view")]
    public void OnFirstPersonChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        player!.ExecuteClientCommandFromServer("firstperson");
    }

    [ConsoleCommand("css_respawn", "Respawn yourself")]
    public void OnRespawnChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        if (player!.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            Reply(player, "Join a team first.");
            return;
        }
        player.Respawn();
    }

    [ConsoleCommand("css_noclip", "Fly through walls (toggle)")]
    public void OnNoclipChatCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!PracticeAllowed(player)) return;
        var pawn = player!.PlayerPawn.Value;
        if (!player.PawnIsAlive || pawn == null || !pawn.IsValid)
        {
            Reply(player, "You must be alive.");
            return;
        }
        var flying = pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP;
        pawn.MoveType = pawn.ActualMoveType = flying ? MoveType_t.MOVETYPE_WALK : MoveType_t.MOVETYPE_NOCLIP;
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
        Reply(player, flying ? "Noclip off." : "Noclip on - type !noclip again to land.");
    }

    [ConsoleCommand("lst_shared_loadout", "1 = everyone uses the launcher's skins, 0 = each player picks their own")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnSharedLoadoutCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var shared = cmd.GetArg(1) != "0";
        var changed = SharedLoadout != shared;
        _sharedLoadout = shared;
        if (!changed) return;
        RefreshEveryone(shared ? "Everyone now uses the host's skins." : "Pick your own skins: !skinhelp");
    }

    [ConsoleCommand("lst_private_server", "1 = allow skins with sv_lan 0 (friends over Radmin / a VPN). Only for your own private server.")]
    [CommandHelper(minArgs: 1, usage: "0|1", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnPrivateServerCommand(CCSPlayerController? caller, CommandInfo cmd) => _privateServer = cmd.GetArg(1) == "1";

    [ConsoleCommand("lst_host_account", "Steam account id of the host, who always gets the launcher's loadout")]
    [CommandHelper(minArgs: 1, usage: "<account id>", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnHostAccountCommand(CCSPlayerController? caller, CommandInfo cmd) =>
        _hostAccountId = uint.TryParse(cmd.GetArg(1), out var id) ? id : 0;

    // ---------------------------------------------------------------------------------------------
    // Server settings from the launcher (game mode, bots, practice options)
    // ---------------------------------------------------------------------------------------------

    private const string ServerSettingsCfg = "lanskintester_server.cfg";

    private string CommandsFile => Path.Combine(ModuleDirectory, "server_commands.txt");

    // plugins/LanSkinTester -> csgo/cfg
    private string ServerSettingsPath =>
        Path.GetFullPath(Path.Combine(ModuleDirectory, "..", "..", "..", "..", "cfg", ServerSettingsCfg));

    // Runs after the game mode's own config, so the launcher's bot/practice settings win.
    private void ExecServerSettings()
    {
        if (File.Exists(ServerSettingsPath)) Server.ExecuteCommand($"exec {ServerSettingsCfg}");
    }

    // The launcher drops one-off commands here ("Apply to running server").
    private void RunLauncherCommands()
    {
        if (!File.Exists(CommandsFile)) return;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(CommandsFile);
            File.Delete(CommandsFile);
        }
        catch (IOException)
        {
            return; // launcher is still writing it; try again next second
        }

        foreach (var line in lines.Select(l => l.Trim()).Where(l => l != "" && !l.StartsWith("//")))
        {
            Logger.LogInformation("Launcher command: {Command}", line);
            Server.ExecuteCommand(line);
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player == null || !IsHumanPlayer(player)) return HookResult.Continue;

        var reason = DisabledReason();
        if (reason != null)
        {
            // Say it once per map so the player knows why nothing changes.
            if (_warnedSlots.Add(player.Slot)) AddTimer(2.0f, () => { if (player.IsValid) Reply(player, reason); });
            return HookResult.Continue;
        }

        AddTimer(0.1f, () => ApplyGloves(player), TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    // ---------------------------------------------------------------------------------------------
    // Applying skins
    // ---------------------------------------------------------------------------------------------

    private void ApplyWeaponSkin(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        if (!SkinsAllowed()) return;

        var loadout = GetLoadout(player);
        var item = weapon.AttributeManager.Item;
        var isKnife = ItemData.IsKnifeClassname(weapon.DesignerName);

        if (isKnife && loadout.Knife != 0)
        {
            if (item.ItemDefinitionIndex != loadout.Knife)
                weapon.AcceptInput("ChangeSubclass", value: loadout.Knife.ToString());

            item.ItemDefinitionIndex = loadout.Knife;
            item.EntityQuality = 3; // "★" unusual quality
        }

        var defIndex = item.ItemDefinitionIndex;
        if (!loadout.Skins.TryGetValue(defIndex, out var skin) || !skin.HasCustomization)
        {
            if (isKnife && loadout.Knife != 0)
            {
                // A vanilla custom knife still needs its own item ID or the client keeps the default model.
                item.AttributeList.Attributes.RemoveAll();
                item.NetworkedDynamicAttributes.Attributes.RemoveAll();
                AssignNewItemId(item, player);
            }
            return;
        }

        item.AttributeList.Attributes.RemoveAll();
        item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        AssignNewItemId(item, player);

        item.CustomName = skin.Nametag;
        if (!isKnife) item.EntityQuality = 0;

        if (skin.Paint != 0)
        {
            weapon.FallbackPaintKit = skin.Paint;
            weapon.FallbackSeed = skin.Seed;
            weapon.FallbackWear = skin.Wear;
            SetPaintAttributes(item, skin);
        }

        SetStickers(item, skin);
        SetKeychain(item, skin);

        if (skin.StatTrak)
        {
            item.EntityQuality = 9;
            // "kill eater" stores the kill count as raw int bits inside a float.
            var kills = BitConverter.Int32BitsToSingle(skin.StatTrakCount);
            SetAttribute(item.NetworkedDynamicAttributes, "kill eater", kills);
            SetAttribute(item.NetworkedDynamicAttributes, "kill eater score type", 0);
            SetAttribute(item.AttributeList, "kill eater", kills);
            SetAttribute(item.AttributeList, "kill eater score type", 0);
        }

        var legacy = IsLegacy(defIndex, skin, isKnife);
        weapon.AcceptInput("SetBodygroup", value: $"body,{(legacy ? 1 : 0)}");
    }

    private void ApplyGloves(CCSPlayerController player)
    {
        if (!SkinsAllowed() || !player.IsValid || !player.PawnIsAlive) return;

        var loadout = GetLoadout(player);
        if (loadout.Gloves == 0 || !loadout.Skins.TryGetValue(loadout.Gloves, out var skin) || skin.Paint == 0)
            return;

        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;

        var item = pawn.EconGloves;
        item.NetworkedDynamicAttributes.Attributes.RemoveAll();
        item.AttributeList.Attributes.RemoveAll();

        // Switching weapons forces the viewmodel to rebuild, otherwise old and new gloves overlap.
        player.ExecuteClientCommand("lastinv");

        AddTimer(0.08f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive || !pawn.IsValid) return;

            item.ItemDefinitionIndex = loadout.Gloves;
            AssignNewItemId(item, player);
            SetPaintAttributes(item, skin);
            item.Initialized = true;

            player.ExecuteClientCommand("lastinv");
            pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,0");
            AddTimer(0.2f, () =>
            {
                if (pawn.IsValid) pawn.AcceptInput("SetBodygroup", value: "first_or_third_person,1");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void SetPaintAttributes(CEconItemView item, SkinSettings skin)
    {
        foreach (var list in new[] { item.NetworkedDynamicAttributes, item.AttributeList })
        {
            SetAttribute(list, "set item texture prefab", skin.Paint);
            SetAttribute(list, "set item texture seed", skin.Seed);
            SetAttribute(list, "set item texture wear", skin.Wear);
        }
    }

    // Ids are stored as raw integer bits inside the float attribute value.
    private static float IdBits(int id) => BitConverter.Int32BitsToSingle(id);

    private void SetStickers(CEconItemView item, SkinSettings skin)
    {
        foreach (var st in skin.Stickers.Where(st => st.Slot is >= 0 and < 5 && st.Id != 0))
        {
            var prefix = $"sticker slot {st.Slot}";
            var list = item.NetworkedDynamicAttributes;
            SetAttribute(list, $"{prefix} id", IdBits(st.Id));
            if (st.OffsetX != 0 || st.OffsetY != 0) SetAttribute(list, $"{prefix} schema", 0);
            SetAttribute(list, $"{prefix} offset x", st.OffsetX);
            SetAttribute(list, $"{prefix} offset y", st.OffsetY);
            SetAttribute(list, $"{prefix} wear", st.Wear);
            SetAttribute(list, $"{prefix} scale", st.Scale == 0 ? 1 : st.Scale);
            SetAttribute(list, $"{prefix} rotation", st.Rotation);
        }
    }

    private void SetKeychain(CEconItemView item, SkinSettings skin)
    {
        if (skin.Keychain is not { Id: not 0 } charm) return;

        var list = item.NetworkedDynamicAttributes;
        SetAttribute(list, "keychain slot 0 id", IdBits(charm.Id));
        SetAttribute(list, "keychain slot 0 offset x", charm.OffsetX);
        SetAttribute(list, "keychain slot 0 offset y", charm.OffsetY);
        SetAttribute(list, "keychain slot 0 offset z", charm.OffsetZ);
        SetAttribute(list, "keychain slot 0 seed", IdBits(charm.Seed));
    }

    private void SetAttribute(CAttributeList list, string name, float value) =>
        _setAttribute?.Invoke(list.Handle, name, value);

    private void AssignNewItemId(CEconItemView item, CCSPlayerController player)
    {
        var id = _nextItemId++;
        item.ItemID = id;
        item.ItemIDLow = (uint)(id & 0xFFFFFFFF);
        item.ItemIDHigh = (uint)(id >> 32);
        item.AccountID = (uint)player.SteamID;
    }

    /// <summary>
    /// Replaces a held weapon with a fresh copy so the client re-renders it with the new skin.
    /// The GiveNamedItem hook applies the stored skin to the new entity.
    /// </summary>
    private void RefreshWeapon(CCSPlayerController player, CBasePlayerWeapon weapon)
    {
        var defIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
        var isKnife = ItemData.IsKnifeClassname(weapon.DesignerName);

        string? classname = isKnife
            ? (player.Team == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife")
            : ItemData.Weapons.GetValueOrDefault(defIndex);
        if (classname == null) return;

        var pawn = player.PlayerPawn.Value;
        var wasActive = pawn?.WeaponServices?.ActiveWeapon.Value?.Index == weapon.Index;
        var slot = isKnife ? 3 : weapon.As<CCSWeaponBase>().VData?.GearSlot == gear_slot_t.GEAR_SLOT_PISTOL ? 2 : 1;
        var clip = weapon.Clip1;
        var reserve = weapon.ReserveAmmo[0];

        weapon.AddEntityIOEvent("Kill", weapon, null, "", 0.01f);

        AddTimer(0.1f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;

            var ptr = player.GiveNamedItem(classname);
            if (ptr == IntPtr.Zero) return;

            var fresh = new CBasePlayerWeapon(ptr);
            Server.NextFrame(() =>
            {
                if (!fresh.IsValid || isKnife) return;
                fresh.Clip1 = clip;
                fresh.ReserveAmmo[0] = reserve;
            });

            if (wasActive) player.ExecuteClientCommand($"slot{slot}");
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    // ---------------------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------------------

    [ConsoleCommand("css_skin", "Set a skin on the weapon in your hands: !skin <paint id | name> [wear] [seed]")]
    [CommandHelper(minArgs: 1, usage: "<paint id | name> [wear] [seed]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSkinCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldWeapon(player, out var weapon)) return;

        var defIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
        var args = Args(cmd);

        // Trailing numbers are wear/seed only when the first argument was a paint ID.
        int paint;
        int consumed;
        if (int.TryParse(args[0], out paint))
        {
            consumed = 1;
        }
        else
        {
            var query = string.Join(' ', args.TakeWhile(a => !IsNumber(a)));
            consumed = query.Split(' ').Length;
            var matches = _catalog.Search(query, defIndex);
            if (_catalog.Count == 0)
            {
                Reply(player!, "No skin names installed - use a paint kit ID (see README).");
                return;
            }
            if (matches.Count == 0)
            {
                Reply(player!, $"No {ItemData.NameOf(defIndex)} skin matches \"{query}\".");
                return;
            }
            if (matches.Count > 1)
            {
                Reply(player!, $"{matches.Count} matches, use the ID:");
                foreach (var m in matches.Take(8))
                    player!.PrintToChat($"  {ChatColors.Lime}{m.Paint}{ChatColors.Default} {m.Name}");
                return;
            }
            paint = matches[0].Paint;
        }

        var skin = GetLoadout(player!).GetOrCreate(defIndex);
        skin.Paint = paint;
        if (args.Length > consumed && TryParseWear(args[consumed], out var wear)) skin.Wear = wear;
        if (args.Length > consumed + 1 && int.TryParse(args[consumed + 1], out var seed)) skin.Seed = seed;

        SaveAndRefresh(player!, weapon, $"{ItemData.NameOf(defIndex)} -> paint {paint} {DescribeSkin(defIndex, skin)}");
    }

    [ConsoleCommand("css_wear", "Change wear (0.0 - 1.0) of the held weapon's skin")]
    [CommandHelper(minArgs: 1, usage: "<0.0 - 1.0>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnWearCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldSkin(player, out var weapon, out var skin)) return;
        if (!TryParseWear(cmd.GetArg(1), out var wear))
        {
            Reply(player!, "Wear must be a number between 0 and 1 (e.g. 0.07).");
            return;
        }
        skin.Wear = wear;
        SaveAndRefresh(player!, weapon, $"Wear set to {wear.ToString(CultureInfo.InvariantCulture)} ({ItemData.WearName(wear)})");
    }

    [ConsoleCommand("css_seed", "Change the pattern seed (0 - 1000) of the held weapon's skin")]
    [CommandHelper(minArgs: 1, usage: "<0 - 1000>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSeedCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldSkin(player, out var weapon, out var skin)) return;
        if (!int.TryParse(cmd.GetArg(1), out var seed) || seed < 0 || seed > 1000)
        {
            Reply(player!, "Seed must be a whole number between 0 and 1000.");
            return;
        }
        skin.Seed = seed;
        SaveAndRefresh(player!, weapon, $"Pattern seed set to {seed}");
    }

    [ConsoleCommand("css_st", "Toggle StatTrak on the held weapon: !st [kills | off]")]
    [CommandHelper(usage: "[kills | off]", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnStatTrakCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldSkin(player, out var weapon, out var skin)) return;

        var arg = cmd.ArgCount > 1 ? cmd.GetArg(1) : "";
        if (arg.Equals("off", StringComparison.OrdinalIgnoreCase)) skin.StatTrak = false;
        else if (int.TryParse(arg, out var kills)) { skin.StatTrak = true; skin.StatTrakCount = Math.Max(0, kills); }
        else skin.StatTrak = !skin.StatTrak;

        SaveAndRefresh(player!, weapon, skin.StatTrak ? $"StatTrak on ({skin.StatTrakCount} kills)" : "StatTrak off");
    }

    [ConsoleCommand("css_nametag", "Set a name tag on the held weapon: !nametag <text | off>")]
    [CommandHelper(minArgs: 1, usage: "<text | off>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNametagCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldSkin(player, out var weapon, out var skin)) return;

        var text = cmd.ArgString.Trim().Trim('"');
        skin.Nametag = text.Equals("off", StringComparison.OrdinalIgnoreCase) ? "" : text[..Math.Min(text.Length, 20)];
        SaveAndRefresh(player!, weapon, skin.Nametag == "" ? "Name tag removed" : $"Name tag: \"{skin.Nametag}\"");
    }

    [ConsoleCommand("css_legacy", "Toggle the legacy weapon model for the held weapon's skin")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnLegacyCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldSkin(player, out var weapon, out var skin)) return;

        var defIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
        skin.Legacy = !IsLegacy(defIndex, skin, ItemData.IsKnifeClassname(weapon.DesignerName));
        SaveAndRefresh(player!, weapon, $"Legacy model {(skin.Legacy.Value ? "on" : "off")} (use this if the skin looks stretched/broken)");
    }

    [ConsoleCommand("css_skinreset", "Remove the skin from the held weapon")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSkinResetCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!TryGetHeldWeapon(player, out var weapon)) return;

        var defIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
        GetLoadout(player!).Skins.Remove(defIndex);
        SaveAndRefresh(player!, weapon, $"{ItemData.NameOf(defIndex)} reset to default");
    }

    [ConsoleCommand("css_gen", "Import a real item from a gen code: !gen <defindex> <paint> <seed> <float> [stickers...]")]
    [ConsoleCommand("css_inspect", "Import a real item from an inspect link: !inspect <link>")]
    [CommandHelper(minArgs: 1, usage: "<gen code | inspect link>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnImportCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!CheckAllowed(player) || !player!.PawnIsAlive)
        {
            if (player is { IsValid: true, PawnIsAlive: false }) Reply(player, "You must be alive.");
            return;
        }

        // "!gen 7 282 ..." arrives as css_gen with the numbers as arguments.
        var text = cmd.GetCommandString.StartsWith("css_gen", StringComparison.OrdinalIgnoreCase)
            ? "!gen " + cmd.ArgString
            : cmd.ArgString.Trim('"');

        if (!ItemImport.TryParse(text, out var imported, out var error))
        {
            Reply(player, error);
            return;
        }

        var def = imported.DefIndex;
        var isKnife = ItemData.Knives.ContainsValue(def);
        var isGloves = ItemData.Gloves.ContainsValue(def);
        if (!isKnife && !isGloves && !ItemData.Weapons.ContainsKey(def))
        {
            Reply(player, $"Item {def} is not a weapon, knife or gloves this plugin can skin.");
            return;
        }

        var loadout = GetLoadout(player);
        if (isKnife) loadout.Knife = def;
        if (isGloves) loadout.Gloves = def;
        loadout.Skins[def] = imported.Skin;
        _store.Save();

        var name = _catalog.Find(def, imported.Skin.Paint)?.Name ?? ItemData.NameOf(def);
        Reply(player, $"Imported {name} (float {imported.Skin.Wear.ToString("0.########", CultureInfo.InvariantCulture)}, " +
                      $"seed {imported.Skin.Seed}{(imported.Skin.Stickers.Count > 0 ? $", {imported.Skin.Stickers.Count} stickers" : "")}).");

        if (isGloves)
        {
            ApplyGloves(player);
            return;
        }

        GiveOrRefresh(player, def, isKnife);
    }

    /// <summary>Refreshes the weapon if the player has it, otherwise gives it (replacing whatever is in that slot).</summary>
    private void GiveOrRefresh(CCSPlayerController player, ushort def, bool isKnife)
    {
        var existing = FindPlayerWeapon(player, w => isKnife
            ? ItemData.IsKnifeClassname(w.DesignerName)
            : w.AttributeManager.Item.ItemDefinitionIndex == def);
        if (existing != null)
        {
            RefreshWeapon(player, existing);
            return;
        }

        var classname = isKnife ? (player.Team == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife") : ItemData.Weapons[def];
        var slot = isKnife ? gear_slot_t.GEAR_SLOT_KNIFE : ItemData.Pistols.Contains(def) ? gear_slot_t.GEAR_SLOT_PISTOL : gear_slot_t.GEAR_SLOT_RIFLE;
        FindPlayerWeapon(player, w => w.As<CCSWeaponBase>().VData?.GearSlot == slot)?.AddEntityIOEvent("Kill", null, null, "", 0.01f);

        AddTimer(0.1f, () =>
        {
            if (!player.IsValid || !player.PawnIsAlive) return;
            player.GiveNamedItem(classname);
            player.ExecuteClientCommand($"slot{(slot == gear_slot_t.GEAR_SLOT_KNIFE ? 3 : slot == gear_slot_t.GEAR_SLOT_PISTOL ? 2 : 1)}");
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    [ConsoleCommand("css_knife", "Pick a knife: !knife <name | default | list>")]
    [CommandHelper(minArgs: 1, usage: "<name | default | list>", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnKnifeCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!CheckAllowed(player)) return;

        var name = cmd.GetArg(1);
        var loadout = GetLoadout(player!);

        if (name.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Reply(player!, "Knives: " + string.Join(", ", ItemData.Knives.Keys));
            return;
        }

        if (name.Equals("default", StringComparison.OrdinalIgnoreCase)) loadout.Knife = 0;
        else if (ItemData.Knives.TryGetValue(name, out var defIndex)) loadout.Knife = defIndex;
        else
        {
            Reply(player!, $"Unknown knife \"{name}\". Try !knife list");
            return;
        }

        var knife = FindPlayerWeapon(player!, w => ItemData.IsKnifeClassname(w.DesignerName));
        var msg = loadout.Knife == 0 ? "Knife reset to default" : $"Knife set to {name}. Hold it and use !skin to paint it.";
        if (knife != null) SaveAndRefresh(player!, knife, msg);
        else { _store.Save(); Reply(player!, msg); }
    }

    [ConsoleCommand("css_gloves", "Pick gloves: !gloves <type> <paint id | name> [wear] [seed] | off | list")]
    [CommandHelper(minArgs: 1, usage: "<type> <paint id | name> [wear] [seed] | off | list", whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGlovesCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (!CheckAllowed(player)) return;

        var args = Args(cmd);
        var loadout = GetLoadout(player!);

        if (args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            Reply(player!, "Gloves: " + string.Join(", ", ItemData.Gloves.Keys));
            return;
        }
        if (args[0].Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            loadout.Gloves = 0;
            _store.Save();
            Reply(player!, "Gloves reset - takes effect on your next respawn.");
            return;
        }
        if (!ItemData.Gloves.TryGetValue(args[0], out var defIndex))
        {
            Reply(player!, $"Unknown gloves \"{args[0]}\". Try !gloves list");
            return;
        }
        if (args.Length < 2)
        {
            Reply(player!, "Usage: !gloves <type> <paint id | name> [wear] [seed]");
            return;
        }

        int paint;
        int next;
        if (int.TryParse(args[1], out paint))
        {
            next = 2;
        }
        else
        {
            var query = string.Join(' ', args.Skip(1).TakeWhile(a => !IsNumber(a)));
            next = 1 + query.Split(' ').Length;
            var matches = _catalog.Search(query, defIndex);
            if (matches.Count != 1)
            {
                Reply(player!, matches.Count == 0 ? $"No glove skin matches \"{query}\"." : $"{matches.Count} matches, use the ID:");
                foreach (var m in matches.Take(8))
                    player!.PrintToChat($"  {ChatColors.Lime}{m.Paint}{ChatColors.Default} {m.Name}");
                return;
            }
            paint = matches[0].Paint;
        }

        loadout.Gloves = defIndex;
        var skin = loadout.GetOrCreate(defIndex);
        skin.Paint = paint;
        if (args.Length > next && TryParseWear(args[next], out var wear)) skin.Wear = wear;
        if (args.Length > next + 1 && int.TryParse(args[next + 1], out var seed)) skin.Seed = seed;
        _store.Save();

        ApplyGloves(player!);
        Reply(player!, $"{args[0]} gloves -> paint {paint} {DescribeSkin(defIndex, skin)}");
    }

    [ConsoleCommand("css_skinsearch", "Search skin names (needs the skins folder): !skinsearch <text>")]
    [CommandHelper(minArgs: 1, usage: "<text>")]
    public void OnSkinSearchCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (_catalog.Count == 0)
        {
            cmd.ReplyToCommand("No skin names installed in the plugin's skins folder - see README.");
            return;
        }

        var matches = _catalog.Search(cmd.ArgString.Trim('"')).Take(10).ToList();
        if (matches.Count == 0) cmd.ReplyToCommand("No matches.");
        foreach (var m in matches)
            cmd.ReplyToCommand($"{m.Paint,5}  {m.Name}{(m.Legacy ? "  (legacy model)" : "")}");
    }

    [ConsoleCommand("css_skinhelp", "List LAN Skin Tester commands")]
    public void OnSkinHelpCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        string[] lines =
        [
            "!skin <id|name> [wear] [seed] - skin the weapon in your hands",
            "!wear <0-1>  !seed <0-1000>  !st [kills|off]  !nametag <text|off>",
            "!legacy - toggle legacy model if a skin looks broken",
            "!knife <name|default|list>  !gloves <type> <id|name> [wear] [seed]",
            "!skinsearch <text>  !skinreset",
            "!gen <gen code> or !inspect <link> - import a real item (float, pattern, stickers)",
            "!give <weapon>  !3p / !fp  !respawn  !noclip - practice",
            "!hacks - hacker mode status",
        ];
        foreach (var line in lines) cmd.ReplyToCommand(line);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private const string GuidelinesMessage =
        "Skins are blocked by CounterStrikeSharp: set \"FollowCS2ServerGuidelines\": false in " +
        "addons/counterstrikesharp/configs/core.json and restart the server (the launcher does this for you).";

    /// <summary>Why skins can't be applied right now, or null if they can.</summary>
    private string? DisabledReason()
    {
        // With this CounterStrikeSharp option on, writing item IDs throws, so only the knife model would change.
        if (CoreConfig.FollowCS2ServerGuidelines) return GuidelinesMessage;
        if (!Config.RequireSvLan || IsLanServer() || _privateServer) return null;
        return "Skins are disabled: this plugin only runs on LAN servers (sv_lan 1) or a private friends server.";
    }

    private static bool IsLanServer()
    {
        var cvar = ConVar.Find("sv_lan");
        if (cvar == null) return false;
        try { return cvar.GetPrimitiveValue<bool>(); }
        catch { return cvar.StringValue == "1"; }
    }

    private bool SkinsAllowed() => DisabledReason() == null;

    private bool CheckAllowed(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return false;

        var reason = DisabledReason();
        if (reason == null) return true;

        Reply(player, reason);
        return false;
    }

    private bool TryGetHeldWeapon(CCSPlayerController? player, out CBasePlayerWeapon weapon)
    {
        weapon = null!;
        if (!CheckAllowed(player)) return false;

        var active = player!.PlayerPawn.Value?.WeaponServices?.ActiveWeapon.Value;
        if (!player.PawnIsAlive || active == null || !active.IsValid)
        {
            Reply(player, "Hold the weapon you want to change (you must be alive).");
            return false;
        }

        var defIndex = active.AttributeManager.Item.ItemDefinitionIndex;
        if (!ItemData.Weapons.ContainsKey(defIndex) && !ItemData.IsKnifeClassname(active.DesignerName))
        {
            Reply(player, "This item can't have a skin.");
            return false;
        }

        weapon = active;
        return true;
    }

    private bool TryGetHeldSkin(CCSPlayerController? player, out CBasePlayerWeapon weapon, out SkinSettings skin)
    {
        skin = null!;
        if (!TryGetHeldWeapon(player, out weapon)) return false;

        var defIndex = weapon.AttributeManager.Item.ItemDefinitionIndex;
        if (!GetLoadout(player!).Skins.TryGetValue(defIndex, out skin!) || skin.Paint == 0)
        {
            Reply(player!, "Set a skin first with !skin <id|name>.");
            return false;
        }
        return true;
    }

    private static CBasePlayerWeapon? FindPlayerWeapon(CCSPlayerController player, Func<CBasePlayerWeapon, bool> predicate)
    {
        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        if (weapons == null) return null;

        foreach (var handle in weapons)
        {
            var w = handle.Value;
            if (w != null && w.IsValid && predicate(w)) return w;
        }
        return null;
    }

    private static CCSPlayerController? FindWeaponOwner(CBasePlayerWeapon weapon)
    {
        if (weapon.OriginalOwnerXuidLow != 0)
        {
            var byXuid = Utilities.GetPlayers().FirstOrDefault(p => (uint)p.SteamID == weapon.OriginalOwnerXuidLow);
            if (byXuid != null) return byXuid;
        }

        var pawn = weapon.OwnerEntity.Value?.As<CCSPlayerPawn>();
        return pawn is { IsValid: true } ? pawn.Controller.Value?.As<CCSPlayerController>() : null;
    }

    private static bool IsHumanPlayer(CCSPlayerController player) =>
        player.IsValid && !player.IsBot && !player.IsHLTV;

    // LAN clients without Steam have SteamID 0, so fall back to the player name.
    // The host (on this PC, or the launcher's Steam account) always gets the launcher's loadout.
    private PlayerLoadout GetLoadout(CCSPlayerController player)
    {
        if (SharedLoadout || IsHost(player)) return _store.Get(LoadoutStore.LocalKey);
        return _store.Get(player.SteamID != 0 ? player.SteamID.ToString() : "name:" + player.PlayerName);
    }

    private bool IsHost(CCSPlayerController player)
    {
        if (_hostAccountId != 0 && (uint)player.SteamID == _hostAccountId) return true;
        var ip = player.IpAddress ?? "";
        return ip.StartsWith("loopback", StringComparison.OrdinalIgnoreCase) || ip.StartsWith("127.");
    }

    private void SaveAndRefresh(CCSPlayerController player, CBasePlayerWeapon weapon, string message)
    {
        _store.Save();
        RefreshWeapon(player, weapon);
        Reply(player, message);
    }

    private void Reply(CCSPlayerController player, string message) =>
        player.PrintToChat($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} {message}");

    private string DescribeSkin(ushort defIndex, SkinSettings skin)
    {
        var name = _catalog.Find(defIndex, skin.Paint)?.Name;
        var details = $"(wear {skin.Wear.ToString(CultureInfo.InvariantCulture)} {ItemData.WearName(skin.Wear)}, seed {skin.Seed})";
        return name != null ? $"[{name}] {details}" : details;
    }

    // Without a catalog entry, guess: most gun finishes use the legacy mesh, most knife finishes don't.
    private bool IsLegacy(ushort defIndex, SkinSettings skin, bool isKnife) =>
        skin.Legacy ?? _catalog.Find(defIndex, skin.Paint)?.Legacy ?? !isKnife;

    private static string[] Args(CommandInfo cmd) =>
        Enumerable.Range(1, cmd.ArgCount - 1).Select(cmd.GetArg).ToArray();

    private static bool IsNumber(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    private static bool TryParseWear(string s, out float wear)
    {
        if (float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out wear) && wear is >= 0 and <= 1)
        {
            wear = Math.Max(wear, 0.00001f);
            return true;
        }
        return false;
    }

    /// <summary>
    /// CAttributeList::SetOrAddAttributeValueByName is not part of CounterStrikeSharp's gamedata,
    /// so the signature ships in gamedata/lanskintester.json. Update that file after a CS2 update breaks it.
    /// </summary>
    private MemoryFunctionVoid<nint, string, float>? LoadSetAttributeFunction()
    {
        const string key = "CAttributeList_SetOrAddAttributeValueByName";
        var path = Path.Combine(ModuleDirectory, "gamedata", "lanskintester.json");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : "linux";
            var signature = doc.RootElement.GetProperty(key).GetProperty("signatures").GetProperty(os).GetString();
            if (string.IsNullOrWhiteSpace(signature)) throw new InvalidDataException($"empty {os} signature");

            return new MemoryFunctionVoid<nint, string, float>(signature, Addresses.ServerPath);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Could not load {Key} from {Path}. Weapon skins fall back to legacy fields; gloves and StatTrak will not work.",
                key, path);
            return null;
        }
    }
}
