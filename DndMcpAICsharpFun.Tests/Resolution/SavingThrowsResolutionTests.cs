using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class SavingThrowsResolutionTests
{
    private static CharacterSheet Sheet(params (string Class, int Level)[] classes)
    {
        var s = new CharacterSheet { Strength = 10, Dexterity = 10, Constitution = 10, Intelligence = 10, Wisdom = 14, Charisma = 10 };
        s.Classes = classes.Select(c => new ClassLevel { Class = c.Class, Level = c.Level }).ToList();
        return s;
    }

    [Fact]
    public void Proficient_save_adds_pb_nonproficient_does_not()
    {
        var fact = CharacterResolutionService.ResolveSavingThrows(Sheet(("Wizard", 4))); // INT/WIS saves, PB +2, WIS 14 (+2)
        fact.Confidence.Should().Be("ok");
        fact.Components.First(c => c.Label == "Wisdom").Value.Should().Be("+4 (proficient)"); // +2 mod + 2 PB
        fact.Components.First(c => c.Label == "Strength").Value.Should().Be("+0"); // 10 -> +0, not proficient
    }

    [Fact]
    public void Multiclass_save_proficiency_comes_only_from_starting_class()
    {
        // Classes[0] = Fighter (STR/CON). Wizard's INT/WIS must NOT be proficient.
        var fact = CharacterResolutionService.ResolveSavingThrows(Sheet(("Fighter", 1), ("Wizard", 1)));
        fact.Components.First(c => c.Label == "Constitution").Value.Should().Contain("proficient");
        fact.Components.First(c => c.Label == "Wisdom").Value.Should().NotContain("proficient");
    }

    [Fact]
    public void Unknown_starting_class_is_needsReview()
    {
        var fact = CharacterResolutionService.ResolveSavingThrows(Sheet(("Homebrewer", 3)));
        fact.Confidence.Should().Be("needsReview");
    }
}