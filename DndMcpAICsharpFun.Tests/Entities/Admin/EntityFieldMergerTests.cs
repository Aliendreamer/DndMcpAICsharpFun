using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class EntityFieldMergerTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static readonly IReadOnlySet<string> ClassAllow =
        new HashSet<string> { "hd", "classFeatures", "subclassTitle" };

    [Fact]
    public void FillsMissingAllowlistedFields_recordsProvenance_leavesEntriesAlone()
    {
        var entity = J("""{ "entries": ["prose"] }""");
        var five = J("""{ "hd": {"faces":12}, "classFeatures": ["Rage"], "subclassTitle": "Path", "entries": ["5e prose"], "hasFluff": true }""");

        var (merged, changed) = EntityFieldMerger.Merge(entity, ClassAllow, five);

        changed.Should().BeTrue();
        merged.GetProperty("hd").GetProperty("faces").GetInt32().Should().Be(12);
        merged.GetProperty("classFeatures").GetArrayLength().Should().Be(1);
        merged.GetProperty("entries")[0].GetString().Should().Be("prose");   // extraction prose untouched
        merged.TryGetProperty("hasFluff", out _).Should().BeFalse();          // non-allowlisted 5etools field NOT pulled
        merged.GetProperty("_fivetoolsFilledFields").EnumerateArray()
            .Select(x => x.GetString()).Should().BeEquivalentTo(["hd", "classFeatures", "subclassTitle"]);
    }

    [Fact]
    public void NeverOverwritesAnExtractionProducedField()
    {
        var entity = J("""{ "hd": {"faces":10} }""");           // extraction already has hd, NOT provenance-marked
        var five = J("""{ "hd": {"faces":12} }""");
        var (merged, changed) = EntityFieldMerger.Merge(entity, ClassAllow, five);
        merged.GetProperty("hd").GetProperty("faces").GetInt32().Should().Be(10);   // extraction wins
    }

    [Fact]
    public void ReRun_isByteIdentical_idempotent()
    {
        var entity = J("""{ "entries": ["p"] }""");
        var five = J("""{ "hd": {"faces":8}, "classFeatures": ["X"], "subclassTitle": "T" }""");
        var (first, _) = EntityFieldMerger.Merge(entity, ClassAllow, five);
        var (second, changed2) = EntityFieldMerger.Merge(first, ClassAllow, five);
        second.GetRawText().Should().Be(first.GetRawText());
        changed2.Should().BeFalse();
    }
}
