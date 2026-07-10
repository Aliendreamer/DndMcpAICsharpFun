using DndMcpAICsharpFun.Features.Combat;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatantConditionsTests
{
    [Fact]
    public void Conditions_round_trip_through_json_as_names()
    {
        var combatant = new Combatant { Name = "Goblin 1", MaxHp = 7, CurrentHp = 7 };
        combatant.Conditions = new[] { Condition.Poisoned, Condition.Prone };

        combatant.ConditionsJson.Should().Contain("Poisoned").And.Contain("Prone");
        combatant.Conditions.Should().Equal(Condition.Poisoned, Condition.Prone);
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
