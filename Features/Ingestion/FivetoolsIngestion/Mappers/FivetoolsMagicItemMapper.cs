using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

/// <summary>
/// Maps magic items — entries that carry a non-"none" rarity.
/// Mundane items (no rarity or rarity == "none") are handled by <see cref="FivetoolsItemMapper"/>.
/// </summary>
public sealed class FivetoolsMagicItemMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.MagicItem;

    public override EntityEnvelope? Map(JsonElement entry)
    {
        if (!entry.TryGetProperty("rarity", out var r)
            || r.ValueKind != JsonValueKind.String
            || string.Equals(r.GetString(), "none", StringComparison.OrdinalIgnoreCase))
            return null;
        return base.Map(entry);
    }
}