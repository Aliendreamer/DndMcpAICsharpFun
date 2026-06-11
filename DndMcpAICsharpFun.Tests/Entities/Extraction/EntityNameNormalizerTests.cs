using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityNameNormalizerTests
{
    [Theory]
    [InlineData("CIRCLE OF SPORES", "Circle of Spores")]
    [InlineData("OF MICE AND MEN", "Of Mice and Men")]
    [InlineData("TASHA'S CAULDRON", "Tasha's Cauldron")]
    [InlineData("DECK OF MANY THINGS", "Deck of Many Things")]
    [InlineData("LOW-LEVEL FOLLOWERS", "Low-Level Followers")]
    [InlineData("Circle of Spores", "Circle of Spores")]
    public void TitleCase_applies_dnd_title_case(string input, string expected)
        => EntityNameNormalizer.TitleCase(input).Should().Be(expected);

    [Theory]
    [InlineData("750 GP ART OBJECTS", "750 GP Art Objects")]
    [InlineData("QUICK NPCs", "Quick NPCs")]
    [InlineData("MONSTERS AS NPCs", "Monsters as NPCs")]
    [InlineData("CHALLENGE 1 (200 XP)", "Challenge 1 (200 XP)")]
    public void TitleCase_preserves_dnd_acronyms(string input, string expected)
        => EntityNameNormalizer.TitleCase(input).Should().Be(expected);

    [Fact]
    public void TitleCase_is_idempotent()
    {
        var once = EntityNameNormalizer.TitleCase("750 GP ART OBJECTS");
        EntityNameNormalizer.TitleCase(once).Should().Be(once);
    }

    [Theory]
    [InlineData("BESTIAL SOUL", true)]
    [InlineData("Circle of Spores", false)]
    [InlineData("Path of the Beast f eature", false)]
    public void TryNormalizeHeading_only_touches_clean_all_caps(string input, bool expectedChanged)
    {
        var changed = EntityNameNormalizer.TryNormalizeHeading(input, out var result);
        changed.Should().Be(expectedChanged);
        if (!changed) result.Should().Be(input);
        else result.Should().NotBe(input);
    }
}
