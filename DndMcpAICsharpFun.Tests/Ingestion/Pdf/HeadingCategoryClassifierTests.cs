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
}
