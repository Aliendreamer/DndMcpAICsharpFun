using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
public static class FivetoolsEntityTypeMap
{
    private static readonly HashSet<string> MagicRarities =
        new(StringComparer.OrdinalIgnoreCase) { "common", "uncommon", "rare", "very rare", "legendary", "artifact" };
    public static EntityType ForItem(string? rarity) =>
        rarity is not null && MagicRarities.Contains(rarity) ? EntityType.MagicItem : EntityType.Item;
}
