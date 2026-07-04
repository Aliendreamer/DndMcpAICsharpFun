using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class MonsterBackfillServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;
    private readonly CanonicalJsonLoader _loader;
    private readonly EntityBackfillService _service;

    public MonsterBackfillServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "monster-backfill-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "bestiary"));
        Directory.CreateDirectory(_canonicalDir);

        // books.json — one official source (MM, 2014).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [ { "id": "MM", "name": "Monster Manual (2014)", "group": "core", "published": "2014-09-30" } ] }
        """);

        // bestiary-mm.json — Goblin (present in canonical), Bullywug (a gap, full stat block),
        // and Aboleth sourced MPMM (must be excluded when key is MM).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "bestiary", "bestiary-mm.json"), """
        { "monster": [
          { "name": "Goblin", "source": "MM", "page": 166,
            "size": ["S"], "type": "humanoid", "str": 8, "cr": "1/4" },
          { "name": "Bullywug", "source": "MM", "page": 35,
            "size": ["M"], "type": "humanoid", "alignment": ["N", "E"],
            "ac": [15], "hp": { "average": 11, "formula": "2d8 + 2" },
            "speed": { "walk": 20, "swim": 40 },
            "str": 12, "dex": 12, "con": 13, "int": 7, "wis": 10, "cha": 7,
            "skill": { "stealth": "+1" },
            "senses": ["darkvision 60 ft."], "passive": 10,
            "languages": ["Bullywug"], "cr": "1/4",
            "trait": [ { "name": "Amphibious", "entries": ["It can breathe air and water."] } ],
            "action": [ { "name": "Spear", "entries": ["Melee weapon attack."] } ],
            "traitTags": ["Amphibious"], "srd": true, "environment": ["swamp"] },
          { "name": "Aboleth", "source": "MPMM", "page": 6,
            "size": ["L"], "type": "aberration", "str": 21, "cr": "10" }
        ] }
        """);

        // canonical mm14.json — Goblin (grounded), Vault Guardian (grounded, not in roster → Extra,
        // matches no 5etools monster → extraUnknown), Ghost Recall (previously backfilled, not in roster
        // → Extra, matches no 5etools monster → extraUnknown), Aboleth (grounded, not in the MM roster but
        // present in 5etools under MPMM → extraOtherSource, a cross-print, never flagged).
        File.WriteAllText(Path.Combine(_canonicalDir, "mm14.json"), """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "MM", "edition": "Edition2014", "fileHash": "", "displayName": "Monster Manual 2014" },
          "entities": [
            { "id": "mm14.monster.goblin", "type": "Monster", "name": "Goblin", "sourceBook": "MM",
              "edition": "Edition2014", "page": 166,
              "firstAppearedIn": { "book": "MM", "edition": "Edition2014", "page": 166 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "str": 8, "cr": "1/4" }, "dataSource": "", "srd": false, "srd52": false,
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
        _service = new EntityBackfillService(new MonsterBackfillProvider(), registry, _loader, _canonicalDir, _fivetoolsDir);
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

    [Fact]
    public async Task Compute_MapsGapMonster_ToWellFormedBackfillEntity()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.True(result.HasSourceKey);
        Assert.Contains("Bullywug", result.Missing);
        var monster = Assert.Single(result.ToAppend);

        Assert.Equal("mm14.monster.bullywug", monster.Id);
        Assert.Equal(EntityType.Monster, monster.Type);
        Assert.Equal("Bullywug", monster.Name);
        Assert.Equal("MM", monster.SourceBook);
        Assert.Equal("Edition2014", monster.Edition);
        Assert.Equal(35, monster.Page);
        Assert.Equal("MM", monster.FirstAppearedIn.Book);
        Assert.Equal(35, monster.FirstAppearedIn.Page);
        Assert.Equal("5etools-backfill", monster.DataSource);
        Assert.Equal(EntityDisposition.Accepted, monster.Disposition);
        Assert.False(monster.NeedsReview);
        Assert.True(monster.Srd);
        Assert.Contains("Amphibious", monster.Keywords);
    }

    [Fact]
    public async Task Compute_BackfilledMonster_RoundTripsAsValidMonsterFields()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);
        var monster = Assert.Single(result.ToAppend);

        // The projected fields must deserialise 1:1 as a typed MonsterFields.
        var fields = _loader.DeserialiseFields<MonsterFields>(monster);

        Assert.Equal(12, fields.Str);
        Assert.Equal(7, fields.Int);
        Assert.NotNull(fields.Hp);
        Assert.Equal(11, fields.Hp!.Average);
        Assert.Contains("darkvision 60 ft.", fields.Senses!);
        Assert.Contains("Bullywug", fields.Languages!);
        Assert.Equal("1/4", fields.Cr!.Value.GetString());
        var trait = Assert.Single(fields.Trait!);
        Assert.Equal("Amphibious", trait.Name);
        Assert.Contains("swamp", fields.Environment!);
        Assert.Equal(JsonValueKind.Object, fields.Skill!.Value.ValueKind);
    }

    [Fact]
    public async Task Compute_SkipsAlreadyPresent_ByNormalizedName()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.DoesNotContain(result.ToAppend, e => e.Name == "Goblin");
        Assert.Equal(1, result.AlreadyPresent); // Goblin
    }

    [Fact]
    public async Task Compute_ExcludesMonstersFromOtherSources()
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
        Assert.Equal(1, result.BackfilledCount);  // Ghost Recall
    }

    [Fact]
    public async Task Compute_Extra_ReportsCanonicalMonstersAbsentFromRoster()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        Assert.Contains("Vault Guardian", result.Extra);
        Assert.Contains("Ghost Recall", result.Extra);
        Assert.Contains("Aboleth", result.Extra);
        Assert.DoesNotContain("Goblin", result.Extra); // Goblin is in the roster
    }

    [Fact]
    public async Task Compute_ExtraOtherSource_ReportsNamesMatchingAnotherSourceInFiveTools()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        // Aboleth is extra for MM (sourced MPMM in the bestiary) but matches a 5etools monster
        // elsewhere — a plausible cross-print, not a false positive.
        Assert.Contains("Aboleth", result.ExtraOtherSource);
        Assert.DoesNotContain("Vault Guardian", result.ExtraOtherSource);
        Assert.DoesNotContain("Ghost Recall", result.ExtraOtherSource);
    }

    [Fact]
    public async Task Compute_ExtraUnknown_ReportsNamesMatchingNoFiveToolsMonster()
    {
        var result = await _service.ComputeAsync(Record("MM"), CancellationToken.None);

        // Vault Guardian and Ghost Recall match no 5etools monster under any source.
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
    public async Task FlagUnknown_SetsNeedsReview_OnExtraUnknownMonsters()
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
    public async Task FlagUnknown_LeavesExtraOtherSourceMonstersUntouched()
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
