using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public static class EntityMerger
{
    public static EntityEnvelope Merge(EntityEnvelope canonical, EntityEnvelope existing)
    {
        var type = canonical.Type == EntityType.Class
            ? existing.Type
            : canonical.Type;

        var keywords = existing.Keywords.Count >= canonical.Keywords.Count
            ? existing.Keywords
            : canonical.Keywords;

        var page = existing.Page ?? canonical.Page;

        return canonical with
        {
            Type           = type,
            Srd            = existing.Srd,
            Srd52          = existing.Srd52,
            BasicRules2024 = existing.BasicRules2024,
            Keywords       = keywords,
            Page           = page,
            DataSource     = "llm",
        };
    }
}
