using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsMappersTests
{
    private static JsonElement J(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public void ClassMapper_maps_fighter_to_envelope()
    {
        var json = J("{\"name\":\"Fighter\",\"source\":\"PHB\",\"page\":70,\"hd\":{\"number\":1,\"faces\":10},\"proficiency\":[\"str\",\"con\"],\"classFeatures\":[\"Fighting Style|Fighter||1\"]}");
        var envelope = new FivetoolsClassMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.Class);
        envelope.Name.Should().Be("Fighter");
        envelope.SourceBook.Should().Be("PHB");
        envelope.DataSource.Should().Be("5etools");
        envelope.Fields.TryGetProperty("hd", out _).Should().BeTrue();
    }

    [Fact]
    public void ClassMapper_returns_null_for_entry_missing_name()
    {
        var json = J("{\"source\":\"PHB\",\"hd\":{\"number\":1,\"faces\":10}}");
        var envelope = new FivetoolsClassMapper().Map(json);
        envelope.Should().BeNull();
    }

    [Fact]
    public void SubclassMapper_maps_battle_master()
    {
        var json = J("{\"name\":\"Battle Master\",\"source\":\"PHB\",\"className\":\"Fighter\",\"classSource\":\"PHB\",\"shortName\":\"Battle Master\",\"subclassFeatures\":[\"Combat Superiority|Fighter|PHB|Battle Master|PHB|3\"]}");
        var envelope = new FivetoolsSubclassMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Subclass);
        envelope.Name.Should().Be("Battle Master");
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void SpellMapper_maps_fireball()
    {
        var json = J("{\"name\":\"Fireball\",\"source\":\"PHB\",\"level\":3,\"school\":\"V\",\"time\":[{\"number\":1,\"unit\":\"action\"}],\"range\":{\"type\":\"point\",\"distance\":{\"type\":\"feet\",\"amount\":150}},\"components\":{\"v\":true,\"s\":true,\"m\":\"a tiny ball of bat guano and sulfur\"},\"duration\":[{\"type\":\"instant\"}],\"entries\":[\"A bright streak flashes.\"]}");
        var envelope = new FivetoolsSpellMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Spell);
        envelope.Name.Should().Be("Fireball");
        envelope.DataSource.Should().Be("5etools");
        envelope.Fields.TryGetProperty("school", out var school).Should().BeTrue();
        school.GetString().Should().Be("V");
    }

    [Fact]
    public void MonsterMapper_maps_aboleth()
    {
        var json = J("{\"name\":\"Aboleth\",\"source\":\"MM\",\"size\":[\"L\"],\"type\":\"aberration\",\"alignment\":[\"L\",\"E\"],\"ac\":[16],\"hp\":{\"average\":135,\"formula\":\"18d10+36\"},\"speed\":{\"walk\":10,\"swim\":40},\"str\":21,\"dex\":9,\"con\":15,\"int\":18,\"wis\":15,\"cha\":18,\"cr\":\"10\",\"entries\":[\"A grotesque fish-like creature.\"]}");
        var envelope = new FivetoolsMonsterMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Monster);
        envelope.Name.Should().Be("Aboleth");
        envelope.DataSource.Should().Be("5etools");
        envelope.Fields.TryGetProperty("str", out var str).Should().BeTrue();
        str.GetInt32().Should().Be(21);
    }

    [Fact]
    public void RaceMapper_maps_elf()
    {
        var json = J("{\"name\":\"Elf\",\"source\":\"PHB\",\"size\":[\"M\"],\"speed\":30,\"entries\":[\"Elves are a magical people.\"]}");
        var envelope = new FivetoolsRaceMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Race);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void BackgroundMapper_maps_sage()
    {
        var json = J("{\"name\":\"Sage\",\"source\":\"PHB\",\"skillProficiencies\":[{\"arcana\":true,\"history\":true}],\"entries\":[\"Scholars study.\"]}");
        var envelope = new FivetoolsBackgroundMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Background);
    }

    [Fact]
    public void ItemMapper_skips_magic_items()
    {
        var json = J("{\"name\":\"Sword +1\",\"source\":\"DMG\",\"rarity\":\"uncommon\",\"type\":\"S\"}");
        var envelope = new FivetoolsItemMapper().Map(json);
        envelope.Should().BeNull("magic items are handled by MagicItemMapper");
    }

    [Fact]
    public void MagicItemMapper_maps_uncommon_magic_item()
    {
        var json = J("{\"name\":\"Sword +1\",\"source\":\"DMG\",\"rarity\":\"uncommon\",\"type\":\"S\",\"entries\":[\"A finely crafted blade.\"]}");
        var envelope = new FivetoolsMagicItemMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.MagicItem);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void MagicItemMapper_skips_nonmagic_items()
    {
        var json = J("{\"name\":\"Rope\",\"source\":\"PHB\",\"type\":\"G\"}");
        var envelope = new FivetoolsMagicItemMapper().Map(json);
        envelope.Should().BeNull("non-magic items are handled by ItemMapper");
    }

    [Fact]
    public void GodMapper_maps_deity()
    {
        var json = J("{\"name\":\"Tyr\",\"source\":\"MTF\",\"pantheon\":\"Forgotten Realms\",\"alignment\":[\"L\",\"G\"],\"domains\":[\"War\"],\"symbol\":\"Balanced scales\",\"entries\":[\"God of justice.\"]}");
        var envelope = new FivetoolsGodMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.God);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void TrapMapper_maps_trap()
    {
        var json = J("{\"name\":\"Pit Trap\",\"source\":\"DMG\",\"trapHazType\":\"MECH\",\"entries\":[\"A covered pit.\"]}");
        var envelope = new FivetoolsTrapMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Trap);
    }

    [Fact]
    public void ConditionMapper_maps_condition()
    {
        var json = J("{\"name\":\"Blinded\",\"source\":\"PHB\",\"entries\":[\"A blinded creature cannot see.\"]}");
        var envelope = new FivetoolsConditionMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Condition);
    }

    [Fact]
    public void RuleMapper_maps_variantrule()
    {
        var json = J("{\"name\":\"Flanking\",\"source\":\"DMG\",\"ruleType\":\"O\",\"entries\":[\"When a creature and its ally are on opposite sides of an enemy.\"]}");
        var envelope = new FivetoolsRuleMapper().Map(json);
        envelope!.Type.Should().Be(EntityType.Rule);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void SubraceMapper_maps_entry()
    {
        var json = J("{\"name\":\"High Elf\",\"source\":\"PHB\",\"raceName\":\"Elf\",\"entries\":[\"High elves have a keen mind.\"]}");
        var envelope = new FivetoolsSubraceMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.Subrace);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void FeatMapper_maps_entry()
    {
        var json = J("{\"name\":\"Alert\",\"source\":\"PHB\",\"entries\":[\"Always on the lookout for danger.\"]}");
        var envelope = new FivetoolsFeatMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.Feat);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void WeaponMapper_maps_entry_with_weaponCategory()
    {
        var json = J("{\"name\":\"Longsword\",\"source\":\"PHB\",\"weaponCategory\":\"martial\",\"type\":\"M\"}");
        var envelope = new FivetoolsWeaponMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.Weapon);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void WeaponMapper_returns_null_without_weaponCategory()
    {
        var json = J("{\"name\":\"Rope\",\"source\":\"PHB\",\"type\":\"G\"}");
        var envelope = new FivetoolsWeaponMapper().Map(json);
        envelope.Should().BeNull("entries without weaponCategory are not weapons");
    }

    [Fact]
    public void ArmorMapper_maps_entry_with_armor_type()
    {
        var json = J("{\"name\":\"Leather Armor\",\"source\":\"PHB\",\"type\":\"LA\",\"ac\":11}");
        var envelope = new FivetoolsArmorMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.Armor);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void ArmorMapper_returns_null_for_non_armor_type()
    {
        var json = J("{\"name\":\"Handaxe\",\"source\":\"PHB\",\"type\":\"W\"}");
        var envelope = new FivetoolsArmorMapper().Map(json);
        envelope.Should().BeNull("type W is not an armor type");
    }

    [Fact]
    public void DiseasePoisonMapper_maps_entry()
    {
        var json = J("{\"name\":\"Cackle Fever\",\"source\":\"DMG\",\"entries\":[\"A disease that causes uncontrollable laughter.\"]}");
        var envelope = new FivetoolsDiseasePoisonMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.DiseasePoison);
        envelope.DataSource.Should().Be("5etools");
    }

    [Fact]
    public void VehicleMapper_maps_entry()
    {
        var json = J("{\"name\":\"Warhorse\",\"source\":\"PHB\",\"entries\":[\"A powerful horse bred for war.\"]}");
        var envelope = new FivetoolsVehicleMapper().Map(json);
        envelope.Should().NotBeNull();
        envelope!.Type.Should().Be(EntityType.VehicleMount);
        envelope.DataSource.Should().Be("5etools");
    }
}
