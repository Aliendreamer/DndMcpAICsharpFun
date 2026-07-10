using System.Text.Json;
using DndMcpAICsharpFun.Features.Encounters;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Encounters;

public sealed class MonsterDexTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("{\"dex\":14}", 2)]   // (14-10)/2 = 2
    [InlineData("{\"dex\":8}", -1)]   // floor((8-10)/2) = -1
    [InlineData("{\"dex\":10}", 0)]
    [InlineData("{\"dex\":15}", 2)]   // floor((15-10)/2) = 2
    public void Reads_dex_and_derives_the_initiative_modifier(string json, int expected)
    {
        MonsterDex.TryReadModifier(Fields(json), out var mod).Should().BeTrue();
        mod.Should().Be(expected);
    }

    [Fact]
    public void Missing_dex_returns_false_and_zero()
    {
        MonsterDex.TryReadModifier(Fields("{\"cr\":\"1/4\"}"), out var mod).Should().BeFalse();
        mod.Should().Be(0);
    }
}
