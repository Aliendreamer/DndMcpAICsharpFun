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

    [Fact]
    public void TrapBackfillProvider_Type_IsTrap()
    {
        var provider = new TrapBackfillProvider();

        Assert.Equal(EntityType.Trap, provider.Type);
    }

    [Fact]
    public void TrapBackfillProvider_BuildEntity_ProjectsCuratedFieldsForPitTrap()
    {
        var provider = new TrapBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Pit Trap", "source": "DMG", "page": 122, "srd": true,
              "rating": [ { "tier": 1, "threat": "dangerous" } ],
              "detectDc": 15, "disarmDc": 10,
              "entries": [ "A simple pit dug in the ground, camouflaged with dirt and debris." ] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "Pit Trap", doc.RootElement);

        Assert.Equal("dmg14.trap.pit-trap", entity.Id);
        Assert.Equal(EntityType.Trap, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<TrapFields>(entity);
        Assert.Equal("Dangerous", fields.Difficulty);
        Assert.Equal(15, fields.DetectDc);
        Assert.Equal(10, fields.DisarmDc);
        Assert.Contains("simple pit", fields.Description);
    }

    [Fact]
    public void TrapBackfillProvider_EnumerateRoster_YieldsOnlyTrapsNotHazards()
    {
        var root = Path.Combine(Path.GetTempPath(), "trap-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "trapshazards.json"), """
                { "trap": [
                    { "name": "Pit Trap", "source": "DMG" },
                    { "name": "Poison Dart Trap", "source": "DMG" }
                  ],
                  "hazard": [
                    { "name": "Falling Rubble", "source": "DMG" }
                  ]
                }
                """);

            var provider = new TrapBackfillProvider();
            var names = provider.EnumerateRoster(root).Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Pit Trap", "Poison Dart Trap" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DiseasePoisonBackfillProvider_Type_IsDiseasePoison()
    {
        var provider = new DiseasePoisonBackfillProvider();

        Assert.Equal(EntityType.DiseasePoison, provider.Type);
    }

    [Fact]
    public void DiseasePoisonBackfillProvider_BuildEntity_ProjectsCuratedFieldsForCackleFever()
    {
        var provider = new DiseasePoisonBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Cackle Fever", "source": "DMG", "page": 257,
              "entries": [ "Any humanoid that drinks water tainted by this disease must succeed on a {@dc 12} Constitution saving throw or become infected." ] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "Cackle Fever", doc.RootElement);

        Assert.Equal("dmg14.diseasepoison.cackle-fever", entity.Id);
        Assert.Equal(EntityType.DiseasePoison, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<DiseasePoisonFields>(entity);
        Assert.Equal("Disease", fields.Kind);
        Assert.Equal("12", fields.SaveDc);
        Assert.Contains("tainted by this disease", fields.Description);
    }

    [Fact]
    public void DiseasePoisonBackfillProvider_EnumerateRoster_YieldsOnlyDiseasesNotConditions()
    {
        var root = Path.Combine(Path.GetTempPath(), "diseasepoison-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "conditionsdiseases.json"), """
                { "condition": [
                    { "name": "Blinded", "source": "PHB" }
                  ],
                  "disease": [
                    { "name": "Cackle Fever", "source": "DMG" },
                    { "name": "Sewer Plague", "source": "DMG" }
                  ]
                }
                """);

            var provider = new DiseasePoisonBackfillProvider();
            var names = provider.EnumerateRoster(root).Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Cackle Fever", "Sewer Plague" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void VehicleBackfillProvider_Type_IsVehicleMount()
    {
        var provider = new VehicleBackfillProvider();

        Assert.Equal(EntityType.VehicleMount, provider.Type);
    }

    [Fact]
    public void VehicleBackfillProvider_BuildEntity_ProjectsCuratedFieldsForKeelboat()
    {
        var provider = new VehicleBackfillProvider();
        using var doc = JsonDocument.Parse("""
            { "name": "Keelboat", "source": "PHB", "page": 119, "srd": true,
              "vehicleType": "SHIP",
              "speed": { "walk": 30 },
              "capCargo": 0.5,
              "entries": [ "A keelboat has a solid keel that allows it to sail on the roughest waters." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Keelboat", doc.RootElement);

        Assert.Equal("phb14.vehiclemount.keelboat", entity.Id);
        Assert.Equal(EntityType.VehicleMount, entity.Type);
        Assert.Equal("5etools-backfill", entity.DataSource);
        Assert.Equal(EntityDisposition.Accepted, entity.Disposition);

        var fields = _loader.DeserialiseFields<VehicleMountFields>(entity);
        Assert.Equal("SHIP", fields.Kind);
        Assert.Equal(30, fields.Speed);
        Assert.Equal(1000, fields.CapacityLb);
        Assert.Contains("solid keel", fields.Description);
    }

    [Fact]
    public void VehicleBackfillProvider_EnumerateRoster_YieldsOnlyVehiclesNotUpgrades()
    {
        var root = Path.Combine(Path.GetTempPath(), "vehicle-backfill-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "vehicles.json"), """
                { "vehicle": [
                    { "name": "Keelboat", "source": "PHB" },
                    { "name": "Longship", "source": "PHB" }
                  ],
                  "vehicleUpgrade": [
                    { "name": "Ballista", "source": "GoS" }
                  ]
                }
                """);

            var provider = new VehicleBackfillProvider();
            var names = provider.EnumerateRoster(root).Select(e => e.GetProperty("name").GetString()).ToList();

            Assert.Equal(new[] { "Keelboat", "Longship" }, names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}