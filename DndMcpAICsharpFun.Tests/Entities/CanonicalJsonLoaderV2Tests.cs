using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalJsonLoaderV2Tests
{
    private static string Json(string version, string extra = "") => $$"""
        {
          "schemaVersion": "{{version}}",
          "book": { "sourceBook": "B", "edition": "Edition2014", "fileHash": "h", "displayName": "B" },
          "entities": [{
            "id": "b.spell.fireball", "type": "Spell", "name": "Fireball",
            "sourceBook": "B", "edition": "Edition2014", "page": 1,
            "firstAppearedIn": { "book": "B", "edition": "Edition2014", "page": 1 },
            "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {}
            {{extra}}
          }]
        }
        """;

    private static async Task<CanonicalJsonFile> LoadAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"v2-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        try { return await new CanonicalJsonLoader().LoadAsync(path, CancellationToken.None); }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("1")]
    [InlineData("2")]
    public async Task Loads_v1_and_v2(string version)
    {
        var file = await LoadAsync(Json(version));
        file.SchemaVersion.Should().Be(version);
        var e = file.Entities.Single();
        e.Checks.Should().BeEmpty();
        e.Provenance.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_v3()
    {
        var act = () => LoadAsync(Json("3"));
        await act.Should().ThrowAsync<CanonicalJsonSchemaException>();
    }

    [Fact]
    public async Task Roundtrips_checks_and_provenance()
    {
        var extra = """
            , "dataSource": "programmatic",
            "checks": [{ "name": "school", "passed": false, "reason": "mismatch" }],
            "provenance": { "pages": [{ "page": 3, "boxes": [{ "x0": 1, "y0": 2, "x1": 3, "y1": 4 }] }] }
            """;
        var file = await LoadAsync(Json("2", extra));
        var e = file.Entities.Single();
        e.DataSource.Should().Be(DataSources.Programmatic);
        e.Checks.Should().ContainSingle().Which.Should().Be(new EntityCheck("school", false, "mismatch"));
        e.Provenance!.Pages.Should().ContainSingle();
        e.Provenance.Pages[0].Page.Should().Be(3);
        e.Provenance.Pages[0].Boxes[0].Should().Be(new PageBox(1, 2, 3, 4));

        var opts = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var again = JsonSerializer.Deserialize<EntityEnvelope>(JsonSerializer.Serialize(e, opts), opts)!;
        again.Checks.Should().BeEquivalentTo(e.Checks);
        again.Provenance.Should().BeEquivalentTo(e.Provenance);
    }
}
