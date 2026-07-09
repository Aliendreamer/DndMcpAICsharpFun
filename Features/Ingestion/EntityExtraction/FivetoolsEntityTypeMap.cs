using System.Collections.Frozen;

using DndMcpAICsharpFun.Domain.Entities;
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public static class FivetoolsEntityTypeMap
{
    private static readonly FrozenSet<string> MagicRarities =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "common", "uncommon", "rare", "very rare", "legendary", "artifact" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    public static EntityType ForItem(string? rarity) =>
        rarity is not null && MagicRarities.Contains(rarity) ? EntityType.MagicItem : EntityType.Item;
}