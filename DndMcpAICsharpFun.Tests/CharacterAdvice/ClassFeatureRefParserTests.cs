// DndMcpAICsharpFun.Tests/CharacterAdvice/ClassFeatureRefParserTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.CharacterAdvice;

public class ClassFeatureRefParserTests
{
    private static IReadOnlyList<JsonElement> Json(string arrayJson) =>
        JsonSerializer.Deserialize<List<JsonElement>>(arrayJson)!;

    [Fact]
    public void ParsesStringAndObjectRefs_withLevel()
    {
        var refs = Json("""
            [ "Action Surge|Fighter|PHB|2",
              { "classFeature": "Extra Attack|Fighter|PHB|5" } ]
            """);
        var parsed = ClassFeatureRefParser.Parse(refs, "classFeature");
        parsed.Should().BeEquivalentTo(new[]
        {
            new FeatureRef("Action Surge", "PHB", 2),
            new FeatureRef("Extra Attack", "PHB", 5),
        });
    }

    [Fact]
    public void SkipsMalformedRefs()
    {
        var refs = Json("""[ "no-pipes-here", { "other": "x" }, 42 ]""");
        ClassFeatureRefParser.Parse(refs, "classFeature").Should().BeEmpty();
    }

    [Fact]
    public void ParsesSixPartSubclassFeatureRefs_LevelAndSourceFromLastTwoPositions()
    {
        var refs = Json("""
            [ "Combat Superiority|Fighter|PHB|Battle Master|PHB|3",
              { "subclassFeature": "Combat Superiority|Fighter|PHB|Battle Master|PHB|3" } ]
            """);
        var parsed = ClassFeatureRefParser.Parse(refs, "subclassFeature");
        parsed.Should().BeEquivalentTo(new[]
        {
            new FeatureRef("Combat Superiority", "PHB", 3),
            new FeatureRef("Combat Superiority", "PHB", 3),
        });
    }
}
