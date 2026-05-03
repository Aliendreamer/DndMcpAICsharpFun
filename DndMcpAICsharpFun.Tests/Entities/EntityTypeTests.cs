using DndMcpAICsharpFun.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class EntityTypeTests
{
    [Fact]
    public void Has_All_Twenty_Types()
    {
        var values = Enum.GetValues<EntityType>();
        values.Should().HaveCount(20);
        values.Should().Contain(new[]
        {
            EntityType.Class, EntityType.Subclass, EntityType.Race, EntityType.Subrace,
            EntityType.Background, EntityType.Feat, EntityType.Spell,
            EntityType.Weapon, EntityType.Armor, EntityType.Item, EntityType.MagicItem,
            EntityType.Monster, EntityType.Trap, EntityType.DiseasePoison, EntityType.VehicleMount,
            EntityType.God, EntityType.Plane, EntityType.Faction, EntityType.Location,
            EntityType.Condition,
        });
    }
}
