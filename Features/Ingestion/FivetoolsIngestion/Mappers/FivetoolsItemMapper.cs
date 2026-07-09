using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

/// <summary>
/// Maps non-magic mundane items (no rarity, or rarity == "none").
/// Magic items are handled by <see cref="FivetoolsMagicItemMapper"/>.
/// </summary>
public sealed class FivetoolsItemMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Item;

    public override EntityEnvelope? Map(JsonElement entry)
    {
        if (entry.TryGetProperty("rarity", out var r)
            && r.ValueKind == JsonValueKind.String
            && !string.Equals(r.GetString(), "none", StringComparison.OrdinalIgnoreCase))
            return null;
        return base.Map(entry);
    }
}