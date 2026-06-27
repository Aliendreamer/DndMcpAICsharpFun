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
}
