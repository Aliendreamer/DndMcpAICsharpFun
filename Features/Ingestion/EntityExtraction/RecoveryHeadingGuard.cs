using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Deterministic name-shape guard for the automatic decline-recovery pass: rejects section and
/// category headings BEFORE they are re-offered to the model.
/// <para>
/// The pass re-offers a declined candidate a <c>[Rule, Lore, none]</c> union and relies on the model
/// answering <c>none</c> for headings. On a book organised into per-guild sections that failed at
/// scale — Guildmasters' Guide to Ravnica produced 97 recovered entities, ALL of them Lore/Rule, 86 of
/// them headings (<c>Boros Creatures</c>, <c>Golgari NPCs</c>, <c>Zombies</c>,
/// <c>Chapter 5: Treasures</c>), i.e. 34% of the book's entities. Whether a name is a heading is
/// decidable from the string; it should not cost a model call or depend on one.
/// </para>
/// <para>
/// Deliberately SEPARATE from <see cref="ExtractionSignatures.IsEntityLikeName"/>, and deliberately
/// more aggressive. That gate guards the primary path, where a false positive destroys a real entity,
/// so it stays conservative. Here every candidate has ALREADY been declined by the main gate, so a
/// false positive merely leaves it declined while a false negative fabricates an entity — the opposite
/// risk profile, and the reason these patterns must not be shared.
/// </para>
/// <para>
/// The vocabulary is EMPIRICAL — drawn from the real recovery output of all three setting books, not
/// one book's shape. It is a floor, not a proof: anything it misses still reaches the model's
/// <c>none</c> option, exactly as before. Enumerating every possible heading would be the
/// keyword whack-a-mole this codebase has repeatedly found to plateau.
/// </para>
/// </summary>
public static partial class RecoveryHeadingGuard
{
    /// <summary>Whether the candidate name is a section/category heading rather than an entity name.</summary>
    public static bool IsSectionHeading(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();

        return CategoryNounSuffix().IsMatch(n)
            || CategoryNounGroupedBy().IsMatch(n)
            || BareCreatureTypePlural().IsMatch(n)
            || BookStructureHeading().IsMatch(n)
            || TruncatedPossessive().IsMatch(n);
    }

    // "<Anything> Creatures / NPCs / Items / Spells / Actions / Lairs / Descriptions / Traits / …".
    // End-anchored on the noun so a real entity merely CONTAINING one of these words is untouched.
    // Creature-type plurals count as category nouns in the SUFFIX position too ("Boros Angels",
    // "Izzet Weirds", "Winged Felidars") — a generalisation of the same rule rather than new
    // vocabulary. Real creatures are named in the singular, so the plural suffix is the tell.
    [GeneratedRegex(
        @"\b(creatures|npcs|items|spells|actions|lairs|options?|features|proficiencies|descriptions|properties|traits|personality|increases|bestiary" +
        @"|angels|weirds|felidars|demons|devils|dragons|drakes|giants|horrors|hydras|thrulls|vampires|zombies|aberrations|beasts|elementals|monstrosities)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex CategoryNounSuffix();

    // The category noun LEADS instead of trailing, grouped by a preposition: "NPCs By Guild",
    // "Magic Items of Wildemount". Requires the preposition so a real entity beginning with a
    // category word ("Items of Power" would be caught, but "Item" singular titles are not) needs the
    // grouping shape to qualify.
    [GeneratedRegex(
        @"^(?:magic\s+)?(?:creatures|npcs|items|spells|actions|lairs|traits|features)\s+(?:by|of|in|per)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex CategoryNounGroupedBy();

    // The WHOLE name is one or two creature-type plurals, optionally joined by "and"
    // ("Zombies", "Insects and Arachnids"). Whole-name anchoring is what keeps the singular real
    // creature ("Zombie") and any longer real title admitted — a name is not a category merely
    // because it ends in "s".
    [GeneratedRegex(
        @"^(?:aberrations|angels|arachnids|beasts|celestials|constructs|demons|devils|dragons|drakes|elementals|fey|fiends|fungus|giants|horrors|humanoids|hydras|insects|krasis|monstrosities|oozes|plants|slaadi|sphinxes|thrulls|undead|vampires|weirds|zombies)" +
        @"(?:\s+and\s+(?:aberrations|angels|arachnids|beasts|celestials|constructs|demons|devils|dragons|drakes|elementals|fey|fiends|fungus|giants|horrors|humanoids|hydras|insects|krasis|monstrosities|oozes|plants|slaadi|sphinxes|thrulls|undead|vampires|weirds|zombies))?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex BareCreatureTypePlural();

    [GeneratedRegex(@"^(chapter\s+\d+\b|appendix\b|(bestiary|races)\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex BookStructureHeading();

    // A heading cut off after the possessive: the name is ONLY "<Name>'s" with nothing following.
    [GeneratedRegex(@"^[\p{L}\p{M}\s\-]+['’]s\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TruncatedPossessive();
}