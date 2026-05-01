using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public sealed class OllamaLlmClassifierTests
{
    private static OllamaLlmClassifier BuildSut(IOllamaApiClient ollama) =>
        new(ollama,
            Options.Create(new OllamaOptions { ExtractionModel = "llama3.2" }),
            NullLogger<OllamaLlmClassifier>.Instance);

    private static IAsyncEnumerable<ChatResponseStream?> StreamResponse(string content) =>
        YieldItems(new ChatResponseStream { Message = new Message { Content = content } });

    private static async IAsyncEnumerable<T> YieldItems<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatResponseStream?> ThrowingStream()
    {
        await Task.CompletedTask;
        throw new OperationCanceledException();
        yield break;
    }

    [Fact]
    public async Task ClassifyPageAsync_ValidJsonObject_ReturnsCategories()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("""{"types":["Spell","Monster"]}"""));
        var sut = BuildSut(ollama);

        var result = await sut.ClassifyPageAsync("some page text");

        Assert.Equal(2, result.Count);
        Assert.Contains("Spell", result);
        Assert.Contains("Monster", result);
    }

    [Fact]
    public async Task ClassifyPageAsync_EmptyTypesArray_ReturnsEmptyList()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("""{"types":[]}"""));
        var sut = BuildSut(ollama);

        var result = await sut.ClassifyPageAsync("some page text");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ClassifyPageAsync_InvalidJson_ReturnsEmptyList()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not json at all"));
        var sut = BuildSut(ollama);

        var result = await sut.ClassifyPageAsync("some page text");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ClassifyPageAsync_EmptyStringResponse_ReturnsEmptyList()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(string.Empty));
        var sut = BuildSut(ollama);

        var result = await sut.ClassifyPageAsync("some page text");

        Assert.Empty(result);
    }

    [Fact]
    public async Task ClassifyPageAsync_CancelledToken_ThrowsOperationCancelled()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(ThrowingStream());
        var sut = BuildSut(ollama);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => sut.ClassifyPageAsync("some page text", CancellationToken.None));
    }
}
