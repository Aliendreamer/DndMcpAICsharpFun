using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tests.Ingestion.Extraction;

public class OllamaLlmEntityExtractorTests
{
    private static OllamaOptions DefaultOllamaOptions() => new() { ExtractionModel = "llama3.2" };

    private static OllamaLlmEntityExtractor BuildSut(
        IOllamaApiClient ollama,
        int llmExtractionRetries = 1)
    {
        var ingestionOptions = new IngestionOptions { LlmExtractionRetries = llmExtractionRetries };
        return new OllamaLlmEntityExtractor(
            ollama,
            Options.Create(DefaultOllamaOptions()),
            Options.Create(ingestionOptions),
            NullLogger<OllamaLlmEntityExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_ValidJsonFirstAttempt_ReturnsEntityWithoutRetry()
    {
        // Arrange
        var json = """[{"name":"Fireball","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        // Act
        var results = await sut.ExtractAsync("page text", "Spell", 1, "PHB", "5e");

        // Assert
        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_InvalidThenValid_RetrySucceeds()
    {
        // Arrange
        var validJson = """[{"name":"Fireball","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                StreamResponse("not valid json"),
                StreamResponse(validJson));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        // Act
        var results = await sut.ExtractAsync("page text", "Spell", 2, "PHB", "5e");

        // Assert
        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(2).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_AllAttemptsInvalid_ReturnsEmpty()
    {
        // Arrange
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        // Act
        var results = await sut.ExtractAsync("page text", "Spell", 3, "PHB", "5e");

        // Assert
        Assert.Empty(results);
        Assert.DoesNotContain(results, e => e.Name.StartsWith("page_") && e.Name.EndsWith("_raw"));
    }

    [Fact]
    public async Task ExtractAsync_ZeroRetries_SingleAttemptOnly()
    {
        // Arrange
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"));

        var sut = BuildSut(ollama, llmExtractionRetries: 0);

        // Act
        var results = await sut.ExtractAsync("page text", "Spell", 4, "PHB", "5e");

        // Assert
        Assert.Empty(results);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    private static IAsyncEnumerable<ChatResponseStream?> StreamResponse(string content)
        => AsyncEnumerable(new ChatResponseStream { Message = new Message { Content = content } });

    private static async IAsyncEnumerable<T> AsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }
}
