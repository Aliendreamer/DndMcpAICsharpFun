using System.Collections.Frozen;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

/// <summary>
/// Maps armor entries — items whose "type" code is one of LA, MA, HA, or S (shield).
/// </summary>
public sealed class FivetoolsArmorMapper : FivetoolsMapperBase
{
    private static readonly FrozenSet<string> ArmorTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "LA", "MA", "HA", "S" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    protected override EntityType EntityType => EntityType.Armor;

    public override EntityEnvelope? Map(JsonElement entry)
    {
        if (!entry.TryGetProperty("type", out var t)
            || t.ValueKind != JsonValueKind.String
            || !ArmorTypes.Contains(t.GetString()!))
            return null;
        return base.Map(entry);
    }
}
