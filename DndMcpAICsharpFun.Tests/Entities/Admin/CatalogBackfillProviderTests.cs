using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Covers FeatBackfillProvider, BackgroundBackfillProvider, and ConditionBackfillProvider: the
/// self-contained prose-catalog providers (mirroring GodBackfillProvider/SpellBackfillProvider,
/// NOT the generic field-fill mappers). See <see cref="BackfillProviderTests"/> for the sibling
/// suite covering the 4 pre-existing providers.
/// </summary>
public sealed class CatalogBackfillProviderTests
{
    private readonly CanonicalJsonLoader _loader = new();

    [Fact]
    public void FeatBackfillProvider_Type_IsFeat()
    {
        var provider = new FeatBackfillProvider();

        Assert.Equal(EntityType.Feat, provider.Type);
    }

    [Fact]
    public void FeatBackfillProvider_BuildEntity_ProjectsCuratedFieldsForAlert()
    {
        var provider = new FeatBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Alert", "source": "PHB", "page": 165, "srd": true,
              "prerequisite": [ { "level": 4 } ],
              "skillProficiencies": [ { "choose": { "from": [ "perception" ] } } ],
              "entries": [ "Always on the lookout for danger, you gain the following benefits." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Alert", doc.RootElement);

        Assert.Equal("phb14.feat.alert", entity.Id);
        Assert.Equal(EntityType.Feat, entity.Type);
        Assert.Equal("Alert", entity.Name);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);
        Assert.True(entity.Srd);

        var fields = _loader.DeserialiseFields<FeatFields>(entity);
        Assert.Contains("Level 4", fields.Prerequisites);
        Assert.Contains("Skill Proficiency", fields.Grants);
        Assert.Equal("Always on the lookout for danger, you gain the following benefits.", fields.Description);
    }

    [Fact]
    public void FeatBackfillProvider_EnumerateRoster_YieldsSeededFeats()
    {
        var root = Path.Combine(Path.GetTempPath(), "feat-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "feats.json"), """
                { "feat": [
                  { "name": "Alert", "source": "PHB" },
                  { "name": "Tough", "source": "PHB" }
                ] }
                """);

            var provider = new FeatBackfillProvider();
            var names = provider.EnumerateRoster(root).Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Alert", "Tough" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void BackgroundBackfillProvider_Type_IsBackground()
    {
        var provider = new BackgroundBackfillProvider();

        Assert.Equal(EntityType.Background, provider.Type);
    }

    [Fact]
    public void BackgroundBackfillProvider_BuildEntity_ProjectsCuratedFieldsForAcolyte()
    {
        var provider = new BackgroundBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Acolyte", "source": "PHB", "page": 127, "srd": true,
              "skillProficiencies": [ { "insight": true, "religion": true } ],
              "languageProficiencies": [ { "anyStandard": 2 } ],
              "startingEquipment": [
                { "_": [
                    { "item": "holy symbol|phb", "displayName": "holy symbol (a gift)" },
                    { "special": "vestments" },
                    "common clothes|phb"
                  ]
                }
              ],
              "entries": [
                { "name": "Feature: Shelter of the Faithful", "type": "entries",
                  "entries": [ "You command the respect of those who share your faith." ] }
              ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Acolyte", doc.RootElement);

        Assert.Equal("phb14.background.acolyte", entity.Id);
        Assert.Equal(EntityType.Background, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<BackgroundFields>(entity);
        Assert.Contains("Insight", fields.SkillProficiencies);
        Assert.Contains("Religion", fields.SkillProficiencies);
        Assert.Contains("2 of your choice", fields.Languages);
        Assert.Contains("holy symbol (a gift)", fields.Equipment);
        Assert.Contains("vestments", fields.Equipment);
        Assert.Contains("common clothes", fields.Equipment);
        Assert.Equal("Shelter of the Faithful", fields.FeatureName);
        Assert.Equal("You command the respect of those who share your faith.", fields.FeatureSummary);
    }

    [Fact]
    public void BackgroundBackfillProvider_EnumerateRoster_YieldsSeededBackgrounds()
    {
        var root = Path.Combine(Path.GetTempPath(), "background-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "backgrounds.json"), """
                { "background": [
                  { "name": "Acolyte", "source": "PHB" },
                  { "name": "Charlatan", "source": "PHB" }
                ] }
                """);

            var provider = new BackgroundBackfillProvider();
            var names = provider.EnumerateRoster(root).Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Acolyte", "Charlatan" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConditionBackfillProvider_Type_IsCondition()
    {
        var provider = new ConditionBackfillProvider();

        Assert.Equal(EntityType.Condition, provider.Type);
    }

    [Fact]
    public void ConditionBackfillProvider_BuildEntity_ProjectsCuratedFieldsForBlinded()
    {
        var provider = new ConditionBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Blinded", "source": "PHB", "page": 290, "srd": true,
              "entries": [
                { "type": "list", "items": [
                    "A blinded creature can't see and automatically fails any ability check that requires sight.",
                    "Attack rolls against the creature have advantage, and the creature's attack rolls have disadvantage."
                  ]
                }
              ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Blinded", doc.RootElement);

        Assert.Equal("phb14.condition.blinded", entity.Id);
        Assert.Equal(EntityType.Condition, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<ConditionFields>(entity);
        Assert.Contains("blinded creature can't see", fields.Description);
        Assert.Contains("disadvantage", fields.Description);
    }

    [Fact]
    public void ConditionBackfillProvider_EnumerateRoster_YieldsOnlyConditionsNotDiseases()
    {
        var root = Path.Combine(Path.GetTempPath(), "condition-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "conditionsdiseases.json"), """
                { "condition": [
                    { "name": "Blinded", "source": "PHB" },
                    { "name": "Charmed", "source": "PHB" }
                  ],
                  "disease": [
                    { "name": "Cackle Fever", "source": "DMG" }
                  ]
                }
                """);

            var provider = new ConditionBackfillProvider();
            var names = provider.EnumerateRoster(root).Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Blinded", "Charmed" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}