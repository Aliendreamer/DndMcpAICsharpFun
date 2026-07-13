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
    public void Keys_are_unique_and_nonempty()
    {
        BookCatalog.All.Should().OnlyContain(b =>
            !string.IsNullOrWhiteSpace(b.Key) && !string.IsNullOrWhiteSpace(b.DisplayName));
        BookCatalog.Keys.Count.Should().Be(BookCatalog.All.Count);
    }
}
