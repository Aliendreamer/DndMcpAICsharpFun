using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Resolution;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class MulticlassSpellcastingTests
{
    private static ClassLevel C(string cls, int lvl, string sub = "") =>
        new() { Class = cls, Level = lvl, Subclass = sub };

    [Fact]
    public void Full_casters_sum_directly()
    {
        MulticlassSpellcasting.CombinedCasterLevel([C("Wizard", 5), C("Cleric", 3)])
            .Should().Be(8);
    }

    [Fact]
    public void Half_casters_round_down_and_add_to_full()
    {
        // Paladin 6 -> floor(6/2)=3 ; + Sorcerer 2 (full) = 5
        MulticlassSpellcasting.CombinedCasterLevel([C("Paladin", 6), C("Sorcerer", 2)])
            .Should().Be(5);
    }

    [Fact]
    public void Artificer_rounds_up()
    {
        // Artificer 3 -> ceil(3/2)=2
        MulticlassSpellcasting.CombinedCasterLevel([C("Artificer", 3)]).Should().Be(2);
    }

    [Fact]
    public void Third_caster_subclass_rounds_down()
    {
        // Fighter 7 Eldritch Knight -> floor(7/3)=2 ; plain Fighter contributes 0
        MulticlassSpellcasting.CombinedCasterLevel([C("Fighter", 7, "Eldritch Knight")]).Should().Be(2);
        MulticlassSpellcasting.CombinedCasterLevel([C("Fighter", 7, "Champion")]).Should().Be(0);
    }

    [Fact]
    public void Warlock_is_excluded_from_combined_level_and_reported_separately()
    {
        var classes = new[] { C("Warlock", 3), C("Sorcerer", 2) };
        MulticlassSpellcasting.CombinedCasterLevel(classes).Should().Be(2); // only the Sorcerer
        var pact = MulticlassSpellcasting.WarlockPact(classes)!;
        pact.SlotCount.Should().Be(2);   // Warlock 3 => 2 pact slots
        pact.SlotLevel.Should().Be(2);   // Warlock 3 => 2nd-level slots
    }

    [Fact]
    public void Non_casters_yield_zero_and_no_pact()
    {
        MulticlassSpellcasting.CombinedCasterLevel([C("Rogue", 3), C("Fighter", 2)]).Should().Be(0);
        MulticlassSpellcasting.WarlockPact([C("Rogue", 3)]).Should().BeNull();
    }

    [Fact]
    public void Spellcasting_ability_is_per_class()
    {
        MulticlassSpellcasting.SpellcastingAbility("Cleric").Should().Be("Wisdom");
        MulticlassSpellcasting.SpellcastingAbility("Wizard").Should().Be("Intelligence");
        MulticlassSpellcasting.SpellcastingAbility("Rogue").Should().BeNull();
    }


    [Fact]
    public void Warlock_level_2_has_two_first_level_pact_slots()
    {
        var pact = MulticlassSpellcasting.WarlockPact([C("Warlock", 2)])!;
        pact.SlotCount.Should().Be(2);
        pact.SlotLevel.Should().Be(1);   // NOT 2 — slot level advances at Warlock 3
    }

    [Fact]
    public void Warlock_level_5_has_two_third_level_pact_slots()
    {
        var pact = MulticlassSpellcasting.WarlockPact([C("Warlock", 5)])!;
        pact.SlotCount.Should().Be(2);
        pact.SlotLevel.Should().Be(3);
    }

    [Fact]
    public void Third_caster_subclass_on_wrong_parent_class_is_not_a_caster()
    {
        // A malformed ClassLevel pairing an EK subclass with a non-Fighter class must NOT be a caster.
        MulticlassSpellcasting.CombinedCasterLevel([C("Barbarian", 6, "Eldritch Knight")]).Should().Be(0);
        MulticlassSpellcasting.Classify(C("Barbarian", 6, "Eldritch Knight"))
            .Should().Be(CasterType.None);
    }


    [Fact]
    public void Single_class_paladin_reads_the_half_caster_table_at_class_level()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Paladin", 5)]);
        s.Kind.Should().Be("half"); s.Level.Should().Be(5);   // NOT combined level 2
    }

    [Fact]
    public void Single_class_eldritch_knight_reads_the_third_caster_table_at_class_level()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Fighter", 7, "Eldritch Knight")]);
        s.Kind.Should().Be("third"); s.Level.Should().Be(7);  // NOT combined level 2
    }

    [Fact]
    public void Single_class_wizard_uses_the_multiclass_table_which_coincides()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Wizard", 5)]);
        s.Kind.Should().Be("multiclass"); s.Level.Should().Be(5);
    }

    [Fact]
    public void Two_spellcasting_classes_use_the_combined_multiclass_table()
    {
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Paladin", 6), C("Sorcerer", 2)]);
        s.Kind.Should().Be("multiclass"); s.Level.Should().Be(5);  // ⌊6/2⌋+2
    }

    [Fact]
    public void One_spellcasting_class_plus_warlock_reads_that_class_table_not_combined()
    {
        // Warlock is Pact (not a "spellcasting class" for the fork); Paladin is the sole caster class.
        var s = MulticlassSpellcasting.ResolveSlotSource([C("Warlock", 3), C("Paladin", 2)]);
        s.Kind.Should().Be("half"); s.Level.Should().Be(2);
    }
}
