using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityCandidateScanner(ILogger<EntityCandidateScanner> logger)
{
    public IEnumerable<EntityCandidate> Scan(
        IList<ScannerInput> blocks,
        TocCategoryMap toc,
        EntityNameMatcher? matcher = null,
        bool recoverFromRoster = false,
        bool ungateOnTocFailure = false,
        string? bookSourceKey = null)
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

        // TOC-failure detection: when ungating is permitted (non-official book with stat blocks) and
        // NOT ONE section maps to an entity type, the book's TOC categorization has failed wholesale.
        // In that case we must not let the (broken) TOC gate suppress its sections — emit them with a
        // broad prior and rely on the downstream decline gate to filter non-entities.
        var tocFailed = ungateOnTocFailure && groups.TrueForAll(
            g => MapCategoryToEntityType(toc.GetCategory(g.Page) ?? ContentCategory.Unknown) is null);

        foreach (var s in groups.OrderBy(s => s.FirstIndex))
        {
            // TocCategoryMap is page-keyed: look up the category by the section's earliest page.
            var category = toc.GetCategory(s.Page) ?? ContentCategory.Unknown;
            var type = MapCategoryToEntityType(category);
            if (type is null)
            {
                // 5etools-roster recovery (official books): a section the TOC gate would drop but
                // whose heading confidently matches ANY indexed 5etools entity is recovered, typed by
                // the MATCHED ENTRY rather than a hardcoded Monster. Matching prefers the book's own
                // source, so EGW's "Orc" page resolves to EGW's Race entry instead of another book's
                // bestiary Orc — the defect that shipped EGW's race pages as Monsters.
                // Recovery only ADDS — extraction + the decline gate still adjudicate the final
                // entity, so it cannot lower precision, and its cost is bounded by roster matches
                // rather than by the number of skipped sections.
                if (recoverFromRoster && matcher is not null &&
                    matcher.MatchPreferSource(s.Section, bookSourceKey) is { } recovered)
                {
                    // Matched type leads the prior, followed by the FULL ranked prior for that type
                    // (its empirical confusion set, then the frequency floor) so the model can still
                    // re-type when the match was misleading. Keeping the confusion set matters for
                    // reprints: EGW's Sea Elf / Aarakocra / Kenku / Tortle pages are races that 5etools
                    // attributes to other sources, so same-source preference finds nothing and the
                    // global fallback returns the bestiary Monster — Monster's confusion set carries
                    // Race, which is the model's route to the right type.
                    var recoveredPrior = new List<EntityType> { recovered.Type };
                    foreach (var t in ExpandToEntityPrior(MapEntityTypeToCategory(recovered.Type)))
                        if (!recoveredPrior.Contains(t)) recoveredPrior.Add(t);

                    logger.LogInformation(
                        "Recovered {Type} candidate '{Canonical}' from TOC-skipped section on page {Page}",
                        recovered.Type, recovered.Canonical, s.Page);
                    yield return new EntityCandidate(recovered.Type, recovered.Canonical, s.Text, s.Page, recoveredPrior);
                    continue;
                }

                // TOC-failure ungate (non-official fallback): categorization failed wholesale, so
                // emit the section with a broad prior instead of dropping it. The decline gate filters.
                if (tocFailed)
                {
                    var broadPrior = ExpandToEntityPrior(ContentCategory.Unknown);
                    yield return new EntityCandidate(broadPrior[0], s.Section, s.Text, s.Page, broadPrior);
                    continue;
                }

                // Traceability guard: log skipped sections so silent candidate losses are visible.
                logger.LogWarning(
                    "Skipping section '{Section}' on page {Page}: no entity category maps to this page (toc category: {Category})",
                    s.Section, s.Page, category);
                continue;
            }

            // Content-first prior: the primary keyword type plus its confusion set and the
            // frequency floor, mapped to entity types. Offered to the model as union branches so
            // it can re-type away from a wrong keyword guess (e.g. Dragonborn -> Race, not Monster).
            var prior = ExpandToEntityPrior(category);

            yield return new EntityCandidate(type.Value, s.Section, s.Text, s.Page, prior);
        }
    }

    // Expands a content category into its ranked entity-type prior (primary + confusion set +
    // frequency floor), dropping categories that are not entity types.
    private static List<EntityType> ExpandToEntityPrior(ContentCategory category) =>
        HeadingCategoryClassifier.ExpandPrior(category)
            .Select(MapCategoryToEntityType)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .Distinct()
            .ToList();

    // Inverse of MapCategoryToEntityType, so a roster-matched TYPE can be expanded into the same
    // ranked prior (confusion set + frequency floor) a keyword-derived CATEGORY gets. Types with no
    // category of their own fall back to Unknown, which still yields the frequency floor.
    private static ContentCategory MapEntityTypeToCategory(EntityType type) => type switch
    {
        EntityType.Spell => ContentCategory.Spell,
        EntityType.Monster => ContentCategory.Monster,
        EntityType.Class or EntityType.Subclass => ContentCategory.Class,
        EntityType.Race or EntityType.Subrace => ContentCategory.Race,
        EntityType.Background => ContentCategory.Background,
        EntityType.Item or EntityType.Weapon or EntityType.Armor => ContentCategory.Item,
        EntityType.MagicItem => ContentCategory.Treasure,
        EntityType.Condition => ContentCategory.Condition,
        EntityType.God => ContentCategory.God,
        EntityType.Plane => ContentCategory.Plane,
        EntityType.Trap => ContentCategory.Trap,
        _ => ContentCategory.Unknown,
    };

    private static EntityType? MapCategoryToEntityType(ContentCategory category) => category switch
    {
        ContentCategory.Spell => EntityType.Spell,
        ContentCategory.Monster => EntityType.Monster,
        ContentCategory.Class => EntityType.Class,
        ContentCategory.Race => EntityType.Race,
        ContentCategory.Background => EntityType.Background,
        ContentCategory.Item => EntityType.Item,
        ContentCategory.Condition => EntityType.Condition,
        ContentCategory.God => EntityType.God,
        ContentCategory.Plane => EntityType.Plane,
        ContentCategory.Treasure => EntityType.MagicItem,
        ContentCategory.Trap => EntityType.Trap,
        // Rule, Combat, Adventuring, Encounter, Trait, Lore, Unknown -> not entities (skipped).
        _ => null,
    };
}