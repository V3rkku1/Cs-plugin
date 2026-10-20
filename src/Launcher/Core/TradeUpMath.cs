namespace LanSkinTester.Launcher.Core;

/// <summary>CS2's Trade Up Contract odds and float, apart from the inventory so they can be tested.</summary>
public static class TradeUpMath
{
    /// <summary>
    /// The ticket rule: every input adds one ticket for each next-grade item of its own collection, and one
    /// ticket wins. Each ticket comes back with its chance (an item from two inputs' collections is listed twice).
    /// </summary>
    public static List<(T Item, double Chance)> Tickets<T>(IEnumerable<IReadOnlyList<T>> outcomesPerInput)
    {
        var tickets = outcomesPerInput.SelectMany(o => o).ToList();
        return tickets.Select(t => (t, 1.0 / tickets.Count)).ToList();
    }

    /// <summary>
    /// CS2's current float: each input's float put on its own skin's range (0 its best, 1 its worst), averaged,
    /// and placed at that point of the new skin's range.
    /// </summary>
    public static float Float(IEnumerable<(float Wear, float Min, float Max)> inputs, float min, float max)
    {
        var normalized = inputs.Average(i => i.Max > i.Min ? Math.Clamp((i.Wear - i.Min) / (i.Max - i.Min), 0f, 1f) : 0f);
        return (float)Math.Clamp(min + normalized * (max - min), 0.000001, 0.999999);
    }
}
