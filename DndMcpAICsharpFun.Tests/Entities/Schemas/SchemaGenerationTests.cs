using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using NJsonSchema;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Schemas;

public class SchemaGenerationTests
{
    [Theory]
    [InlineData("test-book.class.fighter", "ClassFields")]
    [InlineData("test-book.monster.bullywug", "MonsterFields")]
        [InlineData("test-book.spell.fireball", "SpellFields")]
    [InlineData("test-book.lore.the-planes", "LoreFields")]
    [InlineData("test-book.rule.encounter-building", "RuleFields")]
    public async Task Generated_schema_validates_fixture_entity(string entityId, string schemaName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(fixturePath, CancellationToken.None);
        var entity = file.Entities.Single(e => e.Id == entityId);

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Schemas", "canonical", $"{schemaName}.schema.json");
        File.Exists(schemaPath).Should().BeTrue($"expected schema at {schemaPath}");

        var schema = await JsonSchema.FromFileAsync(schemaPath);
        var fieldsJson = entity.Fields.GetRawText();
        var errors = schema.Validate(fieldsJson);
        errors.Should().BeEmpty(string.Join(", ", errors.Select(e => e.ToString())));
    }
}
