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
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEntityLikeName_rejects_headings_and_fragments(string? name, bool expected) =>
        ExtractionSignatures.IsEntityLikeName(name).Should().Be(expected);
}
