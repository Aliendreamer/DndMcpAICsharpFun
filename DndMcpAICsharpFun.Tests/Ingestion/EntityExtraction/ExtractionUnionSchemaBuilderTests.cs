using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class ExtractionUnionSchemaBuilderTests
{
    private static IReadOnlyDictionary<EntityType, JsonElement> FakeSchemas()
    {
        static JsonElement Schema(string field) =>
            JsonDocument.Parse(
                $$"""
                { "type": "object", "additionalProperties": false,
                  "required": ["{{field}}"],
                  "properties": { "{{field}}": { "type": "string" } } }
                """).RootElement.Clone();

        return new Dictionary<EntityType, JsonElement>
        {
            [EntityType.Spell] = Schema("school"),
            [EntityType.Monster] = Schema("cr"),
            [EntityType.Race] = Schema("size"),
        };
    }

    private static List<string> BranchDiscriminators(JsonElement union) =>
        union.GetProperty("oneOf").EnumerateArray()
            .Select(b => b.GetProperty("properties").GetProperty("entityType").GetProperty("const").GetString()!)
            .ToList();

    [Fact]
    public void Build_AlwaysIncludesDeclineBranch_EvenWhenNoBranches()
    {
        var union = ExtractionUnionSchemaBuilder.Build(Array.Empty<EntityType>(), FakeSchemas());

        var discriminators = BranchDiscriminators(union);
        discriminators.Should().ContainSingle().Which.Should().Be(ExtractionUnionSchemaBuilder.DeclineType);
    }

    [Fact]
    public void Build_EmitsOneBranchPerType_PlusDecline()
    {
        var union = ExtractionUnionSchemaBuilder.Build(
            new[] { EntityType.Spell, EntityType.Monster }, FakeSchemas());

        BranchDiscriminators(union).Should().Equal("Spell", "Monster", ExtractionUnionSchemaBuilder.DeclineType);
    }

    [Fact]
    public void Build_PutsConstDiscriminatorAndKeepsTypeFields()
    {
        var union = ExtractionUnionSchemaBuilder.Build(new[] { EntityType.Monster }, FakeSchemas());

        var monster = union.GetProperty("oneOf").EnumerateArray().First();
        monster.GetProperty("properties").GetProperty("entityType").GetProperty("const").GetString()
            .Should().Be("Monster");
        // the type's own field survives into the branch
        monster.GetProperty("properties").TryGetProperty("cr", out _).Should().BeTrue();
        // entityType is required
        monster.GetProperty("required").EnumerateArray().Select(r => r.GetString())
            .Should().Contain("entityType");
    }

    [Fact]
    public void Build_SkipsUnknownTypesAndDeduplicates()
    {
        // Background is absent from FakeSchemas -> skipped; Spell repeated -> deduped.
        var union = ExtractionUnionSchemaBuilder.Build(
            new[] { EntityType.Spell, EntityType.Background, EntityType.Spell }, FakeSchemas());

        BranchDiscriminators(union).Should().Equal("Spell", ExtractionUnionSchemaBuilder.DeclineType);
    }

    [Fact]
    public void Build_DeclineBranchRequiresReason()
    {
        var union = ExtractionUnionSchemaBuilder.Build(Array.Empty<EntityType>(), FakeSchemas());

        var decline = union.GetProperty("oneOf").EnumerateArray().Single();
        decline.GetProperty("required").EnumerateArray().Select(r => r.GetString())
            .Should().Contain(new[] { "entityType", "reason" });
        decline.GetProperty("properties").TryGetProperty("reason", out _).Should().BeTrue();
    }
}
