using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Covers <see cref="MonsterBackfillProvider"/> and <see cref="SpellBackfillProvider"/>: the curated
/// field-projection logic lifted verbatim from <c>MonsterBackfillService</c>/<c>SpellBackfillService</c>
/// into the <c>IFivetoolsBackfillProvider</c> seam. Fixtures mirror <c>MonsterBackfillServiceTests</c>
/// and <c>SpellBackfillServiceTests</c>.
/// </summary>
public sealed class BackfillProviderTests
{
    private readonly CanonicalJsonLoader _loader = new();

    [Fact]
    public void MonsterBackfillProvider_Type_IsMonster()
    {
        var provider = new MonsterBackfillProvider();

        Assert.Equal(EntityType.Monster, provider.Type);
    }

    [Fact]
    public void MonsterBackfillProvider_BuildEntity_ProjectsCuratedFieldsForBullywug()
    {
        var provider = new MonsterBackfillProvider();
        using var doc = JsonDocument.Parse("""
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
              "traitTags": ["Amphibious"], "srd": true, "environment": ["swamp"] }
            """);

        var entity = provider.BuildEntity("MM", "Edition2014", "Bullywug", doc.RootElement);

        Assert.Equal("mm14.monster.bullywug", entity.Id);
        Assert.Equal(EntityType.Monster, entity.Type);
        Assert.Equal("Bullywug", entity.Name);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);
        Assert.True(entity.Srd);
        Assert.Contains("Amphibious", entity.Keywords);

        var fields = _loader.DeserialiseFields<MonsterFields>(entity);
        Assert.Equal(12, fields.Str);
        Assert.NotNull(fields.Hp);
        Assert.Equal(11, fields.Hp!.Average);
        Assert.Contains("darkvision 60 ft.", fields.Senses!);
    }

    [Fact]
    public void MagicItemBackfillProvider_Type_IsMagicItem()
    {
        var provider = new MagicItemBackfillProvider();

        Assert.Equal(EntityType.MagicItem, provider.Type);
    }

    [Fact]
    public void MagicItemBackfillProvider_BuildEntity_ProjectsCuratedFieldsForRodOfThePactKeeper()
    {
        var provider = new MagicItemBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "+1 Rod of the Pact Keeper", "source": "DMG", "rarity": "uncommon",
              "type": "RD|DMG", "reqAttune": "by a warlock",
              "entries": ["While holding this rod..."] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "+1 Rod of the Pact Keeper", doc.RootElement);

        Assert.Equal(EntityType.MagicItem, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);
        Assert.False(entity.NeedsReview);

        var fields = _loader.DeserialiseFields<MagicItemFields>(entity);
        Assert.Equal("uncommon", fields.Rarity);
        Assert.Equal("RD", fields.ItemCategory);
        Assert.Equal("by a warlock", fields.Attunement);
        Assert.StartsWith("While holding", fields.Description);
    }

    [Fact]
    public void MagicItemBackfillProvider_EnumerateRoster_ExcludesMundaneItems()
    {
        var root = Path.Combine(Path.GetTempPath(), "magicitem-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "items.json"), """
                { "item": [
                  { "name": "+1 Rod of the Pact Keeper", "source": "DMG", "rarity": "rare" },
                  { "name": "Dagger", "source": "PHB", "rarity": "none" }
                ] }
                """);

            var provider = new MagicItemBackfillProvider();
            var roster = provider.EnumerateRoster(root).ToList();

            var only = Assert.Single(roster);
            Assert.Equal("+1 Rod of the Pact Keeper", only.GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void GodBackfillProvider_Type_IsGod()
    {
        var provider = new GodBackfillProvider();

        Assert.Equal(EntityType.God, provider.Type);
    }

    [Fact]
    public void GodBackfillProvider_BuildEntity_ProjectsCuratedFieldsForAsmodeus()
    {
        var provider = new GodBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Asmodeus", "source": "DMG", "alignment": ["L", "E"],
              "domains": ["Trickery", "Order"], "symbol": "Three triangles",
              "pantheon": "Dawn War" }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "Asmodeus", doc.RootElement);

        Assert.Equal(EntityType.God, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<GodFields>(entity);
        Assert.Equal("L, E", fields.Alignment);
        Assert.Contains("Trickery", fields.Domains);
        Assert.NotNull(fields.Symbol);
        Assert.Null(fields.Plane);
        Assert.Equal("", fields.Description);
    }

    [Fact]
    public void SpellBackfillProvider_Type_IsSpell()
    {
        var provider = new SpellBackfillProvider();

        Assert.Equal(EntityType.Spell, provider.Type);
    }

    [Fact]
    public void SpellBackfillProvider_BuildEntity_ProjectsDescriptionBlockAndDamageInflict()
    {
        var provider = new SpellBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Regenerate", "source": "PHB", "page": 1, "entries": ["Heals."],
              "entriesHigherLevel": [ { "type": "entries", "name": "At Higher Levels", "entries": ["+1"] } ],
              "damageInflict": ["fire"] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Regenerate", doc.RootElement);

        Assert.Equal("phb14.spell.regenerate", entity.Id);
        Assert.Equal(EntityType.Spell, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<SpellFields>(entity);
        var description = Assert.Single(fields.Entries!);
        Assert.Equal("entries", description.GetProperty("type").GetString());
        Assert.Equal("Description", description.GetProperty("name").GetString());
        Assert.Equal("Heals.", description.GetProperty("entries")[0].GetString());
        Assert.Contains("fire", fields.DamageInflict!);
    }
}
