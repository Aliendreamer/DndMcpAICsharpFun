using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class ExtractionSignaturesTests
{
    [Theory]
    [InlineData("Large aberration, lawful evil  Armor Class 17  Hit Points 135  Challenge 10 (5,900 XP)", true)]
    [InlineData("Armor Class 19  Hit Points 50  damage threshold 15", false)]
    [InlineData("Aboleths are ancient horrors of the deep.", false)]
    [InlineData("", false)]
    public void IsCompleteStatBlock_matches_AC_HP_Challenge(string text, bool expected) =>
        ExtractionSignatures.IsCompleteStatBlock(text).Should().Be(expected);

    [Theory]
    [InlineData("Large object  Armor Class 15  Hit Points 50 (unbroken)", true)]          // siege weapon
    [InlineData("Huge object  Armor Class 19  Hit Points 100", true)]                     // animated object
    [InlineData("Large aberration  Armor Class 17  Hit Points 135  Challenge 10", false)] // creature: has Challenge
    [InlineData("Large object  Armor Class 15", false)]                                   // no Hit Points
    [InlineData("Large beast  Armor Class 12  Hit Points 20", false)]                     // not an "object" type
    public void IsObjectStatBlock_matches_size_object_with_AC_HP_no_Challenge(string text, bool expected) =>
        ExtractionSignatures.IsObjectStatBlock(text).Should().Be(expected);

    [Theory]
    [InlineData("Weapon (any sword that deals slashing damage), legendary (requires attunement)", true)]
    [InlineData("Wondrous item, rare", true)]
    [InlineData("Ring, uncommon (requires attunement)", true)]
    [InlineData("A sturdy leather backpack that holds 30 pounds.", false)]
    [InlineData("Fireball: a bright streak flashes to a point you choose.", false)]
    [InlineData("Huge giant, chaotic evil  Armor Class 14  Legendary Actions", false)]
    public void IsMagicItem_matches_item_signatures(string text, bool expected) =>
        ExtractionSignatures.IsMagicItem(text).Should().Be(expected);

    [Theory]
    [InlineData("Aboleth", true)]
    [InlineData("Bag of Holding", true)]
    [InlineData("Fireball", true)]
    [InlineData("FIREBALL", true)]
    [InlineData("ABOLETH", true)]
    [InlineData("BARD", true)]
    [InlineData("LION", true)]
    [InlineData("ACTIONS", false)]
    [InlineData("REACTIONS", false)]
    [InlineData("LEGENDARY ACTIONS", false)]
    [InlineData("A RED DRAGON'S LAIR", false)]
    [InlineData("Appendix D: Creature Statistics", false)]
    [InlineData("Step 2. Basic Statistics", false)]
    [InlineData("Challenge 7 (2,900 XP)", false)]
    [InlineData("Monster Features", false)]
    [InlineData("Class Features", false)]
    [InlineData("Creating a Monster", false)]
    [InlineData("CREATING A MONSTER", false)]
    [InlineData("Special Features", false)]
    // extraction-noise-name-gate: stat-block field-label names (leaked as Monster entities)
    [InlineData("Damage Immunities poison", false)]
    [InlineData("Armor Class 14 (natural armor) Hit Points 71 (13d8 + 13) Speed 30 ft.", false)]
    [InlineData("Hit Points 71 (13d8 + 13)", false)]
    [InlineData("Saving Throws Dex +5, Con +7", false)]
    [InlineData("Condition Immunities charmed, frightened", false)]
    // extraction-noise-name-gate: sidebar / lair headings
    [InlineData("AN ANARCH's LAIR", false)]
    [InlineData("Effects of the Mold", false)]
    [InlineData("Variant: Chromatic Drakes", false)]
    // known-good entities must STILL admit (no recall regression)
    [InlineData("Tortle", true)]
    [InlineData("Babau", true)]
    [InlineData("Archdruid", true)]
    [InlineData("Deep Gnome", true)]
    [InlineData("Path of the Battlerager", true)]
    [InlineData("Constrictor Snake", true)] // starts with "Con" but is NOT the field label "Condition…"
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEntityLikeName_rejects_headings_and_fragments(string? name, bool expected) =>
        ExtractionSignatures.IsEntityLikeName(name).Should().Be(expected);


    [Theory]
    [InlineData(
        "Starting at 3rd level, you gain the ability to channel dark power against your foes. " +
        "At 6th level, you can smite a foe with unholy force, dealing extra necrotic damage. " +
        "At 10th level, your resistance to necrotic and radiant damage grows stronger. " +
        "At 14th level, you become the veritable avatar of subjugation, striking fear into all who oppose you.",
        true)]
    [InlineData("Starting at 3rd level, you gain the ability to channel dark power against your foes.", false)] // only one level
    [InlineData("Beginning at 6th level, and again when you reach 10th level, you gain new options.", true)]
    [InlineData("A 3rd-level spell slot is required to cast this. A 5th-level slot works too.", true)]
    [InlineData("Ability Score Increase: increase one ability score by 2, or two scores by 1 each.", false)]
    [InlineData("", false)]
    public void HasSubclassFeatureProgression_requires_two_distinct_level_gated_grants(string text, bool expected) =>
        ExtractionSignatures.HasSubclassFeatureProgression(text).Should().Be(expected);


    private static EntityCandidate Candidate(string name, string text) =>
        new(DndMcpAICsharpFun.Domain.Entities.EntityType.Background, name, text, null);

    [Fact]
    public void IsRealEntity_true_for_subclass_feature_progression()
    {
        var c = Candidate("Oathbreaker",
            "Starting at 3rd level, you gain dark power. At 6th level, you smite with unholy force. " +
            "At 10th level, your resistance grows. At 14th level, you become an avatar of subjugation.");
        ExtractionSignatures.IsRealEntity(c).Should().BeTrue();
    }

    [Fact] // structural-only: entity-like name + substantial prose is NOT enough without a 5etools
    // match or a structural signature (stat block / magic item / subclass progression) — declined.
    public void IsRealEntity_false_for_entity_like_name_with_prose_only()
    {
        var c = Candidate("Charlatan",
            "You have always been able to talk your way out of trouble and into places you shouldn't. " +
            "You know a con man's tricks, and you're always ready to make a quick buck through a card game, " +
            "a rigged wager, or a fake potion recipe that doesn't work as advertised.");
        ExtractionSignatures.IsRealEntity(c).Should().BeFalse();
    }

    [Fact]
    public void IsRealEntity_false_for_thin_chapter_noise()
    {
        var c = Candidate("Ability Score Increase", "Increase one ability score of your choice by 2, or two scores by 1 each.");
        ExtractionSignatures.IsRealEntity(c).Should().BeFalse();
    }

    [Fact]
    public void IsRealEntity_false_for_empty_baseclass_shell()
    {
        var c = Candidate("Barbarian", "");
        ExtractionSignatures.IsRealEntity(c).Should().BeFalse();
    }
}