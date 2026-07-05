using DndMcpAICsharpFun.Domain;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Domain;

public sealed class CharacterSheetClassesTests
{
    [Fact]
    public void Multiclass_derives_total_level_primary_class_and_proficiency_bonus()
    {
        var sheet = new CharacterSheet
        {
            Classes =
            [
                new ClassLevel { Class = "Rogue", Level = 3, Subclass = "Thief" },
                new ClassLevel { Class = "Fighter", Level = 2, Subclass = "" },
            ],
        };

        sheet.Level.Should().Be(5);              // total level = Σ per-class
        sheet.Class.Should().Be("Rogue");        // primary = Classes[0]
        sheet.Subclass.Should().Be("Thief");
        sheet.ProficiencyBonus.Should().Be(3);   // 5th-level PB
    }

    [Fact]
    public void SetSingleClass_replaces_the_class_list_with_one_entry()
    {
        var sheet = new CharacterSheet();
        sheet.SetSingleClass("Wizard", "Evocation", 5);

        sheet.Classes.Should().ContainSingle();
        sheet.Class.Should().Be("Wizard");
        sheet.Subclass.Should().Be("Evocation");
        sheet.Level.Should().Be(5);
    }

    [Fact]
    public void Empty_sheet_has_safe_defaults()
    {
        var sheet = new CharacterSheet();
        sheet.Class.Should().Be("");
        sheet.Level.Should().Be(0);
        sheet.ProficiencyBonus.Should().Be(2); // floor: level treated as >=1
    }

    [Fact]
    public void ProficiencyBonusForLevel_matches_the_5e_progression_table()
    {
        CharacterSheet.ProficiencyBonusForLevel(0).Should().Be(2);
        CharacterSheet.ProficiencyBonusForLevel(1).Should().Be(2);
        CharacterSheet.ProficiencyBonusForLevel(5).Should().Be(3);
        CharacterSheet.ProficiencyBonusForLevel(20).Should().Be(6);
    }
}
