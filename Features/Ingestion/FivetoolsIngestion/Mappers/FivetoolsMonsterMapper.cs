using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;

public sealed class FivetoolsMonsterMapper : FivetoolsMapperBase
{
    protected override EntityType EntityType => EntityType.Monster;

    protected override IReadOnlyList<string> GetKeywords(JsonElement entry)
    {
        if (!entry.TryGetProperty("traitTags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return tags.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!)
            .ToList();
    }
}
