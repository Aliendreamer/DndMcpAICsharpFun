using DndMcpAICsharpFun.Domain;
using FluentAssertions;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HeadingCategoryClassifierTests
{
    [Theory]
    [InlineData("Spells", ContentCategory.Spell)]
    [InlineData("Barbarian", ContentCategory.Class)]
    [InlineData("Class Features", ContentCategory.Class)]
    [InlineData("Races", ContentCategory.Race)]
    [InlineData("Monsters", ContentCategory.Monster)]
    [InlineData("Magic Items", ContentCategory.Item)]
    [InlineData("Conditions", ContentCategory.Condition)]
    [InlineData("Rage", ContentCategory.Rule)]
    [InlineData("Hill Dwarf", ContentCategory.Rule)]
    [InlineData("", ContentCategory.Rule)]
    public void Guess_ReturnsExpectedCategory(string title, ContentCategory expected)
    {
        HeadingCategoryClassifier.Guess(title).Should().Be(expected);
    }

    [Fact]
    public void GuessRanked_PutsPrimaryGuessFirst()
    {
        HeadingCategoryClassifier.GuessRanked("Spells").Should().StartWith(ContentCategory.Spell);
    }

    [Fact]
    public void GuessRanked_DragonHeading_IncludesRaceSoModelCanRetype()
    {
        // "Dragonborn" matches the "dragon" creature-type keyword -> primary Monster,
        // but the prior MUST also offer Race so the model can re-type it away from Monster
        // (the exact corpus-wide failure this prior exists to fix).
        var prior = HeadingCategoryClassifier.GuessRanked("Dragonborn Traits");
        prior.Should().StartWith(ContentCategory.Monster);
        prior.Should().Contain(ContentCategory.Race);
    }

    [Fact]
    public void GuessRanked_AlwaysIncludesFrequencyFloor()
    {
        // Primary "Rule" (no keyword hit) still offers the common types so the model can pick them.
        var prior = HeadingCategoryClassifier.GuessRanked("Rage");
        prior.Should().Contain(new[]
        {
            ContentCategory.Monster, ContentCategory.Spell, ContentCategory.Item, ContentCategory.Class,
        });
    }

    [Fact]
    public void GuessRanked_IsDistinctAndSmall()
    {
        var prior = HeadingCategoryClassifier.GuessRanked("Monsters");
        prior.Should().OnlyHaveUniqueItems();
        prior.Count.Should().BeLessThanOrEqualTo(8);
    }
}
