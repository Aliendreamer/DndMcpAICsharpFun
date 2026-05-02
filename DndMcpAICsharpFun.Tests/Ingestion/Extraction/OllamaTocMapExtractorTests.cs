using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class OllamaTocMapExtractorTests
{
    private static OllamaOptions DefaultOptions() => new() { ExtractionModel = "llama3.2" };

    private static OllamaTocMapExtractor BuildSut(IOllamaApiClient ollama) =>
        new(ollama, Options.Create(DefaultOptions()), NullLogger<OllamaTocMapExtractor>.Instance);

    [Fact]
    public async Task ExtractMapAsync_ReturnsEmptyList_WhenTocTextIsEmpty()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync(string.Empty);

        Assert.Empty(result);
        ollama.DidNotReceive().ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractMapAsync_ReturnsEmptyList_WhenLlmReturnsInvalidJson()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks("not json"));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("Table of Contents...");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractMapAsync_ParsesValidLlmResponse_ReturnsEntries()
    {
        var json = """
            [
              {"title":"Classes","category":"Class","startPage":45,"endPage":112},
              {"title":"Spells","category":"Spell","startPage":200,"endPage":null}
            ]
            """;
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("Table of Contents\nClasses ... 45\nSpells ... 200");

        Assert.Equal(2, result.Count);
        Assert.Equal("Classes", result[0].Title);
        Assert.Equal(ContentCategory.Class, result[0].Category);
        Assert.Equal(45, result[0].StartPage);
        Assert.Equal(112, result[0].EndPage);

        Assert.Equal("Spells", result[1].Title);
        Assert.Equal(ContentCategory.Spell, result[1].Category);
        Assert.Equal(200, result[1].StartPage);
        Assert.Null(result[1].EndPage); // null passed through; TocCategoryMap fills it
    }

    [Fact]
    public async Task ExtractMapAsync_ParsesNullCategory_AsNullCategory()
    {
        var json = """[{"title":"Introduction","category":null,"startPage":1,"endPage":10}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("intro text");

        Assert.Single(result);
        Assert.Null(result[0].Category);
    }

    [Fact]
    public async Task ExtractMapAsync_SkipsEntriesWithZeroStartPage()
    {
        var json = """
            [
              {"title":"Bad","category":"Rule","startPage":0,"endPage":5},
              {"title":"Good","category":"Rule","startPage":10,"endPage":20}
            ]
            """;
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = BuildSut(ollama);

        var result = await sut.ExtractMapAsync("toc text");

        Assert.Single(result);
        Assert.Equal(10, result[0].StartPage);
    }

    private static async IAsyncEnumerable<ChatResponseStream?> AsyncChunks(string text)
    {
        yield return new ChatResponseStream { Message = new Message { Content = text } };
        await Task.CompletedTask;
    }
}
