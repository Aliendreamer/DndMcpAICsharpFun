using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class HeadingCategoryClassifier
{
    public static ContentCategory Guess(string title)
    {
        var t = title.ToLowerInvariant();

        if (Contains(t, "spell")) return ContentCategory.Spell;
        if (ContainsAny(t, "monster", "bestiary", "creature",
                            "aberration", "beast", "celestial", "dragon", "elemental",
                            "fey", "fiend", "giant", "humanoid", "monstrosit",
                            "ooze", "plant", "undead", "npc", "nonplayer character"))
            return ContentCategory.Monster;
        if (ContainsAny(t, "equipment", "gear", "weapon", "armor", "armour", "magic item")) return ContentCategory.Item;
        // Class first — "Class Features" should land on Class, not Trait.
        if (ContainsAny(t, "class", "barbarian", "bard", "cleric", "druid", "fighter", "monk", "paladin", "ranger", "rogue", "sorcerer", "warlock", "wizard")) return ContentCategory.Class;
        if (ContainsAny(t, "feat", "trait", "personality")) return ContentCategory.Trait;
        if (ContainsAny(t, "background")) return ContentCategory.Background;
        if (ContainsAny(t, "race", "species")) return ContentCategory.Race;
        if (ContainsAny(t, "condition")) return ContentCategory.Condition;
        if (ContainsAny(t, "god", "deity", "deities", "pantheon")) return ContentCategory.God;
        if (ContainsAny(t, "plane", "cosmology", "multiverse")) return ContentCategory.Plane;
        if (ContainsAny(t, "treasure", "loot", "hoard")) return ContentCategory.Treasure;
        if (ContainsAny(t, "encounter", "dungeon", "random encounter")) return ContentCategory.Encounter;
        if (ContainsAny(t, "trap")) return ContentCategory.Trap;
        if (ContainsAny(t, "lore", "history", "world")) return ContentCategory.Lore;
        if (ContainsAny(t, "combat", "attack")) return ContentCategory.Combat;
        if (ContainsAny(t, "adventuring", "exploration", "resting", "travel", "adventure environment")) return ContentCategory.Adventuring;

        return ContentCategory.Rule;
    }

    /// <summary>
    /// Returns a small ranked set of plausible categories for the heading: the primary keyword
    /// guess first, then its empirical confusion set, then a frequency floor of the most common
    /// types. Used as a PRIOR to prune the discriminated-union extraction schema — it does not
    /// decide the final type. The decline ("none") branch is always added by the union builder,
    /// never here, so a mis-prune degrades to a decline rather than a fabrication.
    /// </summary>
    public static IReadOnlyList<ContentCategory> GuessRanked(string title) => ExpandPrior(Guess(title));

    /// <summary>
    /// Expands a primary category into the same ranked prior set used by <see cref="GuessRanked"/>,
    /// for callers (e.g. the scanner) that already hold a category from the TOC rather than a title.
    /// </summary>
    public static IReadOnlyList<ContentCategory> ExpandPrior(ContentCategory primary)
    {
        var ranked = new List<ContentCategory> { primary };
        foreach (var c in ConfusionSet(primary)) AddDistinct(ranked, c);
        foreach (var c in FrequencyFloor) AddDistinct(ranked, c);
        return ranked;
    }

    // The ~90%-of-corpus common types, always offered so the model can pick them.
    private static readonly ContentCategory[] FrequencyFloor =
    {
        ContentCategory.Monster, ContentCategory.Spell, ContentCategory.Item, ContentCategory.Class,
    };

    // Empirical confusions from the SRD analysis (parent prose-grounded-knowledge-model design.md §A):
    // race/cantrip/magic-item content force-typed as Monster; class sections as Rule.
    private static ContentCategory[] ConfusionSet(ContentCategory primary) => primary switch
    {
        ContentCategory.Monster => new[] { ContentCategory.Race, ContentCategory.Spell, ContentCategory.Item },
        ContentCategory.Race => new[] { ContentCategory.Monster },
        ContentCategory.Class => new[] { ContentCategory.Rule },
        ContentCategory.Item => new[] { ContentCategory.Treasure },
        _ => Array.Empty<ContentCategory>(),
    };

    private static void AddDistinct(List<ContentCategory> list, ContentCategory category)
    {
        if (!list.Contains(category)) list.Add(category);
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