using System.Text.Json;

using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Covers <see cref="FivetoolsEntryText.Flatten"/>: recursive flattening of 5etools <c>entries</c>
/// arrays (plain strings, nested <c>entries</c>/<c>section</c> blocks, <c>list</c>, <c>table</c>, and
/// inline-tag stripping) into a single description string.
/// </summary>
public sealed class FivetoolsEntryTextTests
{
    [Fact]
    public void Flatten_HandlesStringsNestedEntriesListsTablesAndInlineTags()
    {
        using var doc = JsonDocument.Parse("""
            [
              "Plain string entry.",
              { "type": "entries", "name": "Sub", "entries": ["inner"] },
              { "type": "list", "items": ["a", "b"] },
              { "type": "table", "colLabels": ["d100", "Result"], "rows": [["01-50", "Water"], ["51-00", "Beer"]] },
              "Roll {@dice 1d4} then {@item Rope|PHB}."
            ]
            """);

        var result = FivetoolsEntryText.Flatten(doc.RootElement);

        Assert.Contains("Plain string entry.", result);
        Assert.Contains("Sub", result);
        Assert.Contains("inner", result);
        Assert.Contains("• a", result);
        Assert.Contains("• b", result);
        Assert.Contains("d100 | Result", result);
        Assert.Contains("01-50 | Water", result);
        Assert.Contains("Roll 1d4 then Rope.", result);
    }

    [Fact]
    public void Flatten_EmptyArray_ReturnsEmptyString()
    {
        using var doc = JsonDocument.Parse("[]");

        var result = FivetoolsEntryText.Flatten(doc.RootElement);

        Assert.Equal("", result);
    }

    [Fact]
    public void Flatten_NonArrayElement_ReturnsEmptyString()
    {
        using var doc = JsonDocument.Parse("{}");

        var result = FivetoolsEntryText.Flatten(doc.RootElement);

        Assert.Equal("", result);
    }
}