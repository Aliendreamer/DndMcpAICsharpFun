using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;
public sealed class FivetoolsEntityTypeMapTests
{
    [Theory]
    [InlineData("uncommon", EntityType.MagicItem)]
    [InlineData("legendary", EntityType.MagicItem)]
    [InlineData("rare", EntityType.MagicItem)]
    [InlineData("none", EntityType.Item)]
    [InlineData(null, EntityType.Item)]
    [InlineData("unknown", EntityType.Item)]
    public void ForItem_maps_rarity(string? rarity, EntityType expected) =>
        FivetoolsEntityTypeMap.ForItem(rarity).Should().Be(expected);
}
