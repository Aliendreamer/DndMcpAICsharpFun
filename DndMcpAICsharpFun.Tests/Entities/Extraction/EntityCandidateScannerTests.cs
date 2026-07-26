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

    // ── 5etools-roster monster candidate recovery (Task 1.3) ─────────────────────────

    // Shared real 5etools index (Aboleth/Beholder live in bestiary-mm.json).
    private static readonly EntityNameMatcher RosterMatcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    [Fact]
    public void Recovers_monster_candidate_from_rule_page_for_official_book()
    {
        // MM reality: every page classifies Rule, so the monster-name section would be dropped.
        // With recovery on + a matcher, an ABOLETH heading is recovered as a Monster candidate.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Legal", ContentCategory.Rule, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("ABOLETH", 12, "The aboleth is an aberration that dwells in the deep."),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc, RosterMatcher, recoverFromRoster: true).ToList();

        candidates.Should().ContainSingle()
            .Which.Should().Match<EntityCandidate>(c =>
                c.Type == EntityType.Monster && c.DisplayName == "Aboleth" && c.Page == 12);
    }

    // ── Generalized roster recovery (extraction-roster-recovery-generalization) ──────────────

    private static TocCategoryMap AllRulePages() => new(new[]
    {
        new TocSectionEntry("Rules", ContentCategory.Rule, StartPage: 1, EndPage: 400),
    });

    private static List<EntityCandidate> ScanForBook(string heading, string text, string sourceKey) =>
        new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance)
            .Scan(
                new List<ScannerInput> { new(heading, 184, text) },
                AllRulePages(),
                RosterMatcher,
                recoverFromRoster: true,
                bookSourceKey: sourceKey)
            .ToList();

    // EGW's whole character-options chapter classifies Rule, so these were dropped entirely:
    // 82 entities against a ~219 roster, with zero Subclass/Race/Subrace/Spell.
    [Fact]
    public void Recovers_a_subclass_candidate_from_a_rule_page()
    {
        var candidates = ScanForBook(
            "ECHO KNIGHT",
            "Echo Knights are warriors who wield the power of dunamis to summon an echo of themselves.",
            "EGW");

        candidates.Should().ContainSingle()
            .Which.Should().Match<EntityCandidate>(c =>
                c.Type == EntityType.Subclass && c.DisplayName == "Echo Knight");
    }

    [Fact]
    public void Recovers_a_subrace_candidate_from_a_rule_page()
    {
        var candidates = ScanForBook(
            "Pallid",
            "Pallid elves have lived so long underground that their skin has lost all colour.",
            "EGW");

        candidates.Should().ContainSingle()
            .Which.Type.Should().Be(EntityType.Subrace);
    }

    // The mis-typing this change fixes: EGW's Orc page is a RACE, but the global first-wins index
    // returns the bestiary Orc (bestiary-* loads before races.json), so it shipped as a Monster.
    [Fact]
    public void Recovered_candidate_prefers_the_books_own_source_over_another_books_type()
    {
        var candidates = ScanForBook(
            "Orc",
            "Orcs are massive humanoids with grey skin, jutting jaws and prominent teeth.",
            "EGW");

        candidates.Should().ContainSingle()
            .Which.Type.Should().Be(EntityType.Race, "EGW's own Orc entry is a Race, not a Monster");
    }

    // The recovered candidate must still offer alternatives so the model can re-type.
    [Fact]
    public void Recovered_candidate_leads_its_prior_with_the_matched_type()
    {
        var candidates = ScanForBook(
            "ECHO KNIGHT",
            "Echo Knights are warriors who wield the power of dunamis to summon an echo of themselves.",
            "EGW");

        var prior = candidates.Should().ContainSingle().Subject.TypePrior;
        prior[0].Should().Be(EntityType.Subclass);
        prior.Should().HaveCountGreaterThan(1, "the frequency floor must remain available for re-typing");
    }

    // A reprinted RACE page whose 5etools entry belongs to another source falls back to the global
    // bestiary Monster entry, so the model's only route to the right type is Monster's confusion set.
    // The live EGW run showed Sea Elf / Aarakocra / Kenku / Tortle taking exactly this path.
    [Fact]
    public void Recovered_candidate_prior_keeps_the_matched_types_confusion_set()
    {
        var candidates = ScanForBook(
            "Sea Elf",
            "Sea elves fell in love with the wild beauty of the ocean in the earliest days of the multiverse.",
            "EGW");

        var prior = candidates.Should().ContainSingle().Subject.TypePrior;
        prior[0].Should().Be(EntityType.Monster, "no EGW-sourced entry exists, so the global fallback applies");
        prior.Should().Contain(EntityType.Race,
            "Monster's confusion set carries Race — without it a reprinted race page can never re-type");
    }

    [Fact]
    public void Does_not_recover_rule_section_that_matches_no_monster()
    {
        // Precision guard: a Rule-category heading that is NOT a monster stays skipped.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Rules", ContentCategory.Rule, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("ABILITY SCORES", 8, "Six abilities describe every creature's physical and mental characteristics."),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc, RosterMatcher, recoverFromRoster: true).ToList();

        candidates.Should().BeEmpty("a non-monster Rule heading must not be recovered");
    }

    [Fact]
    public void Does_not_recover_monster_when_recovery_flag_is_off()
    {
        // Non-official books pass recoverFromRoster:false → the matcher is never consulted.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Legal", ContentCategory.Rule, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("ABOLETH", 12, "The aboleth is an aberration that dwells in the deep."),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc, RosterMatcher, recoverFromRoster: false).ToList();

        candidates.Should().BeEmpty("recovery is off for non-official books even when the matcher would match");
    }

    // ── TOC-failure ungate (Task 1.2) ────────────────────────────────────────────────

    [Fact]
    public void Ungates_sections_when_toc_categorization_failed_wholesale()
    {
        // Non-official bestiary whose TOC maps ZERO sections to an entity type: with ungating on,
        // sections are emitted (broad prior) instead of all being dropped by the broken TOC gate.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Everything", ContentCategory.Rule, StartPage: 1, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("GRUUL", 10, "Gruul stat block prose."),
            new("SNAPMAW", 20, "Snapmaw stat block prose."),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc, matcher: null,
            recoverFromRoster: false, ungateOnTocFailure: true).ToList();

        candidates.Should().HaveCount(2, "no section maps to an entity type, so the TOC gate is bypassed");
        candidates.Select(c => c.DisplayName).Should().Contain(new[] { "GRUUL", "SNAPMAW" });
        candidates.Should().OnlyContain(c => c.TypePrior.Contains(EntityType.Monster),
            "the broad prior offers Monster (plus the frequency floor) as a union branch");
    }

    [Fact]
    public void Does_not_ungate_when_some_section_maps_to_an_entity_type()
    {
        // Categorization did NOT fail (a Monster page exists) → the Rule section is still dropped.
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Intro", ContentCategory.Rule, StartPage: 1, EndPage: 5),
            new TocSectionEntry("Monsters", ContentCategory.Monster, StartPage: 6, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("FOREWORD", 2, "Some rules prose."),
            new("BULLYWUG", 40, "Bullywug stat block prose."),
        };

        var scanner = new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance);
        var candidates = scanner.Scan(blocks, toc, matcher: null,
            recoverFromRoster: false, ungateOnTocFailure: true).ToList();

        candidates.Should().ContainSingle().Which.DisplayName.Should().Be("BULLYWUG");
    }
}