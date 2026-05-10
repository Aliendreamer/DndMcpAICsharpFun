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

        var srd            = existing.DataSource == "5etools" ? existing.Srd            : canonical.Srd;
        var srd52          = existing.DataSource == "5etools" ? existing.Srd52          : canonical.Srd52;
        var basicRules2024 = existing.DataSource == "5etools" ? existing.BasicRules2024 : canonical.BasicRules2024;

        return canonical with
        {
            Type           = type,
            Srd            = srd,
            Srd52          = srd52,
            BasicRules2024 = basicRules2024,
            Keywords       = keywords,
            Page           = page,
        };
    }
}
