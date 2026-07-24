using System.Text.Json;

using DndMcpAICsharpFun.Features.Retrieval.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RaceAbilityParserTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Fixed_bonus_keys()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"ability":[{"str":2,"con":1}]}"""))
            .Should().BeEquivalentTo(["str", "con"]);

    [Fact]
    public void Choosable_bonus_included()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"ability":[{"choose":{"from":["str","dex","con"],"count":1}}]}"""))
            .Should().Contain("str").And.Contain("dex").And.Contain("con");

    [Fact]
    public void Mixed_fixed_and_choose()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"ability":[{"cha":2,"choose":{"from":["str","wis"],"count":1}}]}"""))
            .Should().BeEquivalentTo(["cha", "str", "wis"]);

    [Fact]
    public void No_ability_data_is_empty()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"size":["M"]}""")).Should().BeEmpty();

    [Fact]
    public void Case_tolerant_ability_key_and_malformed_is_ignored_not_thrown()
    {
        RaceAbilityParser.BoostedAbilities(Fields("""{"Ability":[{"str":2}]}""")).Should().Contain("str"); // case-tolerant key
        RaceAbilityParser.BoostedAbilities(Fields("""{"ability":"nonsense"}""")).Should().BeEmpty();        // wrong kind → no throw
        RaceAbilityParser.BoostedAbilities(Fields("""[1,2,3]""")).Should().BeEmpty();                       // not an object → no throw
    }
}