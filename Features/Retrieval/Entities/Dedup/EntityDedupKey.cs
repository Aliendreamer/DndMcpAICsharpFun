using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>Identity of a real-world entity for corpus dedup: normalized name + type + edition.</summary>
public readonly record struct EntityDedupKey(string NormalizedName, EntityType Type, string Edition)
{
    public static EntityDedupKey From(EntityEnvelope e) =>
        new(EntityNameIndex.Normalize(e.Name), e.Type, e.Edition);
}