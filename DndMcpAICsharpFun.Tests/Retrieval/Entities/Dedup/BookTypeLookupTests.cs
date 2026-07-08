using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class BookTypeLookupTests
{
    [Fact]
    public void Maps_fivetools_key_and_display_name_to_booktype()
    {
        var records = new[]
        {
            new IngestionRecord { Id = 1, DisplayName = "Player's Handbook 2014",
                FivetoolsSourceKey = "PHB", BookType = BookType.Core },
            new IngestionRecord { Id = 2, DisplayName = "Homebrew Tome",
                FivetoolsSourceKey = null, BookType = BookType.Supplement },
        };

        var map = BookTypeLookup.Build(records);

        map["PHB"].Should().Be(BookType.Core);                  // fivetools key
        map.Should().ContainKey("Homebrew Tome");               // raw display-name (what non-official entities actually carry in SourceBook)
        map["Homebrew Tome"].Should().Be(BookType.Supplement);
    }

    [Fact]
    public void Winner_prefers_higher_authority_booktype_when_both_are_keyed_by_raw_display_name()
    {
        // Regression test: non-official entities carry the raw DisplayName (not a slug) in
        // EntityEnvelope.SourceBook (see EntityExtractionOrchestrator merge logic). The map
        // must be keyed by that same raw DisplayName for BookAuthority tier-1 comparisons to work.
        // Both books here are non-official (no FivetoolsSourceKey), so this exercises the exact
        // path the bug broke: before the fix, both resolve to BookType.Unknown and the winner
        // falls through to the CanonicalText-length tiebreak (picking the Adventure entity,
        // which is wrong); after the fix, the Supplement entity correctly outranks the Adventure
        // entity on tier-1 authority regardless of text length.
        var records = new[]
        {
            new IngestionRecord { Id = 1, DisplayName = "Homebrew Tome",
                FivetoolsSourceKey = null, BookType = BookType.Supplement },
            new IngestionRecord { Id = 2, DisplayName = "Forgotten Homebrew Adventure",
                FivetoolsSourceKey = null, BookType = BookType.Adventure },
        };
        var map = BookTypeLookup.Build(records);

        var supplementEntity = TestEnvelopes.Make(
            id: "supplement-1", name: "Ancient Relic", type: EntityType.Item, edition: "2014",
            sourceBook: "Homebrew Tome", canonicalText: "short text");
        var adventureEntity = TestEnvelopes.Make(
            id: "adventure-1", name: "Ancient Relic", type: EntityType.Item, edition: "2014",
            sourceBook: "Forgotten Homebrew Adventure",
            canonicalText: "a much longer canonical text that would win any length-based tiebreak");

        var winner = DuplicateResolver.Winner([supplementEntity, adventureEntity], map);

        winner.Id.Should().Be("supplement-1");
    }
}
