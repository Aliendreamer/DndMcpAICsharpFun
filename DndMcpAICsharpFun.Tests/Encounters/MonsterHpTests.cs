using System.Text.Json;
using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class MonsterHpTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Reads_average_and_formula()
    {
        MonsterHp.TryRead(Fields("{\"hp\":{\"average\":7,\"formula\":\"2d6\"}}"), out var avg, out var formula)
            .Should().BeTrue();
        avg.Should().Be(7);
        formula.Should().Be("2d6");
    }

    [Fact]
    public void Reads_average_when_formula_absent()
    {
        MonsterHp.TryRead(Fields("{\"hp\":{\"average\":22}}"), out var avg, out var formula).Should().BeTrue();
        avg.Should().Be(22);
        formula.Should().BeNull();
    }

    [Fact]
    public void Missing_hp_returns_false()
    {
        MonsterHp.TryRead(Fields("{\"cr\":\"1/4\"}"), out var avg, out var formula).Should().BeFalse();
        avg.Should().Be(0);
        formula.Should().BeNull();
    }
}
