using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class MonsterGroupingTests
{
    private static MonsterRef Goblin(string idSuffix = "") =>
        new($"mm.monster.goblin{idSuffix}", "Goblin", 0.25, 50);
    private static MonsterRef Hobgoblin() =>
        new("mm.monster.hobgoblin", "Hobgoblin", 0.5, 100);

    [Fact]
    public void Group_collapses_repeats_by_id_and_counts_them()
    {
        IReadOnlyList<MonsterRef> flat = [Hobgoblin(), Goblin(), Goblin(), Goblin()];

        var groups = MonsterGrouping.Group(flat);

        groups.Should().HaveCount(2);
        groups[0].Monster.Name.Should().Be("Hobgoblin");   // first-appearance order preserved
        groups[0].Count.Should().Be(1);
        groups[1].Monster.Name.Should().Be("Goblin");
        groups[1].Count.Should().Be(3);
    }

    [Fact]
    public void Group_keeps_distinct_ids_separate()
    {
        IReadOnlyList<MonsterRef> flat = [Goblin("-a"), Goblin("-b")];

        MonsterGrouping.Group(flat).Should().HaveCount(2);
    }

    [Fact]
    public void Group_of_empty_is_empty()
    {
        MonsterGrouping.Group([]).Should().BeEmpty();
    }

    [Fact]
    public void Describe_renders_counts_in_first_appearance_order()
    {
        IReadOnlyList<MonsterRef> flat = [Hobgoblin(), Goblin(), Goblin()];

        MonsterGrouping.Describe(flat).Should().Be("1× Hobgoblin, 2× Goblin");
    }
}
