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

    private static bool Contains(string text, string keyword) =>
        text.Contains(keyword, StringComparison.Ordinal);

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var k in keywords)
            if (text.Contains(k, StringComparison.Ordinal)) return true;
        return false;
    }
}
