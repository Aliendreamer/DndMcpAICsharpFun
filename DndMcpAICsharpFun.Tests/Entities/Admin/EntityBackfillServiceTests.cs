using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Engine tests for the generic <see cref="EntityBackfillService"/>, run against a <see cref="FakeProvider"/>
/// (Type = Monster) rather than the real 5etools bestiary. Fixture mirrors
/// <see cref="MonsterBackfillServiceTests"/>: same canonical seed shape (Goblin grounded, Vault Guardian
/// grounded-unknown-extra, Ghost Recall backfilled-unknown-extra, Aboleth grounded-other-source), but the
/// roster comes from the fake provider instead of a bestiary-*.json file.
/// </summary>
public sealed class EntityBackfillServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;
    private readonly CanonicalJsonLoader _loader;
    private readonly EntityBackfillService _service;

    public EntityBackfillServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "entity-backfill-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(_fivetoolsDir);
        Directory.CreateDirectory(_canonicalDir);

        // books.json — one official source (MM, 2014).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [ { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" } ] }
        """);

        // canonical mm14.json — Goblin (grounded), Vault Guardian (grounded, not in roster → Extra,
        // matches no roster element → extraUnknown), Ghost Recall (previously backfilled, not in roster
        // → Extra, matches no roster element → extraUnknown), Aboleth (grounded, not in the MM roster but
        // present in the roster under MPMM → extraOtherSource, a cross-print, never flagged).
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
            { "id": "mm14.monster.vault-guardian", "type": "Monster", "name": "Vault Guardian", "sourceBook": "MM",
              "edition": "Edition2014", "page": 999,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 999 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 20 }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] },
            { "id": "mm14.monster.ghost-recall", "type": "Monster", "name": "Ghost Recall", "sourceBook": "MM",
              "edition": "Edition2014", "page": 998,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 998 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 6 }, "dataSource": "5etools-backfill", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] },
            { "id": "mm14.monster.aboleth", "type": "Monster", "name": "Aboleth", "sourceBook": "MM",
              "edition": "Edition2014", "page": 997,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 997 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 21 }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
        """);

        var registry = new BookSourceRegistry(Path.Combine(_fivetoolsDir, "books.json"));
        _loader = new CanonicalJsonLoader();
        _service = new EntityBackfillService(new FakeProvider(), registry, _loader, _canonicalDir, _fivetoolsDir);
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

    /// <summary>
    /// Fake roster: Goblin/Bullywug under MM, Aboleth under MPMM — every qualifying element across all
    /// sources (mirrors the shape of a real 5etools bestiary), filtering by source key is the engine's job.
    /// <see cref="BuildEntity"/> returns a minimal envelope; the engine tests don't inspect fields.
    /// </summary>
    private sealed class FakeProvider : IFivetoolsBackfillProvider
    {
        public EntityType Type => EntityType.Monster;

        public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
        {
            using var doc = JsonDocument.Parse("""
            [
              { "name": "Goblin", "source": "MM" },
              { "name": "Bullywug", "source": "MM" },
              { "name": "Aboleth", "source": "MPMM" }
            ]
            """);
            foreach (var element in doc.RootElement.EnumerateArray())
                yield return element.Clone();
        }

        public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
            => new(
                Id: EntityIdSlug.For(sourceKey, Type, name),
                Type: Type,
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
    }

    [Fact]
    public async Task Compute_MapsRosterGap_ToBackfillEntity()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.True(result.HasSourceKey);
        Assert.Contains("Bullywug", result.Missing);
        var entity = Assert.Single(result.ToAppend);
        Assert.Equal("mm14.monster.bullywug", entity.Id);
        Assert.Equal(EntityType.Monster, entity.Type);
        Assert.Equal("Bullywug", entity.Name);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);
    }

    [Fact]
    public async Task Compute_SkipsAlreadyPresent_ByNormalizedName()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.DoesNotContain(result.ToAppend, e => e.Name == "Goblin");
        Assert.Equal(1, result.AlreadyPresent); // Goblin
    }

    [Fact]
    public async Task Compute_ExcludesRosterElementsFromOtherSources()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.DoesNotContain(result.ToAppend, e => e.Name == "Aboleth");
        Assert.DoesNotContain("Aboleth", result.Missing);
    }

    [Fact]
    public async Task Compute_CountsGroundedVsBackfilled()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.Equal(3, result.GroundedCount);   // Goblin + Vault Guardian + Aboleth
        Assert.Equal(1, result.BackfilledCount); // Ghost Recall
    }

    [Fact]
    public async Task Compute_Extra_ReportsCanonicalEntitiesAbsentFromRoster()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.Contains("Vault Guardian", result.Extra);
        Assert.Contains("Ghost Recall", result.Extra);
        Assert.Contains("Aboleth", result.Extra);
        Assert.DoesNotContain("Goblin", result.Extra); // Goblin is in the roster
    }

    [Fact]
    public async Task Compute_ExtraOtherSource_ReportsNamesMatchingAnotherSourceInRoster()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        // Aboleth is extra for MM (sourced MPMM in the roster) but matches a roster element elsewhere —
        // a plausible cross-print, not a false positive.
        Assert.Contains("Aboleth", result.ExtraOtherSource);
        Assert.DoesNotContain("Vault Guardian", result.ExtraOtherSource);
        Assert.DoesNotContain("Ghost Recall", result.ExtraOtherSource);
    }

    [Fact]
    public async Task Compute_ExtraUnknown_ReportsNamesMatchingNoRosterElement()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        // Vault Guardian and Ghost Recall match no roster element under any source.
        Assert.Contains("Vault Guardian", result.ExtraUnknown);
        Assert.Contains("Ghost Recall", result.ExtraUnknown);
        Assert.DoesNotContain("Aboleth", result.ExtraUnknown);
    }

    [Fact]
    public async Task Compute_IsIdempotent_AfterApplyingBackfill()
    {
        var first = await _service.ComputeAsync(Record("MM"), CancellationToken.None);
        Assert.NotEmpty(first.ToAppend);

        // Apply the gap (append + write), exactly like the backfill endpoint.
        var file = await _loader.LoadAsync(first.CanonicalPath!, CancellationToken.None);
        var merged = file.Entities.Concat(first.ToAppend).ToList();
        var writer = new CanonicalJsonWriter();
        await writer.WriteAsync(first.CanonicalPath!, file with { Entities = merged }, CancellationToken.None);

        // A second run has no gaps: nothing to append.
        var second = await _service.ComputeAsync(Record("MM"), CancellationToken.None);
        Assert.Empty(second.ToAppend);
        Assert.Empty(second.Missing);
    }

    [Fact]
    public async Task Compute_NoSourceKey_ReturnsEmptyNoOp()
    {
        var result = await _service.ComputeAsync(Record(null), CancellationToken.None);

        Assert.False(result.HasSourceKey);
        Assert.Empty(result.ToAppend);
        Assert.Empty(result.Missing);
        Assert.Empty(result.Extra);
        Assert.Empty(result.ExtraOtherSource);
        Assert.Empty(result.ExtraUnknown);
    }

    [Fact]
    public async Task FlagUnknown_SetsNeedsReview_OnExtraUnknownEntities()
    {
        var writer = new CanonicalJsonWriter();

        var result = await _service.FlagUnknownAsync(Record("MM"), writer, CancellationToken.None);

        Assert.True(result.HasSourceKey);
        Assert.Contains("Vault Guardian", result.Flagged);
        Assert.Contains("Ghost Recall", result.Flagged);
        Assert.DoesNotContain("Aboleth", result.Flagged);

        var file = await _loader.LoadAsync(result.CanonicalPath!, CancellationToken.None);
        var vaultGuardian = file.Entities.Single(e => e.Name == "Vault Guardian");
        var ghostRecall = file.Entities.Single(e => e.Name == "Ghost Recall");
        Assert.True(vaultGuardian.NeedsReview);
        Assert.True(ghostRecall.NeedsReview);
    }

    [Fact]
    public async Task FlagUnknown_LeavesExtraOtherSourceEntitiesUntouched()
    {
        var writer = new CanonicalJsonWriter();

        await _service.FlagUnknownAsync(Record("MM"), writer, CancellationToken.None);

        var file = await _loader.LoadAsync(Path.Combine(_canonicalDir, "mm14.json"), CancellationToken.None);
        var aboleth = file.Entities.Single(e => e.Name == "Aboleth");
        Assert.False(aboleth.NeedsReview);
    }

    [Fact]
    public async Task FlagUnknown_NeverDeletesAnEntity()
    {
        var writer = new CanonicalJsonWriter();
        var before = await _loader.LoadAsync(Path.Combine(_canonicalDir, "mm14.json"), CancellationToken.None);

        await _service.FlagUnknownAsync(Record("MM"), writer, CancellationToken.None);

        var after = await _loader.LoadAsync(Path.Combine(_canonicalDir, "mm14.json"), CancellationToken.None);
        Assert.Equal(before.Entities.Count, after.Entities.Count);
        foreach (var e in before.Entities)
            Assert.Contains(after.Entities, a => a.Id == e.Id);
    }

    [Fact]
    public async Task FlagUnknown_IsIdempotent_AlreadyFlaggedIsNotReFlagged()
    {
        var writer = new CanonicalJsonWriter();

        var first = await _service.FlagUnknownAsync(Record("MM"), writer, CancellationToken.None);
        Assert.NotEmpty(first.Flagged);

        var second = await _service.FlagUnknownAsync(Record("MM"), writer, CancellationToken.None);
        Assert.Empty(second.Flagged);
    }

    [Fact]
    public async Task FlagUnknown_NoSourceKey_ReturnsEmptyNoOp()
    {
        var writer = new CanonicalJsonWriter();

        var result = await _service.FlagUnknownAsync(Record(null), writer, CancellationToken.None);

        Assert.False(result.HasSourceKey);
        Assert.Empty(result.Flagged);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
