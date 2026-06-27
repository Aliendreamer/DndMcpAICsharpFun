using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class DeterministicTypeResolverTests
{
    private static EntityCandidate C(string name, string text) =>
        new(EntityType.Monster, name, text, 1, new[] { EntityType.Monster });

    [Fact]
    public void Non_entity_name_is_dropped()
    {
        var r = DeterministicTypeResolver.Resolve(C("ACTIONS", "Armor Class 14 Hit Points 30 Challenge 1 (200 XP)"));
        r.Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    [Fact]
    public void Complete_stat_block_with_creature_name_forces_Monster()
    {
        var r = DeterministicTypeResolver.Resolve(C("Aboleth", "Large aberration  Armor Class 17  Hit Points 135  Challenge 10 (5,900 XP)"));
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.Monster);
    }

    [Fact]
    public void Tutorial_fragment_stat_block_is_not_forced_Monster()
    {
        var r = DeterministicTypeResolver.Resolve(C("Step 2. Basic Statistics", "Armor Class 15  Hit Points 100  Challenge 5 (1,800 XP)"));
        r.Outcome.Should().Be(DeterministicOutcome.Drop);
    }

    [Fact]
    public void Magic_item_signature_forces_MagicItem()
    {
        var r = DeterministicTypeResolver.Resolve(C("Vorpal Sword", "Weapon (any sword that deals slashing damage), legendary (requires attunement)"));
        r.Outcome.Should().Be(DeterministicOutcome.ForceType);
        r.ForcedType.Should().Be(EntityType.MagicItem);
    }

    [Fact]
    public void Ordinary_entity_defers_to_union()
    {
        var r = DeterministicTypeResolver.Resolve(C("Fireball", "A bright streak flashes to a point you choose, then blossoms into flame."));
        r.Outcome.Should().Be(DeterministicOutcome.Defer);
    }
}
