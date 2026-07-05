using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class MulticlassRulesTests
{
    private static CharacterSheet Sheet(int str = 10, int dex = 10, int con = 10,
        int intel = 10, int wis = 10, int cha = 10) =>
        new() { Strength = str, Dexterity = dex, Constitution = con,
                Intelligence = intel, Wisdom = wis, Charisma = cha };

    [Fact]
    public void Rogue_requires_dex_13()
    {
        MulticlassRules.CanMulticlassInto("Rogue", Sheet(dex: 12)).Allowed.Should().BeFalse();
        MulticlassRules.CanMulticlassInto("Rogue", Sheet(dex: 13)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Fighter_accepts_str_or_dex_13()
    {
        MulticlassRules.CanMulticlassInto("Fighter", Sheet(str: 13, dex: 8)).Allowed.Should().BeTrue();
        MulticlassRules.CanMulticlassInto("Fighter", Sheet(str: 8, dex: 13)).Allowed.Should().BeTrue();
        MulticlassRules.CanMulticlassInto("Fighter", Sheet(str: 8, dex: 8)).Allowed.Should().BeFalse();
    }

    [Fact]
    public void Paladin_requires_str_and_cha_13()
    {
        MulticlassRules.CanMulticlassInto("Paladin", Sheet(str: 13, cha: 12)).Allowed.Should().BeFalse();
        MulticlassRules.CanMulticlassInto("Paladin", Sheet(str: 13, cha: 13)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void Failed_prerequisite_reports_the_reason()
    {
        var r = MulticlassRules.CanMulticlassInto("Rogue", Sheet(dex: 12));
        r.Reason.Should().Contain("Dexterity 13");
    }

    [Fact]
    public void Fighter_multiclass_proficiency_subset_excludes_heavy_armor_and_saves()
    {
        var profs = MulticlassRules.MulticlassProficiencies("Fighter");
        profs.Should().Contain("light armor");
        profs.Should().Contain("medium armor");
        profs.Should().Contain("shields");
        profs.Should().Contain("martial weapons");
        profs.Should().NotContain("heavy armor");
        profs.Should().NotContain(p => p.Contains("saving throw"));
    }
}
