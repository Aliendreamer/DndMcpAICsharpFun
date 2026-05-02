using DndMcpAICsharpFun.Domain;
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

    private static Task<IReadOnlyList<ExtractedEntity>> Extract(
        OllamaLlmEntityExtractor sut,
        string pageText = "page text",
        string entityType = "Spell",
        int pageNumber = 1,
        string entityName = "Spells",
        int startPage = 200,
        int endPage = 250) =>
        sut.ExtractAsync(pageText, entityType, pageNumber, "PHB", "5e", entityName, startPage, endPage);

    [Fact]
    public async Task ExtractAsync_ValidJsonFirstAttempt_ReturnsEntityWithoutRetry()
    {
        var json = """[{"name":"Fireball","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut, entityName: "Spells", startPage: 200, endPage: 250);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_InvalidThenValid_RetrySucceeds()
    {
        var validJson = """[{"name":"Fireball","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"), StreamResponse(validJson));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut, pageNumber: 2);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(2).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_AllAttemptsInvalid_ReturnsEmpty()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut, pageNumber: 3);

        Assert.Empty(results);
        ollama.Received(2).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_ZeroRetries_SingleAttemptOnly()
    {
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse("not valid json"));

        var sut = BuildSut(ollama, llmExtractionRetries: 0);

        var results = await Extract(sut, pageNumber: 4);

        Assert.Empty(results);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_WrappedJsonObject_UnwrapsAndParsesSuccessfully()
    {
        var json = """{"entities":[{"name":"Fireball","partial":false,"data":{"description":"test"}}]}""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 1);

        var results = await Extract(sut);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Name);
        ollama.Received(1).ChatAsync(Arg.Any<ChatRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractAsync_ContextHint_AppearsInSystemPrompt()
    {
        var capturedRequest = (ChatRequest?)null;
        var json = """[{"name":"Warlock","partial":false,"data":{"description":"test"}}]""";
        var ollama = Substitute.For<IOllamaApiClient>();
        ollama.ChatAsync(Arg.Do<ChatRequest>(r => capturedRequest = r), Arg.Any<CancellationToken>())
            .Returns(StreamResponse(json));

        var sut = BuildSut(ollama, llmExtractionRetries: 0);

        await sut.ExtractAsync("page text", "Class", 106, "PHB", "5e", "Warlock", 105, 112);

        Assert.NotNull(capturedRequest);
        var systemMsg = capturedRequest!.Messages!.First(m => m.Role == ChatRole.System).Content;
        Assert.Contains("Warlock", systemMsg);
        Assert.Contains("105", systemMsg);
        Assert.Contains("112", systemMsg);
    }

    private static IAsyncEnumerable<ChatResponseStream?> StreamResponse(string content)
        => AsyncEnumerable(new ChatResponseStream { Message = new Message { Content = content } });

    private static async IAsyncEnumerable<T> AsyncEnumerable<T>(params T[] items)
    {
        foreach (var item in items) yield return item;
        await Task.CompletedTask;
    }
}
