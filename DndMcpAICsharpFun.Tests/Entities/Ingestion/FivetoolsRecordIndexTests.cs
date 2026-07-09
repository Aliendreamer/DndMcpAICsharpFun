using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsRecordIndexTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Build a tiny on-disk 5etools-like directory tree so tests never need the
    /// full 5etools/ repo.  We write a single spells file with two entries.
    /// </summary>
    private static string CreateTinySpellsDir()
    {
        var rootDir = Path.Combine(Path.GetTempPath(), "5etools-test-" + Guid.NewGuid());
        var spellsDir = Path.Combine(rootDir, "spells");
        Directory.CreateDirectory(spellsDir);

        var json = """
            {
              "spell": [
                { "name": "Fireball", "source": "PHB", "page": 241, "level": 3,
                  "school": "V", "srd": true },
                { "name": "Magic Missile", "source": "PHB", "page": 257, "level": 1,
                  "school": "V" }
              ]
            }
            """;
        File.WriteAllText(Path.Combine(spellsDir, "spells-phb.json"), json);
        return rootDir;
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_KnownSpell_MapsToExpectedId()
    {
        // EntityIdSlug.For("PHB", Spell, "Fireball") → "phb14.spell.fireball"
        var tmp = CreateTinySpellsDir();
        try
        {
            var index = await FivetoolsRecordIndex.BuildAsync(tmp);

            index.TryGetValue("phb14.spell.fireball", out var envelope).Should().BeTrue();
            envelope!.Name.Should().Be("Fireball");
            envelope.Srd.Should().BeTrue();
            envelope.DataSource.Should().Be("5etools");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task BuildAsync_AbsentDirectory_ReturnsEmptyIndex()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "no-5etools");
        var index = await FivetoolsRecordIndex.BuildAsync(missing);
        index.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_FilteredToMatchingSourceKey_ReturnsThatBook()
    {
        var tmp = CreateTinySpellsDir();
        try
        {
            var indexPhb = await FivetoolsRecordIndex.BuildAsync(tmp, sourceKeyFilter: ["PHB"]);
            indexPhb.Should().ContainKey("phb14.spell.fireball");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task BuildAsync_FilteredToUnrelatedSourceKey_ReturnsEmpty()
    {
        var tmp = CreateTinySpellsDir();
        try
        {
            var indexTce = await FivetoolsRecordIndex.BuildAsync(tmp, sourceKeyFilter: ["TCE"]);
            indexTce.Should().BeEmpty();
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task BuildAsync_MultipleEntries_AllMapped()
    {
        var tmp = CreateTinySpellsDir();
        try
        {
            var index = await FivetoolsRecordIndex.BuildAsync(tmp);
            index.Should().ContainKey("phb14.spell.fireball");
            index.Should().ContainKey("phb14.spell.magic-missile");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task BuildAsync_NoFilter_IncludesAllSources()
    {
        var tmp = CreateTinySpellsDir();
        try
        {
            var index = await FivetoolsRecordIndex.BuildAsync(tmp, sourceKeyFilter: null);
            index.Count.Should().Be(2);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}