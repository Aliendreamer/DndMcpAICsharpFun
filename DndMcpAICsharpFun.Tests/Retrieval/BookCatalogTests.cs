using DndMcpAICsharpFun.Features.Retrieval;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public class BookCatalogTests
{
    [Fact]
    public void Keys_and_display_names_round_trip()
    {
        BookCatalog.KeyToDisplayName["DMG"].Should().Be("Dungeon Master's Guide 2014");
        BookCatalog.DisplayNameToKey["Dungeon Master's Guide 2014"].Should().Be("DMG");
        BookCatalog.DisplayNameToKey["PlayerHandbook 2014"].Should().Be("PHB");
    }

    [Fact]
    public void ToDisplayNames_maps_scope_keys_back_to_citation_names()
    {
        // ScopedBooks in tool results must be human/LLM-facing display names, not the terse
        // filtering keys — the passages cite "Dungeon Master's Guide 2014", so the scope summary must too.
        BookCatalog.ToDisplayNames(new[] { "DMG", "PHB" })
            .Should().Equal("Dungeon Master's Guide 2014", "PlayerHandbook 2014");
        BookCatalog.ToDisplayNames(new[] { "XGE" }).Should().ContainSingle()
            .Which.Should().Be("Xanathar's Guide to Everything");
    }

    [Fact]
    public void Keys_are_unique_and_nonempty()
    {
        BookCatalog.All.Should().OnlyContain(b =>
            !string.IsNullOrWhiteSpace(b.Key) && !string.IsNullOrWhiteSpace(b.DisplayName));
        BookCatalog.Keys.Count.Should().Be(BookCatalog.All.Count);
    }
}
