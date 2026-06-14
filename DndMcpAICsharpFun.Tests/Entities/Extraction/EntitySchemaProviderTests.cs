using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class EntitySchemaProviderTests
{
    // ── InjectConfidenceField ─────────────────────────────────────────────────

    [Fact]
    public void InjectConfidenceField_adds_confidence_to_properties()
    {
        // Arrange: a schema with one existing property.
        using var original = JsonDocument.Parse(
            """{"type":"object","properties":{"name":{"type":"string"}}}""");

        // Act
        var result = EntitySchemaProvider.InjectConfidenceField(original.RootElement.Clone());

        // Assert: the properties object now has both "name" and "confidence".
        result.TryGetProperty("properties", out var props).Should().BeTrue();
        props.TryGetProperty("name", out _).Should().BeTrue("original property must be preserved");
        props.TryGetProperty("confidence", out var conf).Should().BeTrue("confidence field must be injected");

        conf.TryGetProperty("type", out var type).Should().BeTrue();
        type.GetString().Should().Be("string");

        conf.TryGetProperty("enum", out var enumProp).Should().BeTrue();
        var values = enumProp.EnumerateArray().Select(e => e.GetString()).ToList();
        values.Should().BeEquivalentTo(["low", "medium", "high"]);
    }

    [Fact]
    public void InjectConfidenceField_preserves_non_properties_top_level_keys()
    {
        using var original = JsonDocument.Parse(
            """{"type":"object","title":"Foo","properties":{"x":{"type":"integer"}}}""");

        var result = EntitySchemaProvider.InjectConfidenceField(original.RootElement.Clone());

        result.TryGetProperty("type", out var typeProp).Should().BeTrue();
        typeProp.GetString().Should().Be("object");

        result.TryGetProperty("title", out var titleProp).Should().BeTrue();
        titleProp.GetString().Should().Be("Foo");
    }

    [Fact]
    public void InjectConfidenceField_schema_with_no_properties_key_passes_through_unchanged()
    {
        // A schema without a "properties" object should not blow up.
        using var original = JsonDocument.Parse("""{"type":"string"}""");

        var act = () => EntitySchemaProvider.InjectConfidenceField(original.RootElement.Clone());
        act.Should().NotThrow();
    }

    // ── LoadSchemas ───────────────────────────────────────────────────────────

    [Fact]
    public void LoadSchemas_returns_empty_when_directory_is_empty()
    {
        var schemasDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(schemasDir);
        try
        {
            var opts     = Options.Create(new EntityExtractionOptions { SchemasDirectory = schemasDir });
            var provider = new EntitySchemaProvider(opts, NullLogger<EntitySchemaProvider>.Instance);

            var result = provider.LoadSchemas();

            result.Should().BeEmpty("no schema files were written");
        }
        finally
        {
            Directory.Delete(schemasDir, true);
        }
    }

    [Fact]
    public void LoadSchemas_loads_and_injects_confidence_for_matching_file()
    {
        var schemasDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(schemasDir);
        try
        {
            // Write a minimal Monster schema.
            File.WriteAllText(
                Path.Combine(schemasDir, "MonsterFields.schema.json"),
                """{"type":"object","properties":{"cr":{"type":"string"}}}""");

            var opts     = Options.Create(new EntityExtractionOptions { SchemasDirectory = schemasDir });
            var provider = new EntitySchemaProvider(opts, NullLogger<EntitySchemaProvider>.Instance);

            var result = provider.LoadSchemas();

            result.Should().ContainKey(DndMcpAICsharpFun.Domain.Entities.EntityType.Monster);
            var schema = result[DndMcpAICsharpFun.Domain.Entities.EntityType.Monster];
            schema.TryGetProperty("properties", out var props).Should().BeTrue();
            props.TryGetProperty("confidence", out _).Should().BeTrue("confidence must be injected");
            props.TryGetProperty("cr", out _).Should().BeTrue("original property must be preserved");
        }
        finally
        {
            Directory.Delete(schemasDir, true);
        }
    }
}
