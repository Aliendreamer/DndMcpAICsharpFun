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
/// End-to-end locks for extraction-statline-name-repair. The ordering constraint (strip BEFORE match)
/// is invisible from either call site alone: DeterministicTypeResolver never drops a name that matched
/// a 5etools entry, so repairing the name before the match is what saves a real magic item from being
/// dropped by the name gate.
/// </summary>
public sealed class StatLineNameRepairTests
{
    // Real 5etools index — the point is that a REPAIRED name grounds against real data.
    private static readonly EntityNameMatcher Matcher =
        new(new EntityNameIndex(TestPaths.RepoFile("5etools")));

    private static PdfStructureItem H(string text, int page = 1) => new("section_header", text, page, 1);
    private static PdfStructureItem T(string text, int page = 1) => new("text", text, page, null);

    private static IPdfBookmarkReader RulePageBookmarks()
    {
        var reader = Substitute.For<IPdfBookmarkReader>();
        reader.ReadBookmarks(Arg.Any<string>()).Returns(new List<PdfBookmark> { new("Magic Items", 1) });
        return reader;
    }

    private static EntityCandidateBuilder BuildBuilder() =>
        new(
            bookmarks: RulePageBookmarks(),
            scanner: new EntityCandidateScanner(NullLogger<EntityCandidateScanner>.Instance),
            statBlockScanner: new StatBlockScanner(),
            logger: NullLogger<EntityCandidateBuilder>.Instance,
            matcher: Matcher);

    [Fact]
    public void Matcher_grounds_a_concatenated_magic_item_heading()
    {
        // "Sun Blade" is a real 5etools magic item. Raw, the concatenated heading normalises to a key
        // that exists in no index; stripped, it must resolve to the real entry.
        Matcher.Match("Sun Blade Weapon (longsword), rare (requires attunement)")
            .Should().NotBeNull()
            .And.Subject.As<(string Canonical, EntityType Type)?>()!.Value
            .Canonical.Should().Be("Sun Blade");
    }

    [Fact]
    public void Concatenated_item_heading_is_repaired_and_kept()
    {
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            H("Sun Blade Weapon (longsword), rare (requires attunement)"),
            T("This item appears to be a longsword hilt. While grasping the hilt, you can use a bonus "
              + "action to cause a blade of pure radiance to spring into existence, or make the blade "
              + "disappear. While the blade exists, this magic longsword has the finesse property."),
        });

        var record = new IngestionRecord
        {
            FilePath = "items.pdf",
            DisplayName = "Dungeon Master's Guide 2014",
            FivetoolsSourceKey = "DMG",
        };

        var candidates = BuildBuilder().Build(doc, record, bookId: 900);

        candidates.Should().Contain(c => c.DisplayName == "Sun Blade",
            "a real item whose heading collapsed with its stat line must be repaired, not dropped");
        candidates.Should().NotContain(c => c.DisplayName.Contains("Weapon (longsword)"),
            "no candidate may keep the stat line in its name");
    }

    [Fact]
    public void A_name_that_is_only_a_statline_is_dropped()
    {
        var doc = new PdfStructureDocument("md", new List<PdfStructureItem>
        {
            H("Wondrous item, very rare"),
            T("This wondrous item requires attunement by a spellcaster. While wearing it you gain a "
              + "+1 bonus to spell attack rolls and the save DC of your spells, and you can cast the "
              + "detect magic spell at will without expending a spell slot."),
        });

        var record = new IngestionRecord
        {
            FilePath = "items.pdf",
            DisplayName = "Dungeon Master's Guide 2014",
            FivetoolsSourceKey = "DMG",
        };

        var candidates = BuildBuilder().Build(doc, record, bookId: 901);

        candidates.Should().NotContain(c => c.DisplayName == "Wondrous item, very rare",
            "a name that is entirely a magic-item stat line is not an entity, whatever its text says");
    }
}