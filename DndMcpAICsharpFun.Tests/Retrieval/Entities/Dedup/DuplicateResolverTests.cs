using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class DuplicateResolverTests
{
    private static readonly Dictionary<string, BookType> Books = new()
    {
        ["PHB"] = BookType.Core,
        ["XGE"] = BookType.Supplement,
        ["ADV"] = BookType.Adventure,
    };

    private static EntityEnvelope E(string id, string source = "PHB", string ds = "",
        bool needsReview = false, string text = "text") =>
        TestEnvelopes.Make(id, "Fireball", EntityType.Spell, "Edition2014",
            sourceBook: source, dataSource: ds, needsReview: needsReview, canonicalText: text);

    [Fact]
    public void Core_book_beats_supplement()
    {
        var core = E("a", "PHB");
        var supp = E("b", "XGE");
        DuplicateResolver.Winner([supp, core], Books).Should().Be(core);
    }

    [Fact]
    public void Authority_outranks_needs_review()
    {
        var coreFlagged = E("a", "PHB", needsReview: true);
        var suppClean = E("b", "XGE", needsReview: false);
        DuplicateResolver.Winner([suppClean, coreFlagged], Books).Should().Be(coreFlagged);
    }

    [Fact]
    public void Authoritative_datasource_beats_parsed_within_same_book()
    {
        var backfill = E("a", "PHB", ds: "5etools-backfill");
        var parsed = E("b", "PHB", ds: "");
        DuplicateResolver.Winner([parsed, backfill], Books).Should().Be(backfill);
    }

    [Fact]
    public void Not_needs_review_beats_needs_review_when_authority_ties()
    {
        var clean = E("a", "PHB", needsReview: false);
        var flagged = E("b", "PHB", needsReview: true);
        DuplicateResolver.Winner([flagged, clean], Books).Should().Be(clean);
    }

    [Fact]
    public void Longer_text_wins_when_higher_tiers_tie()
    {
        var shortT = E("a", "PHB", text: "x");
        var longT = E("b", "PHB", text: "xxxxxxxxxx");
        DuplicateResolver.Winner([shortT, longT], Books).Should().Be(longT);
    }

    [Fact]
    public void Lexicographic_id_is_the_final_deterministic_tiebreak()
    {
        var z = E("zzz", "PHB");
        var a = E("aaa", "PHB");
        DuplicateResolver.Winner([z, a], Books).Should().Be(a);
        DuplicateResolver.Winner([a, z], Books).Should().Be(a); // order-independent
    }

    [Fact]
    public void Unmapped_source_book_is_unknown_authority()
    {
        var known = E("a", "PHB");            // Core
        var unmapped = E("b", "MYSTERY");       // not in map → Unknown (lowest)
        DuplicateResolver.Winner([unmapped, known], Books).Should().Be(known);
    }
}