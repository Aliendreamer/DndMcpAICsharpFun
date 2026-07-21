using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;
using Xunit.Abstractions;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

// extraction-content-classification Phase 1 (Rule), Task 2: a cheap deterministic-path harness —
// NO GPU/LLM — that proves, on REAL corpus candidates, that the Rule rescue (Task 1) targets ONLY
// the decline pile and NEVER a real entity. This is the anti-flooding evidence for the gate.
//
// Book used: the DMG (rules-heavy, so its decline pile is non-trivial). The conversion-cache file
// was identified by content, not filename, since cache files are named by content hash: it is the
// only cached file whose text contains DMG-only chapter titles ("Between Adventures", "Creating
// Nonplayer Characters", "Dungeon Master's Workshop", "Trinkets" all present with real page text).
public sealed class RuleRescueHarnessTests
{
    private const string DmgConversionCacheFile =
        "books/conversion-cache/fb470671dc5def35bf09897c11d4971161c70b3c5558cd792dc164cef2668f44.mineru.json";

    // Real 5etools index — the same matcher the production extraction pipeline uses.
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    // Mirrors PdfConversionDiskCache.CacheJsonOptions so the cache file's camelCase JSON
    // ("markdown"/"items") deserializes correctly.
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITestOutputHelper _output;

    public RuleRescueHarnessTests(ITestOutputHelper output) => _output = output;

    // Loads the real DMG candidate list the same way EntityCandidateBuilderRecoveryTests does:
    // deserialize the cached PdfStructureDocument, then run it through the real builder pipeline
    // (real scanner + real stat-block scanner + real 5etools matcher). The DMG's actual PDF file
    // is not present in the repo, so bookmarks cannot be read from it; returning an empty bookmark
    // list makes EntityCandidateBuilder fall back to the same heading-derived TOC the production
    // pipeline already uses for any book without embedded bookmarks (EntityCandidateBuilder.cs,
    // step 2b) — not a test-only shortcut.
    private static List<EntityCandidate> LoadRealDmgCandidates()
    {
        var cachePath = TestPaths.RepoFile(DmgConversionCacheFile);
        using var stream = File.OpenRead(cachePath);
        var doc = JsonSerializer.Deserialize<PdfStructureDocument>(stream, CacheJsonOptions);
        doc.Should().NotBeNull("the DMG conversion-cache file must deserialize into a PdfStructureDocument");

        var bookmarks = Substitute.For<IPdfBookmarkReader>();
        bookmarks.ReadBookmarks(Arg.Any<string>()).Returns(new List<PdfBookmark>());

        var builder = new EntityCandidateBuilder(
            bookmarks: bookmarks,
            scanner: new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance),
            statBlockScanner: new StatBlockScanner(),
            logger: NullLogger<EntityCandidateBuilder>.Instance,
            matcher: Matcher);

        var record = new IngestionRecord
        {
            FilePath = "dmg14.pdf",
            DisplayName = "Dungeon Master's Guide",
            FivetoolsSourceKey = "DMG",
        };

        return builder.Build(doc!, record, bookId: 1);
    }

    [Fact]
    public void Rule_rescue_targets_only_the_decline_pile_on_real_DMG_candidates()
    {
        var candidates = LoadRealDmgCandidates();
        candidates.Should().NotBeEmpty(
            "the real DMG conversion cache must yield candidates for this harness to mean anything");

        var declines = new List<EntityCandidate>();
        var rescuedAsRule = new List<EntityCandidate>();
        var forced = new List<EntityCandidate>();

        foreach (var candidate in candidates)
        {
            var outcome = DeterministicTypeResolver.Resolve(candidate, Matcher, isOfficial: true).Outcome;

            if (outcome == DeterministicOutcome.Decline)
            {
                declines.Add(candidate);
                if (ExtractionSignatures.RuleSignature(candidate))
                    rescuedAsRule.Add(candidate);
            }
            else if (outcome == DeterministicOutcome.ForceType)
            {
                forced.Add(candidate);

                // THE core anti-flooding property: a real entity (ForceType — matched a genuine
                // 5etools name) must NEVER be rescued as Rule. RescueAsRuleOrNull only fires when
                // the resolver's outcome is Decline, so this must hold for every ForceType
                // candidate in the real corpus, not just in synthetic unit tests.
                EntityExtractionOrchestrator.RescueAsRuleOrNull(candidate, outcome).Should().BeNull(
                    $"'{candidate.DisplayName}' resolved to ForceType (a real entity) and must never be rescued as Rule");
            }
        }

        // The anti-rescue check above is only meaningful if there are real ForceType entities to
        // check against — if `forced` were ever empty (e.g. a resolver regression), the loop
        // would silently pass without exercising the property at all.
        forced.Should().NotBeEmpty(
            "real ForceType entities must exist for the anti-rescue check to be non-vacuous");

        var forcedWithRuleSignature = forced.Count(c => ExtractionSignatures.RuleSignature(c));
        _output.WriteLine($"Total candidates: {candidates.Count}");
        _output.WriteLine($"Declines: {declines.Count}");
        _output.WriteLine($"Rescued-as-Rule (of declines): {rescuedAsRule.Count}");
        _output.WriteLine($"ForceType (real entities): {forced.Count}");
        _output.WriteLine($"ForceType with Rule signature (would-be-rescued if the outcome==Decline gate were removed): {forcedWithRuleSignature}");

        // The rescue pile must be non-empty — rules ARE being recovered, not just theoretically
        // reachable. If this were zero, the harness would have picked a book with no declined
        // rules and would prove nothing.
        rescuedAsRule.Should().NotBeEmpty(
            "DMG is rules-heavy; the decline pile must contain at least one substantial-prose " +
            "rule candidate that gets rescued, or the anti-flooding claim is untested");
    }

    // extraction-cross-type-recovery Task 2: a companion deterministic-path harness proving the
    // Item rescue (Task 1's RescueAsItemOrNull) is dormant-but-safe on the same real DMG decline
    // pile. It is NOT exactly dormant (~0) on this corpus — DAERN's Instant Fortress, a magic rod,
    // a legendary sword, dynamite, and grenades are genuine mundane/magic items that were mis-typed
    // upstream (their PRIMARY prior type failed a 5etools match) and get correctly recovered here.
    // The harness therefore asserts a SMALL, human-eyeballable bounded count (logged in full) rather
    // than a blanket "never touches a rule" claim — RuleSignature is a bare >=200-char prose-length
    // floor, not a rule classifier, so it cannot soundly prove disjointness against ItemSignature.
    [Fact]
    public void Item_rescue_is_dormant_but_safe_on_real_DMG_declines()
    {
        var candidates = LoadRealDmgCandidates();
        candidates.Should().NotBeEmpty(
            "the real DMG conversion cache must yield candidates for this harness to mean anything");

        var declines = new List<EntityCandidate>();
        var itemSignatureDeclines = new List<EntityCandidate>();
        var ruleSignatureDeclines = new List<EntityCandidate>();

        foreach (var candidate in candidates)
        {
            var outcome = DeterministicTypeResolver.Resolve(candidate, Matcher, isOfficial: true).Outcome;
            if (outcome != DeterministicOutcome.Decline)
                continue;

            declines.Add(candidate);
            if (ExtractionSignatures.ItemSignature(candidate))
                itemSignatureDeclines.Add(candidate);
            if (ExtractionSignatures.RuleSignature(candidate))
                ruleSignatureDeclines.Add(candidate);
        }

        _output.WriteLine($"Total declines: {declines.Count}");
        _output.WriteLine($"Item-signature declines: {itemSignatureDeclines.Count}");
        _output.WriteLine($"Rule-signature declines: {ruleSignatureDeclines.Count}");
        foreach (var c in itemSignatureDeclines)
            _output.WriteLine($"  item-signature decline: '{c.DisplayName}'");

        // The specific known rule decline, if present in this build of the candidate list, must
        // NEVER carry the Item signature — RescueAsItemOrNull is checked before RescueAsRuleOrNull,
        // so a false Item grab here would silently steal a real rule before the Rule rescue ever
        // sees it. (Not present in the DMG build of this candidate list — see report — so this
        // branch documents intent for whichever book does surface it.)
        var switchingWeapons = declines.FirstOrDefault(c =>
            c.DisplayName.Contains("switching weapons", StringComparison.OrdinalIgnoreCase)
            || c.DisplayName.Contains("switching-weapons", StringComparison.OrdinalIgnoreCase));
        if (switchingWeapons is not null)
        {
            ExtractionSignatures.ItemSignature(switchingWeapons).Should().BeFalse(
                "'switching weapons' is a rule, not a mundane item, and must not be grabbed by the Item rescue");
        }

        // Fallback/general property requested by the task brief when 'switching-weapons' isn't in
        // the built candidate list: the Item rescue must stay small enough on real declines that
        // every hit can be eyeballed by hand (logged above), not silently flood the decline pile.
        itemSignatureDeclines.Count.Should().BeLessOrEqualTo(10,
            "the Item rescue is expected to be dormant-but-safe on this corpus — a small, " +
            "human-eyeballable count, not a flood of false item grabs");
    }
}