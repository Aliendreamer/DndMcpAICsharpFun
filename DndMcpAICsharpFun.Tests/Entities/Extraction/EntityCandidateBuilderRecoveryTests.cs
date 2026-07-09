using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Tests;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

/// <summary>
/// End-to-end candidate-generation locks for the mm-monster-recall change:
/// the stat-block path is authoritative regardless of TOC category (1.1), and a non-official
/// book whose TOC categorization failed is ungated (1.2).
/// </summary>
public sealed class EntityCandidateBuilderRecoveryTests
{
    // Real 5etools index so DeterministicTypeResolver keeps genuine monster names.
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    private static PdfStructureItem H(string text, int page = 1) => new("section_header", text, page, 1);
    private static PdfStructureItem T(string text, int page = 1) => new("text", text, page, null);

    private static IPdfBookmarkReader RulePageBookmarks()
    {
        // "Introduction" matches no category keyword → ContentCategory.Rule for page 1+.
        var reader = Substitute.For<IPdfBookmarkReader>();
        reader.ReadBookmarks(Arg.Any<string>()).Returns(new List<PdfBookmark> { new("Introduction", 1) });
        return reader;
    }

    private static EntityCandidateBuilder BuildBuilder(IPdfBookmarkReader bookmarks) =>
        new(
            bookmarks: bookmarks,
            scanner: new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance),
            statBlockScanner: new StatBlockScanner(),
            logger: NullLogger<EntityCandidateBuilder>.Instance,
            matcher: Matcher);

    [Fact]
    public void StatBlock_on_rule_page_still_yields_a_monster_candidate()
    {
        // Task 1.1: a detected stat block is authoritative even when its page's TOC category is Rule.
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            H("GIANT FROG"),
            T("Medium beast, unaligned"),
            T("Armor Class 11  Hit Points 18 (4d8)  Speed 30ft."),
            T("STR DEX CON 12 13 11"),
        });

        var record = new IngestionRecord
        {
            FilePath = "bestiary.pdf",
            DisplayName = "Homebrew Bestiary",
            // No FivetoolsSourceKey → non-official (isolates the stat-block path from 5etools recovery).
        };

        var candidates = BuildBuilder(RulePageBookmarks()).Build(doc, record, bookId: 1);

        candidates.Should().Contain(c => c.Type == EntityType.Monster && c.DisplayName == "GIANT FROG",
            "a stat block on a Rule page must not be suppressed by the TOC gate");
    }

    [Fact]
    public void Non_official_book_with_stat_blocks_ungates_a_rule_page_lore_section()
    {
        // Task 1.2 wiring: the book yields a stat block but its TOC maps every page to Rule, so
        // categorization has failed. A separate monster-named lore section (no stat block of its own)
        // must still be emitted via the ungate rather than dropped by the broken TOC gate.
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            H("GOBLIN"),
            T("Goblins are small, black-hearted humanoids that lair in caves."),
            H("GIANT FROG"),
            T("Medium beast, unaligned"),
            T("Armor Class 11  Hit Points 18 (4d8)  Speed 30ft."),
        });

        var record = new IngestionRecord
        {
            FilePath = "bestiary.pdf",
            DisplayName = "Homebrew Bestiary",
        };

        var candidates = BuildBuilder(RulePageBookmarks()).Build(doc, record, bookId: 2);

        candidates.Should().Contain(c => c.DisplayName == "GOBLIN",
            "the TOC categorization failed wholesale, so the ungate must keep the lore-only monster section");
    }
}