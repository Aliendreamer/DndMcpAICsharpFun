using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityCandidateScannerTests
{
    [Fact]
    public void Scanner_groups_by_section_and_maps_category_to_entity_type()
    {
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Lore", ContentCategory.Lore, StartPage: 1, EndPage: 30),
            new TocSectionEntry("Monsters", ContentCategory.Monster, StartPage: 31, EndPage: 200),
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 201, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("Bullywug", 35, "Bullywug stat block text"),
            new("Bullywug", 36, "Bullywug actions"),
            new("Fireball", 241, "Fireball spell text"),
            new("History of the realms", 12, "Long lore prose"),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().HaveCount(2);
        candidates.Should().ContainSingle(c => c.Type == EntityType.Monster && c.DisplayName == "Bullywug")
            .Which.Text.Should().Contain("Bullywug stat block text").And.Contain("Bullywug actions");
        candidates.Should().ContainSingle(c => c.Type == EntityType.Spell && c.DisplayName == "Fireball");
        candidates.Should().NotContain(c => c.DisplayName == "History of the realms");
    }

    [Fact]
    public void Lore_section_is_skipped()
    {
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Lore", ContentCategory.Lore, StartPage: 1, EndPage: 50),
        });

        var blocks = new List<ScannerInput>
        {
            new("History of the realms", 12, "Long lore prose"),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().BeEmpty();
    }

    [Fact]
    public void Scanner_splits_same_title_when_different_title_intervenes_across_chapters()
    {
        // "DARKVISION" appears twice in the Spell section with an unrelated spell between them.
        // The intervening title break must force two separate candidates, not one merged at p184.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("DARKVISION", 184, "The invocation text"),
            new("FIREBALL",   190, "Fireball spell text"),
            new("DARKVISION", 230, "The spell text"),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc).ToList();

        var darkvision = candidates.Where(c => c.DisplayName == "DARKVISION").ToList();
        darkvision.Should().HaveCount(2, "each chapter occurrence is a distinct candidate");
        darkvision.Should().ContainSingle(c => c.Page == 184);
        darkvision.Should().ContainSingle(c => c.Page == 230);
    }

    [Fact]
    public void Scanner_merges_same_title_on_adjacent_pages_within_gap()
    {
        // Two blocks for the same section on consecutive pages must merge into one candidate.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 1, EndPage: 100),
        });

        var blocks = new List<ScannerInput>
        {
            new("FIREBALL", 50, "Fireball part 1"),
            new("FIREBALL", 51, "Fireball part 2"),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().HaveCount(1);
        candidates[0].DisplayName.Should().Be("FIREBALL");
        candidates[0].Page.Should().Be(50);
        candidates[0].Text.Should().Contain("Fireball part 1").And.Contain("Fireball part 2");
    }

    [Fact]
    public void Scanner_splits_same_title_when_page_gap_exceeds_threshold()
    {
        // Same title, no intervening title, but page jump > MaxPageGap (3) → two candidates.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("DARKVISION", 50, "First occurrence text"),
            new("DARKVISION", 60, "Second occurrence text — gap of 10 pages"),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().HaveCount(2, "gap > MaxPageGap(3) with no intervening title still splits");
        candidates.Should().ContainSingle(c => c.Page == 50);
        candidates.Should().ContainSingle(c => c.Page == 60);
    }

    [Fact]
    public void Scanner_preserves_document_order_for_distinct_sections()
    {
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("SPELL_A", 10, "Text A"),
            new("SPELL_B", 20, "Text B"),
            new("SPELL_C", 30, "Text C"),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().HaveCount(3);
        candidates.Select(c => c.DisplayName).Should().Equal("SPELL_A", "SPELL_B", "SPELL_C");
    }

    [Fact]
    public void Scanner_logs_warning_when_section_page_has_no_entity_category()
    {
        // Traceability guard: a section on a page not covered by the TOC (null/Unknown category)
        // is skipped. After Fix #2b the scanner must emit a LogWarning so the loss is visible.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 1, EndPage: 100),
        });

        var blocks = new List<ScannerInput>
        {
            new("ORPHAN_SECTION", 200, "some body text"), // page 200 has no TOC entry → Unknown
        };

        var capturingLogger = new CapturingLogger<EntityCandidateScanner>();
        var scanner = new EntityCandidateScanner(capturingLogger);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().BeEmpty("the section has no entity category and must be skipped");
        capturingLogger.Logs.Should().ContainSingle(
            l => l.Level == LogLevel.Warning && l.Message.Contains("ORPHAN_SECTION"),
            "a warning must be logged naming the dropped section");
    }

    [Fact]
    public void Scanner_does_not_log_warning_for_sections_with_valid_category()
    {
        // Sanity guard: a normal section with a known category must NOT trigger the warning.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 1, EndPage: 100),
        });

        var blocks = new List<ScannerInput>
        {
            new("FIREBALL", 50, "A bright streak flashes from your pointing finger..."),
        };

        var capturingLogger = new CapturingLogger<EntityCandidateScanner>();
        var scanner = new EntityCandidateScanner(capturingLogger);
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().ContainSingle();
        capturingLogger.Logs.Should().BeEmpty("no warning should be emitted for a correctly categorised section");
    }
}
