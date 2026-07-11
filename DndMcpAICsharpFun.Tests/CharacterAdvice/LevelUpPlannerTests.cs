using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

public class LevelUpPlannerTests
{
    private static ClassFields FighterFields() => new(
        Hd: new HitDice(Number: 1, Faces: 10),
        Proficiency: ["con", "str"],
        StartingProficiencies: null,
        ClassFeatures: JsonSerializer.Deserialize<List<JsonElement>>("""
            [ "Fighting Style|Fighter|PHB|1", "Second Wind|Fighter|PHB|1",
              "Action Surge|Fighter|PHB|2",
              "Martial Archetype|Fighter|PHB|3",
              "Ability Score Improvement|Fighter|PHB|4",
              "Extra Attack|Fighter|PHB|5" ]
            """),
        Multiclassing: null,
        Entries: null,
        SubclassTitle: "Martial Archetype");

    private static CharacterSheet FighterAt(int level)
    {
        var s = new CharacterSheet { Constitution = 14 };            // +2 CON
        s.SetSingleClass("Fighter", "", level);
        return s;
    }

    [Fact]
    public void Advancing_fighter_4to5_gainsHpPbAndExtraAttack()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(4), "Fighter", false, FighterFields(), null);

        delta.NewClassLevel.Should().Be(5);
        delta.NewTotalLevel.Should().Be(5);
        delta.HpAverageGain.Should().Be(6 + 2);                       // d10 avg 6 (⌈11/2⌉) + CON 2
        delta.ProficiencyBonusBefore.Should().Be(2);
        delta.ProficiencyBonusAfter.Should().Be(3);                   // level 5 → +3
        delta.FeaturesGained.Select(f => f.Name).Should().Contain("Extra Attack");
    }

    [Fact]
    public void Level4_opensAbilityScoreOrFeatChoice()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(3), "Fighter", false, FighterFields(), null);
        delta.OpenChoices.Select(c => c.Kind).Should().Contain(OpenChoiceKind.AbilityScoreOrFeat);
    }

    [Fact]
    public void Level3_opensSubclassSelection()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(2), "Fighter", false, FighterFields(), null);
        delta.IsSubclassSelectionLevel.Should().BeTrue();
        delta.OpenChoices.Select(c => c.Kind).Should().Contain(OpenChoiceKind.Subclass);
    }

    [Fact]
    public void NonCaster_hasNoSlotChange()
    {
        var delta = new LevelUpPlanner().Plan(FighterAt(4), "Fighter", false, FighterFields(), null);
        delta.SpellSlotsBefore.Should().OnlyContain(x => x == 0);
        delta.SpellSlotsAfter.Should().OnlyContain(x => x == 0);
    }
}
