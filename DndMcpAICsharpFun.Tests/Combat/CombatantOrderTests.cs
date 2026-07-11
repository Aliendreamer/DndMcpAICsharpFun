using DndMcpAICsharpFun.Features.Combat;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Combat;

public sealed class CombatantOrderTests
{
    private static Combatant C(string name, int? init, int mod = 0, bool player = false, int added = 0) =>
        new() { Name = name, InitiativeRoll = init, InitiativeModifier = mod, IsPlayer = player, AddedOrder = added };

    [Fact]
    public void Sorts_by_initiative_descending()
    {
        var ordered = CombatantOrder.Sort(new[] { C("low", 8), C("high", 20), C("mid", 14) });
        ordered.Select(c => c.Name).Should().Equal("high", "mid", "low");
    }

    [Fact]
    public void Breaks_ties_by_modifier_then_player_then_added_order()
    {
        var monsterHiMod = C("monsterHiMod", 15, mod: 4);
        var playerLoMod = C("playerLoMod", 15, mod: 2, player: true);
        var monsterLoModFirst = C("monsterLoModFirst", 15, mod: 2, added: 1);
        var monsterLoModSecond = C("monsterLoModSecond", 15, mod: 2, added: 2);

        var ordered = CombatantOrder.Sort(new[] { monsterLoModSecond, playerLoMod, monsterHiMod, monsterLoModFirst });

        ordered.Select(c => c.Name).Should().Equal(
            "monsterHiMod",        // highest modifier wins the tie
            "playerLoMod",         // same modifier: player before monster
            "monsterLoModFirst",   // same modifier, both monsters: lower AddedOrder first
            "monsterLoModSecond");
    }

    [Fact]
    public void Combatants_without_initiative_sort_last()
    {
        var ordered = CombatantOrder.Sort(new[] { C("noInit", null), C("hasInit", 3) });
        ordered.Select(c => c.Name).Should().Equal("hasInit", "noInit");
    }


    [Fact]
    public void AreTied_true_on_equal_keys_including_both_null_roll()
    {
        var a = new Combatant { InitiativeRoll = null, InitiativeModifier = 2, IsPlayer = false };
        var b = new Combatant { InitiativeRoll = null, InitiativeModifier = 2, IsPlayer = false };
        CombatantOrder.AreTied(a, b).Should().BeTrue();

        var c = new Combatant { InitiativeRoll = 15, InitiativeModifier = 0, IsPlayer = true };
        var d = new Combatant { InitiativeRoll = 15, InitiativeModifier = 0, IsPlayer = true };
        CombatantOrder.AreTied(c, d).Should().BeTrue();
    }

    [Fact]
    public void AreTied_false_when_any_ordering_key_differs()
    {
        var baseC = new Combatant { InitiativeRoll = 15, InitiativeModifier = 2, IsPlayer = false };
        CombatantOrder.AreTied(baseC, new Combatant { InitiativeRoll = 14, InitiativeModifier = 2, IsPlayer = false }).Should().BeFalse();
        CombatantOrder.AreTied(baseC, new Combatant { InitiativeRoll = 15, InitiativeModifier = 3, IsPlayer = false }).Should().BeFalse();
        CombatantOrder.AreTied(baseC, new Combatant { InitiativeRoll = 15, InitiativeModifier = 2, IsPlayer = true }).Should().BeFalse();
    }
}
