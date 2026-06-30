using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityCandidateScanner(ILogger<EntityCandidateScanner> logger)
{
    public IEnumerable<EntityCandidate> Scan(IList<ScannerInput> blocks, TocCategoryMap toc)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(toc);

        // Single ordered pass: consecutive same-titled blocks within MaxPageGap pages merge into
        // one candidate; a different title OR a page jump > MaxPageGap starts a new group.
        // Prevents cross-chapter name collisions (e.g. "Darkvision" invocation vs spell far apart).
        const int MaxPageGap = 3;

        var groups = new List<(string Section, int FirstIndex, int Page, string Text)>();

        if (blocks.Count > 0)
        {
            var currentTitle = blocks[0].SectionTitle;
            var groupFirstIndex = 0;
            var groupMinPage = blocks[0].Page;
            var groupMaxPage = blocks[0].Page;
            var groupTexts = new List<(int Order, string Text)> { (0, blocks[0].Text) };

            for (var i = 1; i < blocks.Count; i++)
            {
                var block = blocks[i];
                var sameTitle = block.SectionTitle == currentTitle;
                var withinGap = block.Page - groupMaxPage <= MaxPageGap;

                if (sameTitle && withinGap)
                {
                    groupMinPage = Math.Min(groupMinPage, block.Page);
                    groupMaxPage = Math.Max(groupMaxPage, block.Page);
                    groupTexts.Add((i, block.Text));
                }
                else
                {
                    groups.Add((
                        currentTitle,
                        groupFirstIndex,
                        groupMinPage,
                        string.Join("\n\n", groupTexts.OrderBy(x => x.Order).Select(x => x.Text))
                    ));
                    currentTitle = block.SectionTitle;
                    groupFirstIndex = i;
                    groupMinPage = block.Page;
                    groupMaxPage = block.Page;
                    groupTexts = [(i, block.Text)];
                }
            }

            groups.Add((
                currentTitle,
                groupFirstIndex,
                groupMinPage,
                string.Join("\n\n", groupTexts.OrderBy(x => x.Order).Select(x => x.Text))
            ));
        }

        foreach (var s in groups.OrderBy(s => s.FirstIndex))
        {
            // TocCategoryMap is page-keyed: look up the category by the section's earliest page.
            var category = toc.GetCategory(s.Page) ?? ContentCategory.Unknown;
            var type = MapCategoryToEntityType(category);
            if (type is null)
            {
                // Traceability guard: log skipped sections so silent candidate losses are visible.
                logger.LogWarning(
                    "Skipping section '{Section}' on page {Page}: no entity category maps to this page (toc category: {Category})",
                    s.Section, s.Page, category);
                continue;
            }

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
