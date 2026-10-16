using System.Text.Json;
using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;

namespace LanSkinTester;

/// <summary>
/// Case opening in game: !cases lists the real CS2 weapon cases, !case &lt;number or name&gt; spins one on
/// the screen with Valve's published odds and equips what you unbox (random float in the skin's range,
/// random pattern, 10% StatTrak). The case list (cases\cases.json) is written by the launcher from the
/// item data. Nothing touches your Steam inventory: the item exists only on this server, like any skin here.
/// </summary>
public partial class LanSkinTester
{
    private sealed class CaseDrop
    {
        [JsonPropertyName("def")] public ushort Def { get; set; }
        [JsonPropertyName("paint")] public int Paint { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("rarity")] public string Rarity { get; set; } = "";
        [JsonPropertyName("min")] public float Min { get; set; }
        [JsonPropertyName("max")] public float Max { get; set; } = 1;
    }

    private sealed class WeaponCase
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("items")] public List<CaseDrop> Items { get; set; } = new();
        [JsonPropertyName("rare")] public List<CaseDrop> Rare { get; set; } = new();
    }

    private List<WeaponCase>? _cases;
    private readonly HashSet<int> _opening = new();

    /// <summary>Valve's odds: Mil-Spec 79.92%, Restricted 15.98%, Classified 3.2%, Covert 0.64%, knife/gloves 0.26%.</summary>
    private static readonly (string Rarity, double Chance)[] CaseOdds =
    [
        ("Mil-Spec Grade", 0.7992), ("Restricted", 0.1598), ("Classified", 0.032), ("Covert", 0.0064), ("Rare", 0.0026),
    ];

    private static readonly Dictionary<string, string> RarityHtml = new()
    {
        ["Mil-Spec Grade"] = "#4b69ff", ["Restricted"] = "#8847ff", ["Classified"] = "#d32ce6", ["Covert"] = "#eb4b4b", ["Rare"] = "#ffd700",
    };

    private List<WeaponCase> Cases()
    {
        if (_cases != null) return _cases;
        var file = Path.Combine(ModuleDirectory, "cases", "cases.json");
        try
        {
            _cases = File.Exists(file) ? JsonSerializer.Deserialize<List<WeaponCase>>(File.ReadAllText(file)) ?? new() : new();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            _cases = new();
        }
        return _cases;
    }

    [ConsoleCommand("css_cases", "List the weapon cases you can open: !cases [page]")]
    public void OnCasesCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid) return;
        var cases = Cases();
        if (cases.Count == 0)
        {
            Reply(player, "No case list yet: in the launcher press Update on Setup › Skin, sticker and charm lists.");
            return;
        }
        const int perPage = 10;
        var pages = (cases.Count + perPage - 1) / perPage;
        var page = int.TryParse(cmd.GetArg(1), out var p) ? Math.Clamp(p, 1, pages) : 1;
        Reply(player, $"Cases (page {page}/{pages}) - open one with !case <number>:");
        for (var i = (page - 1) * perPage; i < Math.Min(cases.Count, page * perPage); i++)
            player.PrintToChat($"  {ChatColors.Gold}{i + 1}{ChatColors.Default} {cases[i].Name}");
        if (page < pages) player.PrintToChat($"  {ChatColors.Grey}!cases {page + 1} for more");
    }

    [ConsoleCommand("css_case", "Open a weapon case: !case <number | name>")]
    public void OnCaseCommand(CCSPlayerController? player, CommandInfo cmd)
    {
        if (player == null || !player.IsValid || !CheckAllowed(player)) return;
        var cases = Cases();
        if (cases.Count == 0)
        {
            Reply(player, "No case list yet: in the launcher press Update on Setup › Skin, sticker and charm lists.");
            return;
        }
        var arg = string.Join(' ', Enumerable.Range(1, Math.Max(0, cmd.ArgCount - 1)).Select(cmd.GetArg)).Trim();
        var chosen = arg == "" ? cases[Random.Shared.Next(cases.Count)]
            : int.TryParse(arg, out var n) && n >= 1 && n <= cases.Count ? cases[n - 1]
            : cases.FirstOrDefault(c => c.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
        if (chosen == null)
        {
            Reply(player, $"No case called \"{arg}\". See !cases");
            return;
        }
        if (!_opening.Add(player.Slot))
        {
            Reply(player, "Wait for the case you're opening.");
            return;
        }
        OpenCase(player, chosen);
    }

    private void OpenCase(CCSPlayerController player, WeaponCase box)
    {
        var drop = RollDrop(box);
        // The reel: random items from the case, slowing down, ending on the drop.
        var reel = Enumerable.Range(0, 24).Select(_ => RollDrop(box)).Append(drop).ToList();
        var delay = 0f;
        for (var i = 0; i < reel.Count; i++)
        {
            var item = reel[i];
            var last = i == reel.Count - 1;
            delay += 0.06f + 0.012f * i * (i / (float)reel.Count);
            AddTimer(delay, () =>
            {
                if (!player.IsValid) return;
                var color = RarityHtml.GetValueOrDefault(RarityOf(item, box), "#ffffff");
                player.PrintToCenterHtml($"<font class='fontSize-m' color='#aaaaaa'>{System.Net.WebUtility.HtmlEncode(box.Name)}</font><br>" +
                                         $"<font class='fontSize-l' color='{color}'>{(last ? "» " : "")}{System.Net.WebUtility.HtmlEncode(item.Name)}{(last ? " «" : "")}</font>",
                                         last ? 5 : 1);
                if (!player.IsBot) player.ExecuteClientCommand(last ? "play sounds/ui/item_drop_personal" : "play sounds/ui/csgo_ui_crate_item_scroll");
            }, TimerFlags.STOP_ON_MAPCHANGE);
        }
        AddTimer(delay + 0.4f, () =>
        {
            _opening.Remove(player.Slot);
            if (player.IsValid) Unbox(player, box, drop);
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private CaseDrop RollDrop(WeaponCase box)
    {
        var roll = Random.Shared.NextDouble();
        foreach (var (rarity, chance) in CaseOdds)
        {
            roll -= chance;
            if (roll > 0) continue;
            var pool = rarity == "Rare" ? box.Rare : box.Items.Where(i => i.Rarity == rarity).ToList();
            if (pool.Count > 0) return pool[Random.Shared.Next(pool.Count)];
        }
        // A case without that grade: the most common one it has.
        var common = box.Items.Where(i => i.Rarity == "Mil-Spec Grade").ToList();
        return (common.Count > 0 ? common : box.Items)[Random.Shared.Next(common.Count > 0 ? common.Count : box.Items.Count)];
    }

    private static string RarityOf(CaseDrop item, WeaponCase box) => box.Rare.Contains(item) ? "Rare" : item.Rarity;

    /// <summary>Equips the unboxed item and tells everyone.</summary>
    private void Unbox(CCSPlayerController player, WeaponCase box, CaseDrop drop)
    {
        var isKnife = ItemData.Knives.ContainsValue(drop.Def);
        var isGloves = ItemData.Gloves.ContainsValue(drop.Def);
        var statTrak = !isGloves && Random.Shared.NextDouble() < 0.10;
        var wear = Math.Clamp(drop.Min + (float)Random.Shared.NextDouble() * (drop.Max - drop.Min), 0.00001f, 0.99999f);

        var loadout = GetLoadout(player);
        var skin = new SkinSettings
        {
            Paint = drop.Paint,
            Wear = wear,
            Seed = Random.Shared.Next(0, 1001),
            StatTrak = statTrak,
        };
        if (drop.Paint != 0 || isKnife) loadout.Skins[drop.Def] = skin;
        if (isKnife) loadout.Knife = drop.Def;
        if (isGloves) loadout.Gloves = drop.Def;
        _store.Save();

        var rarity = RarityOf(drop, box);
        var chatColor = rarity switch
        {
            "Rare" => ChatColors.Gold, "Covert" => ChatColors.Red, "Classified" => ChatColors.Magenta,
            "Restricted" => ChatColors.Purple, _ => ChatColors.Blue,
        };
        var name = (statTrak ? "StatTrak™ " : "") + (isKnife || isGloves ? "★ " : "") + drop.Name;
        Server.PrintToChatAll($" {ChatColors.Green}{Config.ChatPrefix}{ChatColors.Default} {player.PlayerName} unboxed {chatColor}{name}{ChatColors.Default} ({ItemData.WearName(wear)}) from the {box.Name}!");

        if (!player.PawnIsAlive) return;
        if (isGloves)
        {
            ApplyGloves(player);
            return;
        }
        // Hand over the gun (or refresh the knife) so it shows right away.
        var current = FindPlayerWeapon(player, w => isKnife ? ItemData.IsKnifeClassname(w.DesignerName) : w.AttributeManager.Item.ItemDefinitionIndex == drop.Def);
        if (current != null) RefreshWeapon(player, current);
        else if (ItemData.Weapons.TryGetValue(drop.Def, out var classname)) player.GiveNamedItem(classname);
    }
}
