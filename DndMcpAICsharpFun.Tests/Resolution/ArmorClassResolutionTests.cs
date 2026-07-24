using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ArmorClassResolutionTests
{
    private static CharacterSheet Sheet(string @class, int level, int dex = 10, int con = 10, int wis = 10,
        string armor = "", bool shield = false, int magic = 0)
    {
        var s = new CharacterSheet { Dexterity = dex, Constitution = con, Wisdom = wis };
        s.Classes = [new ClassLevel { Class = @class, Level = level }];
        s.WornArmor = new WornArmor { ArmorName = armor, Shield = shield, MagicBonus = magic };
        return s;
    }

    [Fact]
    public void Heavy_armor_ignores_dex() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Fighter", 1, dex: 16, armor: "Plate")).Value.Should().Be("18");

    [Fact]
    public void Medium_armor_caps_dex_at_2() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Cleric", 1, dex: 18, armor: "Half Plate")).Value.Should().Be("17");

    [Fact]
    public void Light_armor_adds_full_dex() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Rogue", 1, dex: 16, armor: "Leather")).Value.Should().Be("14");

    [Fact]
    public void Shield_and_magic_add_on_top()
    {
        var fact = CharacterResolutionService.ResolveArmorClass(Sheet("Fighter", 1, dex: 10, armor: "Chain Mail", shield: true, magic: 1));
        fact.Value.Should().Be("19"); // 16 + 2 + 1
        fact.Components.Should().Contain(c => c.Label == "shield").And.Contain(c => c.Label == "magic");
    }

    [Fact]
    public void Barbarian_unarmored_defense_with_shield()
    {
        // 10 + Dex(+2) + Con(+3) + shield(2) = 17
        CharacterResolutionService.ResolveArmorClass(Sheet("Barbarian", 3, dex: 14, con: 16, shield: true)).Value.Should().Be("17");
    }

    [Fact]
    public void Monk_unarmored_defense_suppressed_by_shield()
    {
        // Monk UD not allowed with a shield → 10 + Dex(+2) + shield(2) = 14
        CharacterResolutionService.ResolveArmorClass(Sheet("Monk", 3, dex: 14, wis: 16, shield: true)).Value.Should().Be("14");
    }

    [Fact]
    public void Multiclass_takes_higher_unarmored_defense()
    {
        // Barbarian/Monk, no shield: max(10+Dex+Con, 10+Dex+Wis). Con 16(+3) > Wis 12(+1) → Barbarian.
        var s = new CharacterSheet
        {
            Dexterity = 14,
            Constitution = 16,
            Wisdom = 12,
            Classes = [new ClassLevel { Class = "Barbarian", Level = 2 }, new ClassLevel { Class = "Monk", Level = 2 }],
            WornArmor = new WornArmor(),
        };
        CharacterResolutionService.ResolveArmorClass(s).Value.Should().Be("15"); // 10+2+3
    }

    [Fact]
    public void Unarmored_defense_win_omits_the_superseded_base_component()
    {
        var fact = CharacterResolutionService.ResolveArmorClass(Sheet("Barbarian", 3, dex: 14, con: 16));
        fact.Components.Should().Contain(c => c.Label == "unarmored defense");
        fact.Components.Should().NotContain(c => c.Label == "base");
    }

    [Fact]
    public void Unknown_armor_is_needsReview() =>
        CharacterResolutionService.ResolveArmorClass(Sheet("Fighter", 1, armor: "Mithral Plate")).Confidence.Should().Be("needsReview");

    [Fact]
    public void Default_worn_armor_is_unarmored()
    {
        var s = new CharacterSheet { Dexterity = 12, Classes = [new ClassLevel { Class = "Wizard", Level = 1 }] }; // WornArmor defaults to new()
        CharacterResolutionService.ResolveArmorClass(s).Value.Should().Be("11"); // 10 + Dex(+1)
    }
}