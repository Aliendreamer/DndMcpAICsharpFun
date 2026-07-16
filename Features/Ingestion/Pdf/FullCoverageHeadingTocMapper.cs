using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Builds a FULL-COVERAGE table of contents from MinerU section-header items for the
/// block-ingestion fallback: every heading becomes a titled section boundary, categories
/// carry forward from the last typed heading, and catch-all entries guarantee that — once
/// assembled into a TocCategoryMap — every page from 1 onward resolves to a section (no
/// block is dropped).
///
/// Deliberately distinct from <see cref="HeadingTocMapper"/>, which is sparse/confident-only
/// for entity extraction (dropped headings there are correct; here they would lose content).
/// A heading "anchors" the carry-forward category when the classifier gives any typed result
/// (i.e. not the <see cref="ContentCategory.Rule"/> fallback); unlike the extraction path this
/// keeps the broader block-useful categories (Combat, Lore, Adventuring, Encounter, Trait).
/// </summary>
public static class FullCoverageHeadingTocMapper
{
    public static IReadOnlyList<TocSectionEntry> Map(
        IReadOnlyList<PdfStructureItem> headings, string bookTitle)
    {
        ArgumentNullException.ThrowIfNull(headings);

        var named = headings.Where(h => !string.IsNullOrWhiteSpace(h.Text)).ToList();
        if (named.Count == 0)
            return new[] { new TocSectionEntry(bookTitle, ContentCategory.Rule, 1) };

        var entries = new List<TocSectionEntry>();

        // Front-matter catch-all so pages before the first heading are never dropped.
        if (named[0].PageNumber > 1)
            entries.Add(new TocSectionEntry("Front Matter", ContentCategory.Rule, 1));

        ContentCategory? lastTyped = null;
        foreach (var h in named)
        {
            var guessed = HeadingCategoryClassifier.Guess(h.Text);
            ContentCategory category;
            if (guessed != ContentCategory.Rule)
            {
                category = guessed;   // a typed heading anchors the carry-forward
                lastTyped = guessed;
            }
            else
            {
                category = lastTyped ?? ContentCategory.Rule;  // inherit, else default
            }

            entries.Add(new TocSectionEntry(h.Text.Trim(), category, h.PageNumber));
        }

        return entries;
    }
}
