using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Tests for the per-book <see cref="EntityFieldFillService"/>: merges allowlisted (<see cref="FieldFillAllowlist"/>)
/// structured 5etools fields onto canonical entities via <see cref="FivetoolsFieldMerger"/>, scoped to one
/// book's <see cref="IngestionRecord.FivetoolsSourceKey"/>. The temp-dir + <see cref="IngestionRecord"/> setup
/// mirrors <see cref="EntityBackfillServiceTests"/>. Because <see cref="FivetoolsSourceRegistry.AllEntries"/> is
/// built once from the real "5etools/class" directory (proven by <c>FivetoolsSourceRegistryTests</c>), the fake
/// 5etools fixture below is placed at "class/class-barbarian.json" under a private temp fivetoolsDir — matching
/// the real registry entry's remapped tail exactly — so only that one synthetic file is ever read.
/// </summary>
public sealed class EntityFieldFillServiceTests : IDisposable
{
    private const string SourceKey = "PHB";

    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;
    private readonly string _canonicalPath;
    private readonly CanonicalJsonLoader _loader;
    private readonly EntityFieldFillService _service;

    public EntityFieldFillServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "entity-field-fill-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        _canonicalPath = Path.Combine(_canonicalDir, "phb14.json");
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "class"));
        Directory.CreateDirectory(_canonicalDir);

        // Fake 5etools roster, at the exact relative path ("class/class-barbarian.json") the real registry
        // entry for the real "class-barbarian.json" file remaps onto when rooted at a custom fivetoolsDir.
        File.WriteAllText(Path.Combine(_fivetoolsDir, "class", "class-barbarian.json"), """
        {
          "class": [
            {
              "name": "Barbarian",
              "source": "PHB",
              "page": 46,
              "hd": { "number": 1, "faces": 12 },
              "classFeatures": [ "Rage|Barbarian||1", "Unarmored Defense|Barbarian||1" ],
              "subclassTitle": "Primal Path"
            }
          ]
        }
        """);

        // canonical phb14.json — Barbarian (prose/extraction, "entries" untouched by the merge), Wizard
        // ("manual" data source, never touched by field-fill regardless of allowlist).
        File.WriteAllText(_canonicalPath, """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "PHB", "edition": "Edition2014", "fileHash": "", "displayName": "Player's Handbook 2014" },
          "entities": [
            { "id": "phb14.class.barbarian", "type": "Class", "name": "Barbarian", "sourceBook": "PHB",
              "edition": "Edition2014", "page": 46,
              "firstAppearedIn": { "book": "PHB", "edition": "Edition2014", "page": 46 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "entries": [] }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] },
            { "id": "phb14.class.wizard", "type": "Class", "name": "Wizard", "sourceBook": "PHB",
              "edition": "Edition2014", "page": 112,
              "firstAppearedIn": { "book": "PHB", "edition": "Edition2014", "page": 112 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "entries": [ "hand-authored prose" ] }, "dataSource": "manual", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
        """);

        _loader = new CanonicalJsonLoader();
        _service = new EntityFieldFillService(_loader, new CanonicalJsonWriter(), _canonicalDir, _fivetoolsDir);
    }

    private static IngestionRecord Record(string? sourceKey) => new()
    {
        Id = 1,
        FilePath = "/tmp/x.pdf",
        FileName = "x.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = "Player's Handbook 2014",
        Status = IngestionStatus.JsonIngested,
        FivetoolsSourceKey = sourceKey,
    };

    [Fact]
    public async Task Fill_MergesAllowlistedFields_OnNonManualEntity_LeavingEntriesUntouched()
    {
        var result = await _service.FillAsync(Record(SourceKey), CancellationToken.None);

        Assert.True(result.HasSourceKey);
        Assert.Equal(_canonicalPath, result.CanonicalPath);
        Assert.True(result.FilledByType.TryGetValue(EntityType.Class, out var filled) && filled >= 1);
        Assert.True(result.EntitiesTouched >= 1);

        var file = await _loader.LoadAsync(_canonicalPath, CancellationToken.None);
        var barbarian = file.Entities.Single(e => e.Name == "Barbarian");

        Assert.True(barbarian.Fields.TryGetProperty("hd", out var hd));
        Assert.Equal(12, hd.GetProperty("faces").GetInt32());
        Assert.True(barbarian.Fields.TryGetProperty("classFeatures", out _));
        Assert.True(barbarian.Fields.TryGetProperty("subclassTitle", out var title));
        Assert.Equal("Primal Path", title.GetString());

        Assert.True(barbarian.Fields.TryGetProperty("_fivetoolsFilledFields", out var prov));
        var provenance = prov.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("hd", provenance);
        Assert.Contains("classFeatures", provenance);
        Assert.Contains("subclassTitle", provenance);

        // entries is extraction-owned; the merge never touches it.
        Assert.True(barbarian.Fields.TryGetProperty("entries", out var entries));
        Assert.Equal(JsonValueKind.Array, entries.ValueKind);
        Assert.Empty(entries.EnumerateArray());
    }

    [Fact]
    public async Task Fill_LeavesManualEntityUnchanged()
    {
        await _service.FillAsync(Record(SourceKey), CancellationToken.None);

        var file = await _loader.LoadAsync(_canonicalPath, CancellationToken.None);
        var wizard = file.Entities.Single(e => e.Name == "Wizard");

        Assert.False(wizard.Fields.TryGetProperty("hd", out _));
        Assert.False(wizard.Fields.TryGetProperty("_fivetoolsFilledFields", out _));
        Assert.True(wizard.Fields.TryGetProperty("entries", out var entries));
        Assert.Equal("hand-authored prose", entries.EnumerateArray().Single().GetString());
    }

    [Fact]
    public async Task Fill_NoSourceKey_ReturnsNoOpAndDoesNotWrite()
    {
        var before = File.ReadAllText(_canonicalPath);

        var result = await _service.FillAsync(Record(null), CancellationToken.None);

        Assert.False(result.HasSourceKey);
        Assert.Null(result.CanonicalPath);
        Assert.Empty(result.FilledByType);
        Assert.Equal(0, result.EntitiesTouched);
        Assert.Equal(before, File.ReadAllText(_canonicalPath));
    }

    [Fact]
    public async Task Fill_NoCanonicalFile_ReturnsNoOpWithoutThrowing()
    {
        var otherKey = "XDMG"; // slugs to dmg24.json, which was never seeded in this fixture
        var result = await _service.FillAsync(Record(otherKey), CancellationToken.None);

        Assert.True(result.HasSourceKey);
        Assert.NotNull(result.CanonicalPath);
        Assert.False(File.Exists(result.CanonicalPath));
        Assert.Equal(0, result.EntitiesTouched);
    }

    [Fact]
    public async Task Fill_WrittenCanonical_RoundTrips_WithUniqueIds()
    {
        await _service.FillAsync(Record(SourceKey), CancellationToken.None);

        // CanonicalJsonLoader itself throws on a duplicate id, so a successful load already proves
        // uniqueness; the explicit distinct-count assertion below documents the intent.
        var file = await _loader.LoadAsync(_canonicalPath, CancellationToken.None);

        Assert.Equal(2, file.Entities.Count);
        Assert.Equal(file.Entities.Count, file.Entities.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public async Task Fill_IsIdempotent_SecondRunChangesNothing()
    {
        var first = await _service.FillAsync(Record(SourceKey), CancellationToken.None);
        Assert.True(first.EntitiesTouched >= 1);

        var afterFirst = File.ReadAllText(_canonicalPath);
        var second = await _service.FillAsync(Record(SourceKey), CancellationToken.None);

        Assert.Equal(0, second.EntitiesTouched);
        Assert.Equal(afterFirst, File.ReadAllText(_canonicalPath));
    }

    /// <summary>
    /// Real-data spot check (don't trust the fixture): the actual corpus file backing the Class/Subclass
    /// registry entries really does carry the allowlisted "hd"/"classFeatures" fields, so the field-fill has
    /// something to merge against real books, not just the hand-built fixture above.
    /// </summary>
    [Fact]
    public void RealFivetoolsClassFile_Carries_AllowlistedFields()
    {
        var path = TestPaths.RepoFile("5etools/class/class-fighter.json");

        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        var fighter = doc.RootElement.GetProperty("class")[0];

        Assert.True(fighter.TryGetProperty("hd", out _));
        Assert.True(fighter.TryGetProperty("classFeatures", out _));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
