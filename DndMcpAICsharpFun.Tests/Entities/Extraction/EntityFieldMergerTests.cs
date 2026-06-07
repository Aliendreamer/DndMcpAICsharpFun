using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class EntityFieldMergerTests
{
    private readonly EntityFieldMerger _merger = new();

    private static JsonElement Parse(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Merge_SingleElement_ReturnsItUnchanged()
    {
        var one = Parse("""{"name":"Fireball","level":3}""");
        var merged = _merger.Merge([one]);
        merged.GetRawText().Should().Be(one.GetRawText());
    }

    [Fact]
    public void Merge_Scalars_FirstNonNullWins()
    {
        var a = Parse("""{"name":"Pit Trap","threat":null}""");
        var b = Parse("""{"name":"SHOULD NOT WIN","threat":"setback"}""");
        var merged = _merger.Merge([a, b]);
        merged.GetProperty("name").GetString().Should().Be("Pit Trap");
        merged.GetProperty("threat").GetString().Should().Be("setback");
    }

    [Fact]
    public void Merge_Arrays_ConcatenatesInChunkOrder()
    {
        var a = Parse("""{"entries":["first"]}""");
        var b = Parse("""{"entries":["second","third"]}""");
        var merged = _merger.Merge([a, b]);
        merged.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetString())
            .Should().ContainInOrder("first", "second", "third");
    }

    [Fact]
    public void Merge_ArraysWithNamedItems_DropsLaterDuplicates()
    {
        var a = Parse("""{"variants":[{"name":"Spiked Pit","entries":["a"]}]}""");
        var b = Parse("""{"variants":[{"name":"Spiked Pit","entries":["b"]},{"name":"Hidden Pit","entries":["c"]}]}""");
        var merged = _merger.Merge([a, b]);
        var names = merged.GetProperty("variants").EnumerateArray()
            .Select(v => v.GetProperty("name").GetString()).ToList();
        names.Should().Equal("Spiked Pit", "Hidden Pit");
        merged.GetProperty("variants")[0].GetProperty("entries")[0].GetString().Should().Be("a");
    }

    [Fact]
    public void Merge_Objects_FirstNonNullWins()
    {
        var a = Parse("""{"hp":{"average":11,"formula":"2d8+2"}}""");
        var b = Parse("""{"hp":{"average":99,"formula":"9d8"}}""");
        var merged = _merger.Merge([a, b]);
        merged.GetProperty("hp").GetProperty("average").GetInt32().Should().Be(11);
    }

    [Fact]
    public void Merge_AllEmpty_ReturnsEmptyObject()
    {
        var merged = _merger.Merge([Parse("{}"), Parse("{}")]);
        merged.ValueKind.Should().Be(JsonValueKind.Object);
        merged.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public void Merge_FieldsOnlyInLaterChunks_AreIncluded()
    {
        var a = Parse("""{"name":"Gas Trap"}""");
        var b = Parse("""{"trapHazType":"MECH"}""");
        var merged = _merger.Merge([a, b]);
        merged.GetProperty("name").GetString().Should().Be("Gas Trap");
        merged.GetProperty("trapHazType").GetString().Should().Be("MECH");
    }
}
