using System.Text.Json;

using DndMcpAICsharpFun.Features.Retrieval.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RaceDamageDefenseParserTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Direct_strings()
        => RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":["necrotic","radiant"]}"""), "resist")
            .Should().BeEquivalentTo(["necrotic", "radiant"]);

    [Fact]
    public void Choosable_included()
        => RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":[{"choose":{"from":["acid","fire"]}}]}"""), "resist")
            .Should().Contain("acid").And.Contain("fire");

    [Fact]
    public void Mixed_string_and_choose()
        => RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":["cold",{"choose":{"from":["fire"]}}]}"""), "resist")
            .Should().BeEquivalentTo(["cold", "fire"]);

    [Fact]
    public void Reads_the_requested_key_only()
    {
        var f = Fields("""{"immune":["poison"]}""");
        RaceDamageDefenseParser.DamageTypes(f, "immune").Should().BeEquivalentTo(["poison"]);
        RaceDamageDefenseParser.DamageTypes(f, "resist").Should().BeEmpty(); // no resist key
    }

    [Fact]
    public void Case_tolerant_key_and_malformed_ignored_not_thrown()
    {
        RaceDamageDefenseParser.DamageTypes(Fields("""{"Resist":["fire"]}"""), "resist").Should().Contain("fire"); // key case-tolerant
        RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":"nonsense"}"""), "resist").Should().BeEmpty();     // wrong kind → no throw
        RaceDamageDefenseParser.DamageTypes(Fields("""[1,2,3]"""), "resist").Should().BeEmpty();                   // not an object → no throw
    }
}