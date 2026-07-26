using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

/// <summary>
/// Every "bad" string here was observed as an entity NAME in a live run: EGW produced 7 Item and
/// most of 12 Rule entities named by their magic-item stat line, and SCC the same shape. See
/// openspec/changes/extraction-statline-name-repair.
/// </summary>
public sealed class MagicItemStatLineNameTests
{
    // ---- Strip: a real name with a TRAILING stat line is repaired --------------------------

    [Fact]
    public void Strip_amulet_with_trailing_statline_removes_suffix() =>
        MagicItemStatLineName.Strip("AMULET OF THE DRUNKARD Wondrous item, uncommon")
            .Should().Be("AMULET OF THE DRUNKARD");

    [Fact]
    public void Strip_weapon_statline_with_subtype_and_attunement_removes_suffix() =>
        MagicItemStatLineName.Strip("Sun Blade Weapon (longsword), rare (requires attunement)")
            .Should().Be("Sun Blade");

    [Fact]
    public void Strip_armor_statline_removes_suffix() =>
        MagicItemStatLineName.Strip("Elven Chain Armor (chain shirt), rare")
            .Should().Be("Elven Chain");

    // ---- Strip: the whole input IS the stat line -> unchanged (so the name gate rejects it) --

    [Theory]
    [InlineData("Wondrous item, uncommon")]
    [InlineData("Wondrous item, very rare")]
    [InlineData("Weapon (greataxe), very rare (requires attunement)")]
    [InlineData("Armor (studded leather), legendary (requires attunement)")]
    [InlineData("Ring, rare (requires attunement)")]
    public void Strip_whole_input_is_statline_returns_unchanged(string name) =>
        MagicItemStatLineName.Strip(name).Should().Be(name);

    // ---- Strip: names with no stat line are untouched ---------------------------------------

    [Theory]
    [InlineData("Moorbounder")]
    [InlineData("Path of the Battlerager")]
    [InlineData("Ring of Spell Storing")]
    [InlineData("Potion of Healing")]
    [InlineData("Acheron Blade")]
    public void Strip_clean_name_returns_unchanged(string name) =>
        MagicItemStatLineName.Strip(name).Should().Be(name);

    // ---- IsWholeStatLine: drives the name-gate reject ---------------------------------------

    [Theory]
    [InlineData("Wondrous item, very rare")]
    [InlineData("Wondrous item, common")]
    [InlineData("Wondrous item, uncommon")]
    [InlineData("Wondrous item, legendary")]
    [InlineData("Wondrous Item, Uncommon (Requires Attunement by a Spellcaster)")]
    [InlineData("Potion, very rare")]
    [InlineData("Potion, rare")]
    [InlineData("Weapon (greataxe), very rare (requires attunement)")]
    [InlineData("Weapon (whip), artifact (requires attunement)")]
    [InlineData("Weapon (mace), artifact (requires attunement)")]
    [InlineData("Armor (studded leather), legendary (requires attunement)")]
    [InlineData("Ring, rare (requires attunement)")]
    public void IsWholeStatLine_true_for_observed_junk_names(string name) =>
        MagicItemStatLineName.IsWholeStatLine(name).Should().BeTrue();

    [Theory]
    [InlineData("Ring of Spell Storing")]
    [InlineData("Potion of Healing")]
    [InlineData("Acheron Blade")]
    [InlineData("Battering Shield")]
    [InlineData("Brooch of Living Essence")]
    [InlineData("Moorbounder")]
    [InlineData("Path of the Battlerager")]
    [InlineData("AMULET OF THE DRUNKARD Wondrous item, uncommon")]
    // A real item name that merely ENDS in a rarity must not read as a bare stat line: the gap
    // between the category token and the comma may only be empty or a parenthesised sub-type.
    [InlineData("Wand of Fireballs, rare")]
    [InlineData("Staff of the Woodlands, very rare")]
    public void IsWholeStatLine_false_for_real_entity_names(string name) =>
        MagicItemStatLineName.IsWholeStatLine(name).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsWholeStatLine_false_for_blank(string name) =>
        MagicItemStatLineName.IsWholeStatLine(name).Should().BeFalse();
}