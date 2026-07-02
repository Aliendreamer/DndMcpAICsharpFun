using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Builds the ordered <see cref="EntityCandidate"/> list from a converted PDF document:
/// TOC classification (embedded bookmarks or a heading-derived fallback), section + stat-block
/// scanning, id-keyed dedup, and the deterministic non-entity prefilter. Pure candidate
/// production — no LLM calls, no output writing. Shared by every extraction run mode.
/// </summary>
public sealed class EntityCandidateBuilder(
    IPdfBookmarkReader bookmarks,
    EntityCandidateScanner scanner,
    StatBlockScanner statBlockScanner,
    ILogger<EntityCandidateBuilder> logger,
    EntityNameMatcher? matcher = null)
{
    private readonly EntityNameMatcher? _matcher = matcher;

    public List<EntityCandidate> Build(
        PdfStructureDocument doc,
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        int bookId)
    {
        // 2. Read bookmarks → TocCategoryMap.
        var pdfBookmarks = bookmarks.ReadBookmarks(record.FilePath);
        var tocEntries   = BookmarkTocMapper.Map(pdfBookmarks);
        var tocMap       = new TocCategoryMap(tocEntries);

        // 2b. No embedded bookmarks → derive the TOC from MinerU's heading structure items,
        // reusing the same deterministic keyword classifier (no LLM). Bookmarked books skip this.
        if (tocMap.IsEmpty)
        {
            var headingItems = doc.Items
                .Where(i => string.Equals(i.Type, "section_header", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var headingEntries = HeadingTocMapper.Map(headingItems);
            tocMap = new TocCategoryMap(headingEntries);
            logger.LogInformation(
                "No bookmarks for book {BookId}; derived TOC from {HeadingCount} headings → {EntryCount} confident category entries (heading-derived fallback)",
                bookId, headingItems.Count, headingEntries.Count);
        }

        // 3. Project structure items into ScannerInputs.
        var scannerInputs = BuildScannerInputs(doc.Items);
        var sectionCandidates = scanner.Scan(scannerInputs, tocMap).ToList();
        // Recover stat blocks MinerU failed to tag with a heading (headerless / fragmented under
        // mis-detected ACTIONS headers). Prepend so they win the id-keyed dedup with clean stat-block
        // text and supersede a headerless monster's lore-only section candidate.
        var statBlockCandidates = statBlockScanner.Scan(doc.Items).ToList();
        // Collapse same-id candidates (a header-clean monster yields both a section and a
        // stat-block candidate) to the best input: prefer the one carrying a stat block, then
        // the richer text — so header-clean monsters extract from full-context section text
        // (reliable) and headerless ones keep their stat-block candidate.
        var candidates    = ExtractionCandidateDeduplicator.Dedupe(
            statBlockCandidates.Concat(sectionCandidates), ExtractionEntityIds.BookKey(record));

        // Deterministic resolution drops non-entity-named candidates (headings/fragments) before
        // extraction — no wasted LLM call, no garbage entity. INVARIANT: this prefilter omits
        // isOfficial on purpose — it removes ONLY Drop. Declines must survive to the recording loop
        // in the orchestrator (which passes isOfficial) so they reach declined.json; passing
        // isOfficial here would silently filter them out. Do not "fix" it to pass isOfficial.
        var keptCandidates = candidates
            .Where(c => DeterministicTypeResolver.Resolve(c, _matcher).Outcome != DeterministicOutcome.Drop)
            .ToList();
        var droppedCount = candidates.Count - keptCandidates.Count;
        if (droppedCount > 0)
            logger.LogInformation("Dropped {Count} non-entity-named candidates before extraction", droppedCount);
        candidates = keptCandidates;

        logger.LogInformation(
            "Entity extraction: {CandidateCount} candidates from {ItemCount} structure items",
            candidates.Count, doc.Items.Count);

        return candidates;
    }

    private IList<ScannerInput> BuildScannerInputs(IReadOnlyList<PdfStructureItem> items)
    {
        var inputs = new List<ScannerInput>(items.Count);
        var currentSection = "(unknown)";
        var hadBody = false; // tracks whether currentSection has received any body text

        foreach (var item in items)
        {
            var type = item.Type ?? string.Empty;
            if (type.StartsWith("section", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("heading", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                {
                    var nextSection = item.Text.Trim();
                    // Traceability guard: a heading immediately following another heading with no
                    // body text between them silently drops the prior section as a candidate.
                    // Log a warning so the loss is visible in the extraction run output.
                    if (!hadBody && currentSection != "(unknown)")
                        logger.LogWarning(
                            "Section '{Prev}' received no body before heading '{Next}' — it will not become a candidate",
                            currentSection, nextSection);

                    currentSection = nextSection;
                    hadBody = false;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Text)) continue;
            hadBody = true;
            inputs.Add(new ScannerInput(currentSection, item.PageNumber, item.Text));
        }
        return inputs;
    }
}
