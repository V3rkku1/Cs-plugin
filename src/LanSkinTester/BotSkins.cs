using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace LanSkinTester;

/// <summary>
/// Bot skins without Bot Improver: every bot gets its own random loadout from the skins catalog (skins/skins.json):
/// a skin on each gun, a knife and gloves, random float and pattern, sometimes StatTrak. Kept per bot until the
/// map changes or a reroll.  lst_bot_skins 1|0|reroll
/// Skipped: agents (bots keep the default models).
/// </summary>
public partial class LanSkinTester
{
    private bool _botSkins;
    private readonly Dictionary<int, PlayerLoadout> _botLoadouts = new();

    private void LoadBotSkins() => RegisterListener<Listeners.OnMapStart>(_ => _botLoadouts.Clear());

    [ConsoleCommand("lst_bot_skins", "1 = random skins, knives and gloves on every bot, 0 = off, reroll = new ones")]
    [CommandHelper(minArgs: 1, usage: "0|1|reroll", whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnBotSkinsCommand(CCSPlayerController? caller, CommandInfo cmd)
    {
        var arg = cmd.GetArg(1).ToLowerInvariant();
        var on = arg is "1" or "reroll";
        if (on == _botSkins && arg != "reroll") return; // the cfg sends it on every apply: keep the bots' looks
        _botSkins = on;
        _botLoadouts.Clear();
        foreach (var bot in Utilities.GetPlayers().Where(p => p.IsValid && p.PawnIsAlive && IsBotPlayer(p)))
            RefreshPlayer(bot, "");
    }

    /// <summary>Who gets skins: humans, and bots while bot skins are on.</summary>
    private bool Skinned(CCSPlayerController player) => IsHumanPlayer(player) || (_botSkins && player.IsValid && IsBotPlayer(player));

    private PlayerLoadout BotLoadout(CCSPlayerController bot)
    {
        if (!_botSkins) return new PlayerLoadout();
        if (!_botLoadouts.TryGetValue(bot.Slot, out var loadout))
            _botLoadouts[bot.Slot] = loadout = RandomLoadout(_catalog, Random.Shared);
        return loadout;
    }

    internal static PlayerLoadout RandomLoadout(SkinCatalog catalog, Random random)
    {
        var knives = ItemData.Knives.Values.ToArray();
        var gloves = ItemData.Gloves.Values.ToArray();
        var loadout = new PlayerLoadout
        {
            Knife = knives[random.Next(knives.Length)],
            Gloves = gloves[random.Next(gloves.Length)],
        };
        foreach (var def in ItemData.Weapons.Keys.Append(loadout.Knife).Append(loadout.Gloves))
        {
            var paints = catalog.ForItem(def).ToArray();
            if (paints.Length == 0) continue; // vanilla: a knife or gloves without a catalog stays default
            loadout.Skins[def] = new SkinSettings
            {
                Paint = paints[random.Next(paints.Length)].Paint,
                Wear = (float)(0.01 + random.NextDouble() * 0.79),
                Seed = random.Next(1001),
                StatTrak = !ItemData.Gloves.ContainsValue(def) && random.Next(10) == 0,
            };
        }
        return loadout;
    }
}
