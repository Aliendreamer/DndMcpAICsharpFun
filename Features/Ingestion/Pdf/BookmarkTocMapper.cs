using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class BookmarkTocMapper
{
    public static IReadOnlyList<TocSectionEntry> Map(IReadOnlyList<PdfBookmark> bookmarks)
    {
        if (bookmarks.Count == 0) return [];

        var entries = new List<TocSectionEntry>(bookmarks.Count);
        foreach (var b in bookmarks)
            entries.Add(new TocSectionEntry(b.Title, GuessCategory(b.Title), b.PageNumber));
        return entries;
    }

    private static ContentCategory GuessCategory(string title)
    {
        var t = title.ToLowerInvariant();

        if (Contains(t, "spell")) return ContentCategory.Spell;
        if (ContainsAny(t, "monster", "bestiary", "creature")) return ContentCategory.Monster;
        if (ContainsAny(t, "equipment", "gear", "weapon", "armor", "armour", "magic item")) return ContentCategory.Item;
        if (ContainsAny(t, "background")) return ContentCategory.Background;
        if (ContainsAny(t, "race", "species")) return ContentCategory.Race;
        if (ContainsAny(t, "class", "barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard")) return ContentCategory.Class;
        if (ContainsAny(t, "condition")) return ContentCategory.Condition;
        if (ContainsAny(t, "god", "deity", "deities", "pantheon")) return ContentCategory.God;
        if (ContainsAny(t, "plane", "cosmology", "multiverse")) return ContentCategory.Plane;
        if (ContainsAny(t, "treasure", "loot", "hoard")) return ContentCategory.Treasure;
        if (ContainsAny(t, "encounter")) return ContentCategory.Encounter;
        if (ContainsAny(t, "trap")) return ContentCategory.Trap;
        if (ContainsAny(t, "feat", "trait", "personality")) return ContentCategory.Trait;
        if (ContainsAny(t, "lore", "history", "world")) return ContentCategory.Lore;
        if (ContainsAny(t, "combat", "attack")) return ContentCategory.Combat;
        if (ContainsAny(t, "adventuring", "exploration", "resting", "travel")) return ContentCategory.Adventuring;

        return ContentCategory.Rule;
    }

    private static bool Contains(string text, string keyword) =>
        text.Contains(keyword, StringComparison.Ordinal);

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var k in keywords)
            if (text.Contains(k, StringComparison.Ordinal)) return true;
        return false;
    }
}
