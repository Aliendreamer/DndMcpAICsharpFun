using System.Text.Json;

using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

/// <summary>
/// Whole-branch review regression (I-1): the backfill providers used to build <c>Fields</c> in
/// the curated domain <c>*Fields</c> shape, but the actual <c>ISimpleEntityRenderer</c>s (and
/// real extraction output) read the RAW 5etools shape off <c>envelope.Fields</c> — so a
/// backfilled entity rendered NAME-ONLY at ingest (e.g. "Alert.") and embedded no rules text.
/// These tests drive each provider's <c>BuildEntity</c> output through the SAME
/// <see cref="EntityCanonicalTextDispatcher"/> the real ingestion pipeline uses (see
/// <c>EntityIngestionOrchestrator</c>: <c>textDispatcher.Render(merged)</c> is called
/// unconditionally for every entity) and assert the rendered <c>canonicalText</c> contains a
/// verbatim substring of the source element's <c>entries</c> prose — proving the entity is NOT
/// name-only. Before the fix (curated <c>*Fields</c>), these fail (RED): the renderer finds none
/// of its expected raw keys and canonicalText collapses to just the name.
/// </summary>
public sealed class BackfillRenderCompatTests
{
    private readonly EntityCanonicalTextDispatcher _dispatcher = new();

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Feat_BackfillEntity_RendersPrerequisiteAndEntriesProse_NotNameOnly()
    {
        var provider = new FeatBackfillProvider();
        var element = Parse("""
            { "name": "Alert", "source": "PHB", "page": 165,
              "prerequisite": [ { "level": 4 } ],
              "entries": [ "Always on the lookout for danger, you gain the following benefits." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Alert", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Alert.");
        text.Should().Contain("Always on the lookout for danger");
        text.Should().Contain("4");
    }

    [Fact]
    public void Weapon_BackfillEntity_RendersDamageAndEntriesProse_NotNameOnly()
    {
        var provider = new WeaponBackfillProvider();
        var element = Parse("""
            { "name": "Dagger", "source": "PHB", "page": 149,
              "weaponCategory": "simple", "dmg1": "1d4", "dmgType": "P",
              "entries": [ "A simple blade, easily concealed and thrown." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Dagger", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Dagger.");
        text.Should().Contain("1d4");
        text.Should().Contain("simple blade, easily concealed");
    }

    [Fact]
    public void Condition_BackfillEntity_RendersEntriesProse_NotNameOnly()
    {
        var provider = new ConditionBackfillProvider();
        var element = Parse("""
            { "name": "Blinded", "source": "PHB", "page": 290,
              "entries": [
                { "type": "list", "items": [
                    "A blinded creature can't see and automatically fails any ability check that requires sight.",
                    "Attack rolls against the creature have advantage, and the creature's attack rolls have disadvantage."
                  ]
                }
              ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Blinded", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Blinded");
        text.Should().NotBe("Blinded.");
        text.Should().Contain("automatically fails any ability check");
    }

    [Fact]
    public void Background_BackfillEntity_RendersSkillsAndEntriesProse_NotNameOnly()
    {
        var provider = new BackgroundBackfillProvider();
        var element = Parse("""
            { "name": "Acolyte", "source": "PHB", "page": 127,
              "skillProficiencies": [ { "insight": true, "religion": true } ],
              "entries": [ "You have spent your life in the service of a temple." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Acolyte", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Acolyte.");
        text.Should().Contain("insight");
        text.Should().Contain("service of a temple");
    }

    [Fact]
    public void Trap_BackfillEntity_RendersTrapHazTypeAndEntriesProse_NotNameOnly()
    {
        var provider = new TrapBackfillProvider();
        var element = Parse("""
            { "name": "Pit Trap", "source": "DMG", "page": 122,
              "trapHazType": "SMPL",
              "entries": [ "A simple pit dug in the ground, camouflaged with dirt and debris." ] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "Pit Trap", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Pit Trap.");
        text.Should().Contain("SMPL");
        text.Should().Contain("camouflaged with dirt and debris");
    }

    [Fact]
    public void DiseasePoison_BackfillEntity_RendersEntriesProse_NotNameOnly()
    {
        var provider = new DiseasePoisonBackfillProvider();
        var element = Parse("""
            { "name": "Cackle Fever", "source": "DMG", "page": 257,
              "entries": [ "Any humanoid that drinks water tainted by this disease must succeed on a saving throw or become infected." ] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "Cackle Fever", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Cackle Fever.");
        text.Should().Contain("tainted by this disease");
    }

    [Fact]
    public void VehicleMount_BackfillEntity_RendersVehicleTypeAndEntriesProse_NotNameOnly()
    {
        var provider = new VehicleBackfillProvider();
        var element = Parse("""
            { "name": "Keelboat", "source": "PHB", "page": 119,
              "vehicleType": "SHIP",
              "entries": [ "A keelboat has a solid keel that allows it to sail on the roughest waters." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Keelboat", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Keelboat.");
        text.Should().Contain("SHIP");
        text.Should().Contain("solid keel");
    }

    [Fact]
    public void Item_BackfillEntity_RendersTypeValueAndEntriesProse_NotNameOnly()
    {
        var provider = new ItemBackfillProvider();
        var element = Parse("""
            { "name": "Crowbar", "source": "PHB", "page": 153,
              "type": "AT", "value": 200,
              "entries": [ "Using a crowbar grants advantage to Strength checks where the crowbar's leverage can be applied." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Crowbar", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Crowbar.");
        text.Should().Contain("AT");
        text.Should().Contain("advantage to Strength checks");
    }

    [Fact]
    public void Armor_BackfillEntity_RendersTypeAcAndEntriesProse_NotNameOnly()
    {
        var provider = new ArmorBackfillProvider();
        var element = Parse("""
            { "name": "Chain Mail", "source": "PHB", "page": 145,
              "type": "HA", "ac": 16,
              "entries": [ "Made of interlocking metal rings, chain mail includes a layer of quilted fabric." ] }
            """);

        var entity = provider.BuildEntity("PHB", "Edition2014", "Chain Mail", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Chain Mail.");
        text.Should().Contain("16");
        text.Should().Contain("interlocking metal rings");
    }

    [Fact]
    public void God_BackfillEntity_RendersAlignmentDomainsAndEntriesProse_NotNameOnly()
    {
        var provider = new GodBackfillProvider();
        var element = Parse("""
            { "name": "Asmodeus", "source": "DMG", "alignment": ["L", "E"],
              "domains": ["Trickery", "Order"], "symbol": "Three triangles",
              "pantheon": "Dawn War",
              "entries": [ "Asmodeus rules the Nine Hells with cold calculation." ] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "Asmodeus", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("Asmodeus.");
        text.Should().Contain("Trickery");
        text.Should().Contain("rules the Nine Hells");
    }

    [Fact]
    public void MagicItem_BackfillEntity_RendersRarityAttunementAndEntriesProse_NotNameOnly()
    {
        var provider = new MagicItemBackfillProvider();
        var element = Parse("""
            { "name": "+1 Rod of the Pact Keeper", "source": "DMG", "rarity": "uncommon",
              "type": "RD|DMG", "reqAttune": "by a warlock",
              "entries": [ "While holding this rod, you gain a +1 bonus to spell attack rolls." ] }
            """);

        var entity = provider.BuildEntity("DMG", "Edition2014", "+1 Rod of the Pact Keeper", element);
        var text = _dispatcher.Render(entity);

        text.Should().NotBe("+1 Rod of the Pact Keeper.");
        text.Should().Contain("uncommon");
        text.Should().Contain("attunement");
        text.Should().Contain("+1 bonus to spell attack rolls");
    }
}
