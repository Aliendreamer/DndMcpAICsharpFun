using System.IO;

using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.FivetoolsIngestion;

public class BookSourceRegistryTests : IDisposable
{
    private readonly string _tmpPath;

    public BookSourceRegistryTests()
    {
        _tmpPath = Path.GetTempFileName();
        var json = """
        {
          "book": [
            { "id": "PHB",  "name": "Player's Handbook (2014)", "source": "PHB",  "group": "core",       "published": "2014-08-19" },
            { "id": "XPHB", "name": "Player's Handbook (2024)", "source": "XPHB", "group": "core",       "published": "2024-09-17" },
            { "id": "TCE",  "name": "Tasha's Cauldron of Everything", "source": "TCE", "group": "supplement", "published": "2020-11-17" }
          ]
        }
        """;
        File.WriteAllText(_tmpPath, json);
    }

    public void Dispose() => File.Delete(_tmpPath);

    [Fact]
    public void TryGetBook_KnownKey_ReturnsInfo()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var info = sut.TryGetBook("PHB");
        info.Should().NotBeNull();
        info!.SourceKey.Should().Be("PHB");
        info.Group.Should().Be("core");
        info.PublishedYear.Should().Be(2014);
        info.DisplayAbbr.Should().Be("PHB'14");
    }

    [Fact]
    public void TryGetBook_XPrefixedKey_StripsXInDisplayAbbr()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var info = sut.TryGetBook("XPHB");
        info!.DisplayAbbr.Should().Be("PHB'24");
    }

    [Fact]
    public void TryGetBook_SupplementKey_AppendsYear()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var info = sut.TryGetBook("TCE");
        info!.DisplayAbbr.Should().Be("TCE'20");
    }

    [Fact]
    public void TryGetBook_UnknownKey_ReturnsNull()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.TryGetBook("HOMEBREW").Should().BeNull();
    }

    [Fact]
    public void GetByGroup_Core_ReturnsCoreKeys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var keys = sut.GetByGroup("core");
        keys.Should().BeEquivalentTo(["PHB", "XPHB"]);
    }

    [Fact]
    public void GetByGroup_Unknown_ReturnsEmpty()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.GetByGroup("does-not-exist").Should().BeEmpty();
    }

    [Fact]
    public void ResolveIntent_CoreBooks_ReturnsCoreKeys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("core books").Should().BeEquivalentTo(["PHB", "XPHB"]);
        sut.ResolveIntent("core").Should().BeEquivalentTo(["PHB", "XPHB"]);
    }

    [Fact]
    public void ResolveIntent_2024_Returns2024PlusKeys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("2024").Should().BeEquivalentTo(["XPHB"]);
    }

    [Fact]
    public void ResolveIntent_2014_Returns2014Keys()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("2014").Should().BeEquivalentTo(["PHB"]);
    }

    [Fact]
    public void ResolveIntent_Srd_ReturnsSentinel()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("srd").Should().BeEquivalentTo(["srd52"]);
        sut.ResolveIntent("free rules").Should().BeEquivalentTo(["srd52"]);
    }

    [Fact]
    public void ResolveIntent_Unknown_ReturnsEmpty()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        sut.ResolveIntent("gibberish").Should().BeEmpty();
    }

    [Fact]
    public void MissingBooksJson_DoesNotThrow_EmptyRegistry()
    {
        var sut = new BookSourceRegistry("/nonexistent/path/books.json");
        sut.TryGetBook("PHB").Should().BeNull();
        sut.GetByGroup("core").Should().BeEmpty();
    }

    [Fact]
    public void SuggestByName_PartialMatch_ReturnsSuggestions()
    {
        var sut = new BookSourceRegistry(_tmpPath);
        var suggestions = sut.SuggestByName("Player's Handbook");
        suggestions.Should().Contain("PHB");
    }
}