using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

/// <summary>
/// Every name here is REAL output from the automatic decline-recovery pass on the three setting books
/// (GGR produced 97, EGW 10, SCC 10). GGR's 86 headings were 34% of that book's entities. The observed
/// names ARE the specification — see openspec/changes/decline-recovery-heading-guard.
/// </summary>
public sealed class RecoveryHeadingGuardTests
{
    // ── Rejected: category-noun suffix ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("Boros Creatures")]
    [InlineData("Azorius Creatures")]
    [InlineData("Guildless Creatures")]
    [InlineData("Golgari NPCs")]
    [InlineData("SIMIC NPCs")]
    // category noun LEADS, grouped by a preposition
    [InlineData("NPCs By Guild")]
    [InlineData("Magic Items of Wildemount")]
    [InlineData("Azorius Items")]
    [InlineData("Rakdos Items")]
    [InlineData("Domain Spells")]
    [InlineData("Circle Spells")]
    [InlineData("Blade Juggler Actions")]
    [InlineData("High-Wire Acrobat Actions")]
    [InlineData("Golgari Lairs")]
    [InlineData("Subclass Options")]
    [InlineData("Bonus Proficiencies")]
    // observed in EGW / SCC, not GGR — the vocabulary must not be one book's shape
    [InlineData("Artifact Descriptions")]
    [InlineData("Betrayer Artifact Properties")]
    [InlineData("Owlin Traits")]
    [InlineData("Prismari Personality")]
    [InlineData("Quandrix Personality")]
    [InlineData("Ability Score Increases")]
    [InlineData("Race Option")]
    [InlineData("Wildemount Bestiary")]
    public void Rejects_category_noun_suffix(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeTrue();

    // ── Rejected: bare creature-type plural ──────────────────────────────────────────────────
    [Theory]
    [InlineData("Zombies")]
    [InlineData("Aberrations")]
    [InlineData("Beasts")]
    [InlineData("Demons")]
    [InlineData("Giants")]
    [InlineData("Humanoids")]
    [InlineData("Monstrosities")]
    [InlineData("Vampires")]
    [InlineData("Angels")]
    [InlineData("Horrors")]
    [InlineData("Hydras")]
    [InlineData("Insects and Arachnids")]
    public void Rejects_bare_creature_type_plural(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeTrue();

    // ── Rejected: book-structure heading ─────────────────────────────────────────────────────
    [Theory]
    [InlineData("Chapter 5: Treasures")]
    [InlineData("Chapter 6: Friends and Foes")]
    [InlineData("Bestiary")]
    [InlineData("Appendix D: Creature Statistics")]
    public void Rejects_book_structure_heading(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeTrue();

    // ── Rejected: truncated possessive ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("Aurelia's")]
    [InlineData("Trostani's")]
    [InlineData("Isperia's")]
    [InlineData("LAZAV's")]
    [InlineData("Zegana's")]
    public void Rejects_truncated_possessive(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeTrue();

    // ── ADMITTED: the 14 named GGR legendary NPCs the same pass recovered ────────────────────
    // These are REAL content. A guard that suppresses them is worse than the bug it fixes.
    [Theory]
    [InlineData("Teysa Karlov")]
    [InlineData("Mizzix of the Izmagnus")]
    [InlineData("Prime Speaker Zegana")]
    [InlineData("Warleader Aurelia")]
    [InlineData("Lazav the Faceless")]
    [InlineData("Supreme Judge Isperia")]
    [InlineData("Master Chemister Grozgrox")]
    [InlineData("Nikya of the Old Ways")]
    [InlineData("Izoni, Devkarin Matka")]
    [InlineData("Lavinia, Steward of the Guildpact")]
    [InlineData("Ludmilla, the Tunnel Viper")]
    [InlineData("Emmara Tandris")]
    [InlineData("Firemane Nevena")]
    [InlineData("Storrev")]
    [InlineData("Azorius Charm")]
    public void Admits_named_npcs_and_real_entities(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeFalse();

    // ── ADMITTED: genuine rule/lore prose the pass exists to recover ─────────────────────────
    [Theory]
    [InlineData("Psychic Wind Effects")]
    [InlineData("Loose Pantheons")]
    [InlineData("Vestiges of Divergence")]
    [InlineData("Elder Evils of Exandria")]
    public void Admits_genuine_rule_and_lore(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeFalse();

    // ── The singular/plural discriminator: a name is not a category just because it ends in s ─
    [Theory]
    [InlineData("Zombie")]
    [InlineData("Giant Insect")]
    [InlineData("Sphinx of Judgment")]
    [InlineData("Moorbounder")]
    [InlineData("Acheron Blade")]
    [InlineData("Gravity Sinkhole")]
    [InlineData("Echo Knight")]
    public void Admits_singular_and_real_entity_names(string name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Admits_blank_rather_than_throwing(string? name) =>
        RecoveryHeadingGuard.IsSectionHeading(name).Should().BeFalse();
}