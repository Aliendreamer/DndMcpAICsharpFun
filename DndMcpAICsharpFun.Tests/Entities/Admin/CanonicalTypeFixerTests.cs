using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class CanonicalTypeFixerTests
{
    private static CanonicalTypeFixerService CreateSvc(string dir) =>
        new(Options.Create(new EntityIngestionOptions { CanonicalDirectory = dir }));

    [Fact]
    public async Task FixTypesAsync_RewritesEntityTypeFromLookup()
    {
        // Arrange: canonical JSON with one Class entity that lookup says is Subclass
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var canonicalJson = """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "TCE", "edition": "Edition2014", "fileHash": "", "displayName": "Tasha's" },
          "entities": [{
            "id": "tce.class.circle-of-spores",
            "type": "Class",
            "name": "Circle of Spores",
            "sourceBook": "TCE",
            "edition": "Edition2014",
            "page": 36,
            "firstAppearedIn": { "book": "TCE", "edition": "Edition2014", "page": null },
            "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {},
            "dataSource": "llm", "srd": false, "srd52": false, "basicRules2024": false, "keywords": []
          }]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "tce.json"), canonicalJson);

        var lookup = new Dictionary<(string name, string source), EntityType>
        {
            [("circle of spores", "TCE")] = EntityType.Subclass
        };
        var svc = CreateSvc(dir);

        // Act
        var result = await svc.FixTypesAsync("tce", lookup);

        // Assert
        result.Fixed.Should().Be(1);
        result.Unmatched.Should().Be(0);
        var updated = await File.ReadAllTextAsync(Path.Combine(dir, "tce.json"));
        updated.Should().Contain("\"type\": \"Subclass\"");
        updated.Should().Contain("\"id\": \"tce.subclass.circle-of-spores\"");
        updated.Should().NotContain("tce.class.circle-of-spores");
    }

    [Fact]
    public async Task FixTypesAsync_LeavesUnmatchedEntityUnchanged()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var canonicalJson = """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "TCE", "edition": "Edition2014", "fileHash": "", "displayName": "Tasha's" },
          "entities": [{
            "id": "tce.class.custom-thing",
            "type": "Class", "name": "Custom Thing", "sourceBook": "TCE",
            "edition": "Edition2014", "page": null,
            "firstAppearedIn": { "book": "TCE", "edition": "Edition2014", "page": null },
            "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {},
            "dataSource": "llm", "srd": false, "srd52": false, "basicRules2024": false, "keywords": []
          }]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "tce.json"), canonicalJson);

        var svc = CreateSvc(dir);
        var result = await svc.FixTypesAsync("tce", new Dictionary<(string, string), EntityType>());

        result.Fixed.Should().Be(0);
        result.Unmatched.Should().Be(1);
        var content = await File.ReadAllTextAsync(Path.Combine(dir, "tce.json"));
        content.Should().Contain("\"id\": \"tce.class.custom-thing\"");
    }

    [Fact]
    public async Task FixTypesAsync_UpdatesCrossReferences()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var canonicalJson = """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "TCE", "edition": "Edition2014", "fileHash": "", "displayName": "Tasha's" },
          "entities": [
            {
              "id": "tce.class.circle-of-spores",
              "type": "Class", "name": "Circle of Spores", "sourceBook": "TCE",
              "edition": "Edition2014", "page": 36,
              "firstAppearedIn": { "book": "TCE", "edition": "Edition2014", "page": null },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": {}, "dataSource": "llm", "srd": false, "srd52": false, "basicRules2024": false, "keywords": []
            },
            {
              "id": "tce.class.some-feature",
              "type": "Class", "name": "Some Feature", "sourceBook": "TCE",
              "edition": "Edition2014", "page": 40,
              "firstAppearedIn": { "book": "TCE", "edition": "Edition2014", "page": null },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "relatedSubclass": "tce.class.circle-of-spores" },
              "dataSource": "llm", "srd": false, "srd52": false, "basicRules2024": false, "keywords": []
            }
          ]
        }
        """;
        await File.WriteAllTextAsync(Path.Combine(dir, "tce.json"), canonicalJson);

        var lookup = new Dictionary<(string name, string source), EntityType>
        {
            [("circle of spores", "TCE")] = EntityType.Subclass
        };
        var svc = CreateSvc(dir);

        var result = await svc.FixTypesAsync("tce", lookup);

        result.Fixed.Should().Be(1);
        result.CrossRefsUpdated.Should().BeGreaterThan(0);
        var updated = await File.ReadAllTextAsync(Path.Combine(dir, "tce.json"));
        updated.Should().Contain("tce.subclass.circle-of-spores");
        updated.Should().NotContain("tce.class.circle-of-spores");
    }

    // Bug-guard: shared WriteOptions must serialize EntityType as a string, not an integer.
    // This test verifies the CanonicalJson.WriteOptions enum serialization behaviour.
    [Fact]
    public void CanonicalJson_WriteOptions_SerializesEntityTypeAsString()
    {
        // CanonicalJson.WriteOptions must include JsonStringEnumConverter so that
        // EntityType is written as e.g. "Subclass" rather than the int 1.
        var opts = DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.CanonicalJson.WriteOptions;

        var envelope = new EntityEnvelope(
            Id:              "tce.subclass.circle-of-spores",
            Type:            EntityType.Subclass,
            Name:            "Circle of Spores",
            SourceBook:      "TCE",
            Edition:         "Edition2014",
            Page:            36,
            FirstAppearedIn: new FirstAppearance("TCE", "Edition2014", 36),
            RevisedIn:       [],
            SettingTags:     [],
            CanonicalText:   "",
            Fields:          System.Text.Json.JsonDocument.Parse("{}").RootElement,
            NeedsReview:     false);

        var json = System.Text.Json.JsonSerializer.Serialize(envelope, opts);

        // WriteIndented=true → "type": "Subclass" (with space after colon)
        json.Should().MatchRegex("\"type\"\\s*:\\s*\"Subclass\"");
        json.Should().NotMatchRegex("\"type\"\\s*:\\s*\\d");
    }
}
