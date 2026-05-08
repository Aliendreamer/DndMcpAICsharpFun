using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalJsonLoaderTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");

    [Fact]
    public async Task Load_returns_three_entities()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        file.Entities.Should().HaveCount(19);
        file.Book.SourceBook.Should().Be("Test Book");
        file.SchemaVersion.Should().Be("1");
    }

    [Fact]
    public async Task Load_deserialises_class_fields()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        var fighter = file.Entities.Single(e => e.Id == "test-book.class.fighter");
        // Verify the entity loads successfully; field names are now 5etools-style (hd, classFeatures, etc.)
        // and do not map to the old ClassFields C# properties, so we verify via raw JSON instead.
        fighter.Fields.TryGetProperty("hd", out var hd).Should().BeTrue();
        hd.TryGetProperty("faces", out var faces).Should().BeTrue();
        faces.GetInt32().Should().Be(10);
        fighter.Fields.TryGetProperty("classFeatures", out var classFeatures).Should().BeTrue();
        classFeatures.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Load_deserialises_monster_fields_cr()
    {
        var loader = new CanonicalJsonLoader();
        var file = await loader.LoadAsync(FixturePath, CancellationToken.None);
        var bullywug = file.Entities.Single(e => e.Id == "test-book.monster.bullywug");
        var fields = loader.DeserialiseFields<MonsterFields>(bullywug);
        var crText = fields.Cr.HasValue && fields.Cr.Value.ValueKind == System.Text.Json.JsonValueKind.String
            ? fields.Cr.Value.GetString()
            : fields.Cr?.GetProperty("cr").GetString();
        crText.Should().Be("1/4");
        fields.Str.Should().Be(12);
    }

    [Fact]
    public async Task Load_rejects_mismatched_schema_version()
    {
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, """{"schemaVersion":"0.5","book":{"sourceBook":"x","edition":"e","fileHash":"h","displayName":"x"},"entities":[]}""");
        try
        {
            var loader = new CanonicalJsonLoader();
            var act = async () => await loader.LoadAsync(tmp, CancellationToken.None);
            await act.Should().ThrowAsync<CanonicalJsonSchemaException>()
                     .WithMessage("*schemaVersion*0.5*");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public async Task Load_rejects_duplicate_ids()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "canonical", "test-book.json");
        var content = await File.ReadAllTextAsync(path);
        var dup = content.Replace("test-book.spell.fireball", "test-book.class.fighter"); // duplicate id
        var tmp = Path.GetTempFileName();
        await File.WriteAllTextAsync(tmp, dup);
        try
        {
            var loader = new CanonicalJsonLoader();
            var act = async () => await loader.LoadAsync(tmp, CancellationToken.None);
            await act.Should().ThrowAsync<CanonicalJsonSchemaException>()
                     .WithMessage("*duplicate*");
        }
        finally { File.Delete(tmp); }
    }
}
