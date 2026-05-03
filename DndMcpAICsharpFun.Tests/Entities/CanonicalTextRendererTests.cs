using DndMcpAICsharpFun.Domain.Entities.Fields;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class CanonicalTextRendererTests
{
    [Fact]
    public void Class_text_includes_hitdie_and_saves_and_level1_features()
    {
        var fields = new ClassFields(
            HitDie: "d10",
            PrimaryAbilities: new[] { "Strength" },
            SavingThrowProficiencies: new[] { "Strength", "Constitution" },
            ArmorProficiencies: Array.Empty<string>(),
            WeaponProficiencies: Array.Empty<string>(),
            ToolProficiencies: Array.Empty<string>(),
            SkillChoices: new SkillChoice(2, new[] { "Athletics" }),
            StartingEquipment: Array.Empty<EquipmentChoice>(),
            Multiclass: new MulticlassBlock(
                new MulticlassPrerequisites("or", new Dictionary<string, int> { ["Strength"] = 13 }),
                Array.Empty<string>()),
            Spellcasting: null,
            SubclassSelectionLevel: 3,
            Subclasses: Array.Empty<string>(),
            AsiLevels: Array.Empty<int>(),
            FeaturesByLevel: new[] {
                new ClassLevelEntry(1, 2, new[] {
                    new FeatureRef("Fighting Style", "x", "Pick a Fighting Style.")
                })
            });

        var text1 = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        var text2 = new ClassCanonicalTextRenderer().Render("Fighter", fields);
        text1.Should().Be(text2); // determinism
        text1.Should().Contain("Fighter").And.Contain("d10").And.Contain("Strength, Constitution").And.Contain("Fighting Style");
    }

    [Fact]
    public void Spell_text_includes_at_higher_levels()
    {
        var fields = new SpellFields(
            Level: 3, School: "evocation",
            CastingTime: "1 action", Range: "150 feet",
            Components: new SpellComponents(true, true, true, "guano"),
            Duration: "Instantaneous", Ritual: false, Concentration: false,
            Description: "A bright streak.",
            AtHigherLevels: "Damage increases.",
            Classes: new[] { "Wizard" }, DamageTypes: new[] { "fire" });
        var text = new SpellCanonicalTextRenderer().Render("Fireball", fields);
        text.Should().Contain("3rd-level evocation").And.Contain("Damage increases.");
    }
}
