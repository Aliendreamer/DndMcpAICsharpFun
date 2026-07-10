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
}
