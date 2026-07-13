namespace DndMcpAICsharpFun.Features.Crafting;

/// <summary>
/// Pure, table-driven crafting math (mirrors <see cref="Encounters.EncounterMath"/>): no I/O, no DI,
/// so it can be unit-tested against the published rules. Nonmagical crafting uses the XGE downtime
/// formula (materials = half market value; workweeks = market value / 50, workweek = 5 days).
/// Magic-item crafting is the XGE "Magic Item Crafting Time and Cost" table.
/// </summary>
public static class CraftingMath
{
    /// <summary>
    /// XGE nonmagical downtime crafting. Materials cost half the item's market value; total workweeks
    /// are the market value divided by 50, split evenly across crafters; a workweek is 5 days.
    /// </summary>
    public static NonmagicalCraft CraftNonmagical(int marketValueGp, int crafters = 1)
    {
        if (marketValueGp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(marketValueGp), marketValueGp,
                "Market value must be positive.");
        }

        var effectiveCrafters = Math.Max(1, crafters);
        var materials = marketValueGp / 2;
        var totalWorkweeks = marketValueGp / 50.0;
        var perCrafter = totalWorkweeks / effectiveCrafters;
        var days = (int)Math.Ceiling(perCrafter * 5);
        return new NonmagicalCraft(materials, totalWorkweeks, perCrafter, days);
    }

    /// <summary>
    /// XGE "Magic Item Crafting Time and Cost" table (workweeks, gold). Values are the published XGE
    /// table; the table cells do not survive Marker's PDF table rendering, so they are encoded here and
    /// cited to XGE — the same precedent as EncounterMath's DMG tables.
    /// </summary>
    public static MagicItemCraft CraftMagicItem(Rarity rarity)
    {
        var (workweeks, gold) = rarity switch
        {
            Rarity.Common => (1, 50),
            Rarity.Uncommon => (2, 200),
            Rarity.Rare => (10, 2000),
            Rarity.VeryRare => (25, 20000),
            Rarity.Legendary => (50, 100000),
            _ => throw new ArgumentOutOfRangeException(nameof(rarity), rarity, "Unknown rarity.")
        };
        return new MagicItemCraft(rarity, workweeks, gold);
    }
}
