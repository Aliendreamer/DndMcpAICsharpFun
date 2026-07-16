using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class EntityNameIndexTests
{
    private static readonly EntityNameIndex Index = new(TestPaths.RepoFile("5etools"));

    [Fact]
    public void Loads_fireball_as_spell() =>
        Index.Entries[EntityNameIndex.Normalize("FIREBALL")]
            .Should().Be(("Fireball", EntityType.Spell));

    [Fact]
    public void Loads_aboleth_as_monster() =>
        Index.Entries[EntityNameIndex.Normalize("ABOLETH")]
            .Should().Be(("Aboleth", EntityType.Monster));

    [Fact]
    public void Loads_bard_as_class() =>
        Index.Entries[EntityNameIndex.Normalize("BARD")]
            .Should().Be(("Bard", EntityType.Class));

    [Fact]
    public void Loads_bag_of_holding_as_magic_item() =>
        Index.Entries[EntityNameIndex.Normalize("BAG OF HOLDING")]
            .Should().Be(("Bag of Holding", EntityType.MagicItem));

    [Fact]
    public void Does_not_contain_spellcasting() =>
        Index.Entries.Should().NotContainKey(EntityNameIndex.Normalize("Spellcasting"));

    [Fact]
    public void Does_not_contain_archery() =>
        Index.Entries.Should().NotContainKey(EntityNameIndex.Normalize("Archery"));

    // ── Subclass roster (extraction-authority-ladder Tier 1) ─────────────────────────

    // "Path of the Battlerager" is a subclass[] entry in class-barbarian.json (source SCAG).
    [Fact]
    public void Loads_path_of_the_battlerager_as_subclass() =>
        Index.Entries[EntityNameIndex.Normalize("Path of the Battlerager")]
            .Should().Be(("Path of the Battlerager", EntityType.Subclass));

    // Bare shortName "Battlerager" (distinct from the full name) must also resolve, grounding
    // to the subclass's canonical (full) name.
    [Fact]
    public void Loads_battlerager_shortname_as_subclass() =>
        Index.Entries[EntityNameIndex.Normalize("Battlerager")]
            .Should().Be(("Path of the Battlerager", EntityType.Subclass));

    // "Mastermind" (class-rogue.json) is a subclass whose shortName equals its full name.
    [Fact]
    public void Loads_mastermind_shortname_as_subclass() =>
        Index.Entries[EntityNameIndex.Normalize("Mastermind")]
            .Should().Be(("Mastermind", EntityType.Subclass));

    // Base classes are loaded before subclasses, so a name collision (none expected today, but
    // this locks the ordering guarantee) resolves to Class.
    [Fact]
    public void Loads_barbarian_as_class_not_subclass() =>
        Index.Entries[EntityNameIndex.Normalize("Barbarian")]
            .Should().Be(("Barbarian", EntityType.Class));
}