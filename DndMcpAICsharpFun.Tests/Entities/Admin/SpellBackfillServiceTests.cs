using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class SpellBackfillServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _fivetoolsDir;
    private readonly string _canonicalDir;
    private readonly EntityBackfillService _service;

    public SpellBackfillServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "spell-backfill-" + Guid.NewGuid().ToString("N"));
        _fivetoolsDir = Path.Combine(_root, "5etools");
        _canonicalDir = Path.Combine(_root, "canonical");
        Directory.CreateDirectory(Path.Combine(_fivetoolsDir, "spells"));
        Directory.CreateDirectory(_canonicalDir);

        // books.json — one official source (PHB, 2014).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "books.json"), """
        { "book": [ { "id": "PHB", "name": "Player's Handbook (2014)", "group": "core", "published": "2014-08-19" } ] }
        """);

        // spells-phb.json — Fireball (present in canonical), Ray of Sickness (a gap),
        // and Green-Flame Blade sourced XPHB (must be excluded when key is PHB).
        File.WriteAllText(Path.Combine(_fivetoolsDir, "spells", "spells-phb.json"), """
        { "spell": [
          { "name": "Fireball", "source": "PHB", "page": 241, "level": 3,
            "entries": ["boom"],
            "entriesHigherLevel": [ { "type": "entries", "name": "At Higher Levels", "entries": ["more boom"] } ],
            "damageInflict": ["fire"] },
          { "name": "Ray of Sickness", "source": "PHB", "page": 271, "level": 1,
            "entries": ["a ray of sickening greenish energy"],
            "entriesHigherLevel": [ { "type": "entries", "name": "At Higher Levels", "entries": ["+1d8"] } ],
            "damageInflict": ["poison"], "conditionInflict": ["poisoned"] },
          { "name": "Green-Flame Blade", "source": "XPHB", "page": 1,
            "entries": ["xphb only"] }
        ] }
        """);

        // canonical phb14.json — only Fireball present (parsed).
        File.WriteAllText(Path.Combine(_canonicalDir, "phb14.json"), """
        {
          "schemaVersion": "1",
          "book": { "sourceBook": "PHB", "edition": "Edition2014", "fileHash": "", "displayName": "Player's Handbook 2014" },
          "entities": [
            { "id": "phb14.spell.fireball", "type": "Spell", "name": "Fireball", "sourceBook": "PHB",
              "edition": "Edition2014", "page": 242,
              "firstAppearedIn": { "book": "PHB", "edition": "Edition2014", "page": 242 },
              "revisedIn": [], "settingTags": [], "canonicalText": "",
              "fields": { "entries": [] }, "dataSource": "", "srd": false, "srd52": false,
              "basicRules2024": false, "needsReview": false, "disposition": "Accepted", "keywords": [] }
          ]
        }
        """);

        var registry = new BookSourceRegistry(Path.Combine(_fivetoolsDir, "books.json"));
        _service = new EntityBackfillService(new SpellBackfillProvider(), registry, new CanonicalJsonLoader(), _canonicalDir, _fivetoolsDir);
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
    public async Task Compute_MapsGapSpell_ToWellFormedBackfillEntity()
    {
        var result = await _service.ComputeAsync(Record("PHB"), CancellationToken.None);

        Assert.True(result.HasSourceKey);
        var spell = Assert.Single(result.ToAppend);

        Assert.Equal("phb14.spell.ray-of-sickness", spell.Id);
        Assert.Equal(EntityType.Spell, spell.Type);
        Assert.Equal("Ray of Sickness", spell.Name);
        Assert.Equal("PHB", spell.SourceBook);
        Assert.Equal("Edition2014", spell.Edition);
        Assert.Equal(271, spell.Page);
        Assert.Equal("PHB", spell.FirstAppearedIn.Book);
        Assert.Equal(271, spell.FirstAppearedIn.Page);
        Assert.Equal("5etools-backfill", spell.DataSource);
        Assert.Equal(EntityDisposition.Accepted, spell.Disposition);
        Assert.False(spell.NeedsReview);

        // fields.entries = [ { type:"entries", name:"Description", entries:<5etools.entries> } ]
        var fields = spell.Fields;
        var entries = fields.GetProperty("entries");
        Assert.Equal(1, entries.GetArrayLength());
        var descr = entries[0];
        Assert.Equal("entries", descr.GetProperty("type").GetString());
        Assert.Equal("Description", descr.GetProperty("name").GetString());
        Assert.Equal("a ray of sickening greenish energy", descr.GetProperty("entries")[0].GetString());

        Assert.Equal(JsonValueKind.Array, fields.GetProperty("entriesHigherLevel").ValueKind);
        Assert.Equal("poison", fields.GetProperty("damageInflict")[0].GetString());
        Assert.Equal("poisoned", fields.GetProperty("conditionInflict")[0].GetString());
    }

    [Fact]
    public async Task Compute_SkipsAlreadyPresent_ByNormalizedName()
    {
        var result = await _service.ComputeAsync(Record("PHB"), CancellationToken.None);

        Assert.DoesNotContain(result.ToAppend, e => e.Name == "Fireball");
        Assert.Equal(1, result.AlreadyPresent); // Fireball
    }

    [Fact]
    public async Task Compute_ExcludesXphbSpells_WhenKeyIsPhb()
    {
        var result = await _service.ComputeAsync(Record("PHB"), CancellationToken.None);

        Assert.DoesNotContain(result.ToAppend, e => e.Name == "Green-Flame Blade");
    }

    [Fact]
    public async Task Compute_NoSourceKey_ReturnsEmptyNoOp()
    {
        var result = await _service.ComputeAsync(Record(null), CancellationToken.None);

        Assert.False(result.HasSourceKey);
        Assert.Empty(result.ToAppend);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}