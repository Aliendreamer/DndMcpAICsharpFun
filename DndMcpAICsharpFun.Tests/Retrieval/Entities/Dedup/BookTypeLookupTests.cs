using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class BookTypeLookupTests
{
    [Fact]
    public void Maps_fivetools_key_and_slug_to_booktype()
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
        map.Should().ContainKey("homebrew-tome");               // display-name slug
        map["homebrew-tome"].Should().Be(BookType.Supplement);
    }
}
