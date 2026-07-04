using System.Collections.Frozen;
using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public static class HeadingTocMapper
{
    // Categories that EntityCandidateScanner.MapCategoryToEntityType maps to a concrete EntityType.
    // Only these "confident" categories are emitted, so keyword-less sub-headings (which classify to
    // Rule) are dropped and do not reset the surrounding page-range category. Keep in sync with
    // EntityCandidateScanner.MapCategoryToEntityType.
    private static readonly FrozenSet<ContentCategory> Confident = new HashSet<ContentCategory>
    {
        ContentCategory.Spell, ContentCategory.Monster, ContentCategory.Class, ContentCategory.Race,
        ContentCategory.Background, ContentCategory.Item, ContentCategory.Condition, ContentCategory.God,
        ContentCategory.Plane, ContentCategory.Treasure, ContentCategory.Trap,
    }.ToFrozenSet();

    public static IReadOnlyList<TocSectionEntry> Map(IReadOnlyList<PdfStructureItem> headings)
    {
        ArgumentNullException.ThrowIfNull(headings);

        var entries = new List<TocSectionEntry>();
        foreach (var h in headings)
        {
            if (string.IsNullOrWhiteSpace(h.Text)) continue;
            var category = HeadingCategoryClassifier.Guess(h.Text);
            if (!Confident.Contains(category)) continue;
            entries.Add(new TocSectionEntry(h.Text.Trim(), category, h.PageNumber));
        }
        return entries;
    }
}
