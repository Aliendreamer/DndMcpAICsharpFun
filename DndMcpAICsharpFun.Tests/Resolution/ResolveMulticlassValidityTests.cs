using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ResolveMulticlassValidityTests
{
    [Fact]
    public void Blocked_multiclass_reports_the_failed_prerequisite()
    {
        var sheet = new CharacterSheet { Classes = [new() { Class = "Wizard", Level = 5 }], Dexterity = 12 };
        var fact = CharacterResolutionService.ResolveMulticlassValidity(sheet, "Rogue");

        fact.Value.Should().StartWith("not allowed");
        fact.Components.Should().Contain(c => c.Label == "prerequisite" && c.Value.Contains("Dexterity 13"));
    }

    [Fact]
    public void Allowed_multiclass_lists_the_reduced_proficiency_subset()
    {
        var sheet = new CharacterSheet { Classes = [new() { Class = "Wizard", Level = 5 }], Dexterity = 14 };
        var fact = CharacterResolutionService.ResolveMulticlassValidity(sheet, "Fighter");

        fact.Value.Should().Be("allowed");
        var profs = fact.Components.Single(c => c.Label == "proficiencies").Value;
        profs.Should().Contain("martial weapons");
        profs.Should().NotContain("heavy armor");
    }
}