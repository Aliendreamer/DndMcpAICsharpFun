using DndMcpAICsharpFun.Domain;
using System.Text.Json;
using DndMcpAICsharpFun.Features.Campaigns;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Campaign;

public sealed class CharacterSheetSerializationTests
{
    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var sheet = new CharacterSheet
        {
            Race = "Elf", Class = "Wizard", Subclass = "Divination",
            Background = "Sage", Level = 5, Alignment = "Neutral Good", ExperiencePoints = 6500,
            Strength = 8, Dexterity = 16, Constitution = 14,
            Intelligence = 18, Wisdom = 12, Charisma = 10,
            MaxHitPoints = 32, CurrentHitPoints = 28, ArmorClass = 13,
            Speed = 30, Initiative = 3, ProficiencyBonus = 3,
            SpellcastingAbility = "Intelligence", SpellSaveDC = 15, SpellAttackBonus = 7,
            SpellSlots = [4, 3, 2, 1, 0, 0, 0, 0, 0],
            UsedSpellSlots = [1, 0, 0, 0, 0, 0, 0, 0, 0],
            SpellsKnown = ["Fireball", "Counterspell"],
            WeaponProficiencies = ["Daggers", "Darts"],
            Languages = ["Common", "Elvish"],
            SkillProficiencies = ["Arcana", "History"],
            Features = [new CharacterFeature { Name = "Portent", Description = "Roll 2d20 at dawn." }],
            Equipment = ["Spellbook", "Wand of Magic Missile"]
        };

        var json = JsonSerializer.Serialize(sheet);
        var restored = JsonSerializer.Deserialize<CharacterSheet>(json)!;

        restored.Race.Should().Be("Elf");
        restored.Level.Should().Be(5);
        restored.SpellSlots.Should().Equal([4, 3, 2, 1, 0, 0, 0, 0, 0]);
        restored.SpellsKnown.Should().Equal(["Fireball", "Counterspell"]);
        restored.Features.Should().HaveCount(1);
        restored.Features[0].Name.Should().Be("Portent");
        restored.Features[0].Description.Should().Be("Roll 2d20 at dawn.");
        restored.Equipment.Should().Equal(["Spellbook", "Wand of Magic Missile"]);
    }

    [Fact]
    public void Modifier_ComputesCorrectly()
    {
        CharacterSheet.Modifier(10).Should().Be(0);
        CharacterSheet.Modifier(8).Should().Be(-1);
        CharacterSheet.Modifier(16).Should().Be(3);
        CharacterSheet.Modifier(20).Should().Be(5);
        // odd scores below 10 require floor division, not truncation
        CharacterSheet.Modifier(9).Should().Be(-1);
        CharacterSheet.Modifier(7).Should().Be(-2);
        CharacterSheet.Modifier(1).Should().Be(-5);
    }

    [Fact]
    public void ModifierStr_FormatsWithSign()
    {
        CharacterSheet.ModifierStr(16).Should().Be("+3");
        CharacterSheet.ModifierStr(8).Should().Be("-1");
        CharacterSheet.ModifierStr(10).Should().Be("+0");
    }
}
