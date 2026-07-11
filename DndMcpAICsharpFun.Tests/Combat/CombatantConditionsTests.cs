using DndMcpAICsharpFun.Features.Combat;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatantConditionsTests
{
    [Fact]
    public void Conditions_round_trip_through_json_as_names()
    {
        var combatant = new Combatant { Name = "Goblin 1", MaxHp = 7, CurrentHp = 7 };
        combatant.Conditions = new[] { new ConditionTimer(Condition.Poisoned), new ConditionTimer(Condition.Prone) };

        combatant.ConditionsJson.Should().Contain("Poisoned").And.Contain("Prone");
        combatant.Conditions.Select(t => t.Condition).Should().Equal(Condition.Poisoned, Condition.Prone);
    }

    [Fact]
    public void New_shape_round_trips_timed_and_indefinite()
    {
        var json = CombatantConditions.Serialize(new[]
        {
            new ConditionTimer(Condition.Poisoned, 2),
            new ConditionTimer(Condition.Prone, null),
        });
        var back = CombatantConditions.Deserialize(json);
        back.Should().HaveCount(2);
        back.Single(t => t.Condition == Condition.Poisoned).RoundsRemaining.Should().Be(2);
        back.Single(t => t.Condition == Condition.Prone).RoundsRemaining.Should().BeNull();
    }

    [Fact]
    public void Legacy_string_array_reads_as_indefinite()
    {
        var back = CombatantConditions.Deserialize("[\"Poisoned\",\"Prone\"]");
        back.Select(t => t.Condition).Should().BeEquivalentTo(new[] { Condition.Poisoned, Condition.Prone });
        back.Should().OnlyContain(t => t.RoundsRemaining == null);
    }

    [Fact]
    public void Empty_reads_as_no_conditions()
    {
        CombatantConditions.Deserialize("[]").Should().BeEmpty();
        CombatantConditions.Deserialize("").Should().BeEmpty();
    }

    [Fact]
    public void Default_combatant_has_no_conditions()
    {
        new Combatant().Conditions.Should().BeEmpty();
    }

    [Fact]
    public void Enum_has_the_fifteen_standard_conditions()
    {
        Enum.GetValues<Condition>().Should().HaveCount(15);
    }
}
