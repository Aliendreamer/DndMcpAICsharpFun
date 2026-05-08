using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

/// <summary>
/// Maps weapon entries — items that carry a "weaponCategory" property.
/// </summary>
public sealed class FivetoolsWeaponMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Weapon;

    public override EntityEnvelope? Map(JsonElement entry)
    {
        if (!entry.TryGetProperty("weaponCategory", out var wc)
            || wc.ValueKind != JsonValueKind.String)
            return null;
        return base.Map(entry);
    }
}
