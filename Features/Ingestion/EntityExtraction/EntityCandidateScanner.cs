using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityCandidateScanner
{
    public IEnumerable<EntityCandidate> Scan(IList<ScannerInput> blocks, TocCategoryMap toc)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(toc);

        // Group consecutive (and any same-titled) blocks by section title; preserve first-occurrence order.
        var bySection = blocks
            .Select((b, i) => (Block: b, Order: i))
            .GroupBy(x => x.Block.SectionTitle)
            .Select(g => new
            {
                Section = g.Key,
                FirstIndex = g.Min(x => x.Order),
                Page = g.Min(x => x.Block.Page),
                Text = string.Join("\n\n", g.OrderBy(x => x.Order).Select(x => x.Block.Text)),
            })
            .OrderBy(s => s.FirstIndex);

        foreach (var s in bySection)
        {
            // TocCategoryMap is page-keyed: look up the category by the section's earliest page.
            var category = toc.GetCategory(s.Page) ?? ContentCategory.Unknown;
            var type = MapCategoryToEntityType(category);
            if (type is null) continue;

            // Content-first prior: the primary keyword type plus its confusion set and the
            // frequency floor, mapped to entity types. Offered to the model as union branches so
            // it can re-type away from a wrong keyword guess (e.g. Dragonborn -> Race, not Monster).
            var prior = HeadingCategoryClassifier.ExpandPrior(category)
                .Select(MapCategoryToEntityType)
                .Where(t => t is not null)
                .Select(t => t!.Value)
                .Distinct()
                .ToList();

            yield return new EntityCandidate(type.Value, s.Section, s.Text, s.Page, prior);
        }
    }

    private static EntityType? MapCategoryToEntityType(ContentCategory category) => category switch
    {
        ContentCategory.Spell      => EntityType.Spell,
        ContentCategory.Monster    => EntityType.Monster,
        ContentCategory.Class      => EntityType.Class,
        ContentCategory.Race       => EntityType.Race,
        ContentCategory.Background => EntityType.Background,
        ContentCategory.Item       => EntityType.Item,
        ContentCategory.Condition  => EntityType.Condition,
        ContentCategory.God        => EntityType.God,
        ContentCategory.Plane      => EntityType.Plane,
        ContentCategory.Treasure   => EntityType.MagicItem,
        ContentCategory.Trap       => EntityType.Trap,
        // Rule, Combat, Adventuring, Encounter, Trait, Lore, Unknown -> not entities (skipped).
        _ => null,
    };
}
