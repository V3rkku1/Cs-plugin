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

public class LanSkinTester : BasePlugin, IPluginConfig<LanSkinTesterConfig>
{
    public override string ModuleName => "LAN Skin Tester";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Cs-plugin";
    public override string ModuleDescription => "Preview CS2 weapon, knife and glove skins on your own LAN / offline server.";

    // Item IDs above what real inventories use, so the client treats the item as a custom econ item.
    private const ulong FirstCustomItemId = 65578;

    public LanSkinTesterConfig Config { get; set; } = new();

    private LoadoutStore _store = null!;
    private SkinCatalog _catalog = null!;
    private MemoryFunctionVoid<nint, string, float>? _setAttribute;
    private ulong _nextItemId = FirstCustomItemId;

    public void OnConfigParsed(LanSkinTesterConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        _store = new LoadoutStore(Path.Combine(ModuleDirectory, "loadouts.json"));
        _catalog = new SkinCatalog(Path.Combine(ModuleDirectory, "skins"));
        _setAttribute = LoadSetAttributeFunction();

        Logger.LogInformation("Loaded {Count} skin names from the skins folder", _catalog.Count);

        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        AddTimer(1.0f, CheckLauncherChanges, TimerFlags.REPEAT);
        VirtualFunctions.GiveNamedItemFunc.Hook(OnGiveNamedItemPost, HookMode.Post);
    }

    public override void Unload(bool hotReload)
    {
        VirtualFunctions.GiveNamedItemFunc.Unhook(OnGiveNamedItemPost, HookMode.Post);
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
            Logger.LogDebug(ex, "GiveNamedItem hook failed");
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
                Logger.LogDebug(ex, "Entity spawn handler failed");
            }
        });
    }

    // The launcher edits loadouts.json while the server runs; pick up its changes and re-skin everyone.
    private void CheckLauncherChanges()
    {
        if (!SkinsAllowed() || !_store.ChangedOnDisk() || !_store.TryLoad()) return;

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
            Reply(player, "Loadout updated from the launcher.");
        }
    }

    [GameEventHandler]
    public HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && IsHumanPlayer(player))
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
        if (!loadout.Skins.TryGetValue(defIndex, out var skin) || skin.Paint == 0)
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

        weapon.FallbackPaintKit = skin.Paint;
        weapon.FallbackSeed = skin.Seed;
        weapon.FallbackWear = skin.Wear;

        SetPaintAttributes(item, skin);

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
        ];
        foreach (var line in lines) cmd.ReplyToCommand(line);
    }

    // ---------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------

    private bool SkinsAllowed()
    {
        if (!Config.RequireSvLan) return true;
        var cvar = ConVar.Find("sv_lan");
        if (cvar == null) return false;
        try { return cvar.GetPrimitiveValue<bool>(); }
        catch { return cvar.StringValue == "1"; }
    }

    private bool CheckAllowed(CCSPlayerController? player)
    {
        if (player == null || !player.IsValid) return false;
        if (SkinsAllowed()) return true;

        Reply(player, "Skins are disabled: this plugin only runs on LAN servers (sv_lan 1).");
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
    private PlayerLoadout GetLoadout(CCSPlayerController player)
    {
        if (Config.SingleLoadout) return _store.Get(LoadoutStore.LocalKey);
        return _store.Get(player.SteamID != 0 ? player.SteamID.ToString() : "name:" + player.PlayerName);
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
