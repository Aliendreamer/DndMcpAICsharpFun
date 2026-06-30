using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class HeadingFallbackScannerTests
{
    [Fact]
    public void HeadingDerivedToc_ProducesTypedCandidates_ForBookmarklessInput()
    {
        // Headings drive the category map (as in a bookmark-less PDF like the SRD).
        var headings = new[] { new PdfStructureItem("section_header", "Barbarian", 1, 1) };
        var tocMap = new TocCategoryMap(HeadingTocMapper.Map(headings));

        // Content sits under the "Barbarian" section on page 1.
        var inputs = new[] { new ScannerInput("Barbarian", 1, "The barbarian can enter a rage as a bonus action.") };

        var candidates = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance).Scan(inputs, tocMap).ToList();

        candidates.Should().ContainSingle(c => c.Type == EntityType.Class);
    }

    [Fact]
    public void BookmarkDerivedToc_IsNonEmpty_SoFallbackBranchIsSkipped()
    {
        // Guards the orchestrator guard: with bookmarks present the TocCategoryMap is non-empty,
        // so `if (tocMap.IsEmpty)` is false and HeadingTocMapper is never invoked.
        var tocMap = new TocCategoryMap(BookmarkTocMapper.Map(new[] { new PdfBookmark("Spells", 100) }));
        tocMap.IsEmpty.Should().BeFalse();
    }
}
