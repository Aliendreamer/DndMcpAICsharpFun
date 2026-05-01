using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class OllamaTocCategoryClassifierTests
{
    private static OllamaOptions DefaultOptions() => new() { ExtractionModel = "llama3.2" };

    [Fact]
    public async Task ClassifyAsync_ReturnsEmptyMap_WhenBookmarksEmpty()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        var sut = new OllamaTocCategoryClassifier(
            ollama,
            Options.Create(DefaultOptions()),
            NullLogger<OllamaTocCategoryClassifier>.Instance);

        var map = await sut.ClassifyAsync([], CancellationToken.None);

        Assert.True(map.IsEmpty);
    }

    [Fact]
    public async Task ClassifyAsync_ReturnsEmptyMap_WhenLlmReturnsInvalidJson()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks("not json at all"));

        var sut = new OllamaTocCategoryClassifier(
            ollama,
            Options.Create(DefaultOptions()),
            NullLogger<OllamaTocCategoryClassifier>.Instance);

        var bookmarks = new[] { new PdfBookmark("Chapter 11: Spells", 200) };
        var map = await sut.ClassifyAsync(bookmarks, CancellationToken.None);

        Assert.True(map.IsEmpty);
    }

    [Fact]
    public async Task ClassifyAsync_ParsesValidResponse_IntoMap()
    {
        var json = """
            [
              {"startPage": 1,   "category": null},
              {"startPage": 45,  "category": "Class"},
              {"startPage": 200, "category": "Spell"}
            ]
            """;

        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(AsyncChunks(json));

        var sut = new OllamaTocCategoryClassifier(
            ollama,
            Options.Create(DefaultOptions()),
            NullLogger<OllamaTocCategoryClassifier>.Instance);

        var bookmarks = new[]
        {
            new PdfBookmark("Introduction", 1),
            new PdfBookmark("Chapter 3: Classes", 45),
            new PdfBookmark("Chapter 11: Spells", 200),
        };

        var map = await sut.ClassifyAsync(bookmarks, CancellationToken.None);

        Assert.Null(map.GetCategory(3));
        Assert.Equal(ContentCategory.Class, map.GetCategory(80));
        Assert.Equal(ContentCategory.Spell, map.GetCategory(250));
    }

    private static async IAsyncEnumerable<ChatResponseStream?> AsyncChunks(string text)
    {
        yield return new ChatResponseStream { Message = new Message { Content = text } };
        await Task.CompletedTask;
    }
}
