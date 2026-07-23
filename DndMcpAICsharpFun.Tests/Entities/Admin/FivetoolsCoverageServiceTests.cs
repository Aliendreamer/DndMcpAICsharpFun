using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Engine tests for <see cref="FivetoolsCoverageService"/>, run against two fake providers (Monster,
/// Spell — mirroring <see cref="EntityBackfillServiceTests"/>'s <c>FakeProvider</c> pattern) rather
/// than the real 5etools corpus, so the per-type aggregation and the small <c>Unmodeled</c> catalog
/// check can be asserted precisely without depending on live 5etools data shape/size.
/// </summary>
public sealed class FivetoolsCoverageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;
    private readonly FivetoolsCoverageService _service;

    public FivetoolsCoverageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "fivetools-coverage-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(_fivetoolsDir);
        Directory.CreateDirectory(_canonicalDir);

        // books.json — one official source (MM, 2014), same shape as EntityBackfillServiceTests.
        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [ { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" } ] }
        """);

        // canonical mm14.json — one Monster (Goblin) and one Spell (Fireball), both grounded/present.
        File.WriteAllText(Path.Combine(_canonicalDir, "mm14.json"), """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "MM", "edition": "Edition2014", "fileHash": "", "displayName": "Monster Manual 2014" },
          "entities": [
            { "id": "mm14.monster.goblin", "type": "Monster", "name": "Goblin", "sourceBook": "MM",
              "edition": "Edition2014", "page": 166,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 166 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 8 }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] },
            { "id": "mm14.spell.fireball", "type": "Spell", "name": "Fireball", "sourceBook": "MM",
              "edition": "Edition2014", "page": 241,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 241 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "entries": [] }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
        """);

        // optionalfeatures.json — has an MM-sourced entry, so the book's Unmodeled bucket should
        // include "optionalfeatures" (a content type with no EntityType at all).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "optionalfeatures.json"), """
        { "optionalfeature": [ { "name": "Fighting Style Sample", "source": "MM" } ] }
        """);

        // senses.json — has entries, but none sourced to MM, so "senses" must NOT show up for this book.
        File.WriteAllText(Path.Combine(_fivetoolsDir, "senses.json"), """
        { "sense": [ { "name": "Blindsight", "source": "PHB" } ] }
        """);

        var registry = new BookSourceRegistry(Path.Combine(_fivetoolsDir, "books.json"));
        var loader = new CanonicalJsonLoader();
        IReadOnlyDictionary<EntityType, EntityBackfillService> services = new Dictionary<EntityType, EntityBackfillService>
        {
            [EntityType.Monster] = new EntityBackfillService(new FakeMonsterProvider(), registry, loader, _canonicalDir, _fivetoolsDir),
            [EntityType.Spell] = new EntityBackfillService(new FakeSpellProvider(), registry, loader, _canonicalDir, _fivetoolsDir),
        };
        _service = new FivetoolsCoverageService(services, _fivetoolsDir);
    }

    private static IngestionRecord Record(string? sourceKey) => new()
    {
        Id = 1,
        FilePath = "/tmp/x.pdf",
        FileName = "x.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = "Monster Manual 2014",
        Status = IngestionStatus.JsonIngested,
        FivetoolsSourceKey = sourceKey,
    };

    /// <summary>Fake Monster roster: Goblin present in canonical, Bugbear a gap.</summary>
    private sealed class FakeMonsterProvider : IFivetoolsBackfillProvider
    {
        public EntityType Type => EntityType.Monster;

        public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        {
            using var doc = JsonDocument.Parse("""
            [ { "name": "Goblin", "source": "MM" }, { "name": "Bugbear", "source": "MM" } ]
            """);
            foreach (var element in doc.RootElement.EnumerateArray())
                yield return element.Clone();
        }

        public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
            => FakeEntity(sourceKey, edition, Type, name);
    }

    /// <summary>Fake Spell roster: Fireball present in canonical, Magic Missile a gap.</summary>
    private sealed class FakeSpellProvider : IFivetoolsBackfillProvider
    {
        public EntityType Type => EntityType.Spell;

        public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        {
            using var doc = JsonDocument.Parse("""
            [ { "name": "Fireball", "source": "MM" }, { "name": "Magic Missile", "source": "MM" } ]
            """);
            foreach (var element in doc.RootElement.EnumerateArray())
                yield return element.Clone();
        }

        public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
            => FakeEntity(sourceKey, edition, Type, name);
    }

    private static EntityEnvelope FakeEntity(string sourceKey, string edition, EntityType type, string name)
        => new(
            Id: EntityIdSlug.For(sourceKey, type, name),
            Type: type,
            Name: name,
            SourceBook: sourceKey,
            Edition: edition,
            Page: null,
            FirstAppearedIn: new FirstAppearance(sourceKey, edition, null),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: JsonDocument.Parse("{}").RootElement.Clone(),
            DataSource: "5etools-backfill",
            Srd: false,
            Srd52: false,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: Array.Empty<string>(),
            Disposition: EntityDisposition.Accepted);

    [Fact]
    public async Task Compute_AggregatesPerType_WithRosterPresentAndMissingNames()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.Equal("MM", result.SourceKey);

        var monster = result.PerType.Single(t => t.Type == EntityType.Monster);
        Assert.Equal(2, monster.RosterCount);
        Assert.Equal(1, monster.Present);
        Assert.Equal(1, monster.MissingCount);
        Assert.Contains("Bugbear", monster.MissingNames);

        var spell = result.PerType.Single(t => t.Type == EntityType.Spell);
        Assert.Equal(2, spell.RosterCount);
        Assert.Equal(1, spell.Present);
        Assert.Equal(1, spell.MissingCount);
        Assert.Contains("Magic Missile", spell.MissingNames);
    }

    [Fact]
    public async Task Compute_Totals_AggregateAcrossTypes_WithCorrectCoveragePct()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.Equal(2, result.TotalPresent); // Goblin + Fireball
        Assert.Equal(4, result.TotalRoster);  // 2 Monster + 2 Spell
        Assert.Equal(50.0, result.CoveragePct);
    }

    [Fact]
    public async Task Compute_Unmodeled_ContainsOptionalfeatures_WhenBookHasEntriesThere()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.Contains("optionalfeatures", result.Unmodeled);
    }

    [Fact]
    public async Task Compute_Unmodeled_ExcludesFile_WhenBookHasNoEntriesThere()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        // senses.json exists but has no MM-sourced entry — must not surface for this book.
        Assert.DoesNotContain("senses", result.Unmodeled);
    }

    [Fact]
    public async Task Compute_NoSourceKey_ReturnsEmpty()
    {
        var result = await _service.ComputeAsync(Record(null), CancellationToken.None);

        Assert.Same(BookCoverage.Empty, result);
        Assert.Empty(result.PerType);
        Assert.Empty(result.Unmodeled);
        Assert.Equal(0, result.TotalPresent);
        Assert.Equal(0, result.TotalRoster);
        Assert.Equal(0.0, result.CoveragePct);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}