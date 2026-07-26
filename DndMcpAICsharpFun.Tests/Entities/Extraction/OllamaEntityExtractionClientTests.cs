using System.Text.Json;

using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class OllamaEntityExtractionClientTests
{
    private sealed class StubChatClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    /// <summary>Never returns; only completes (by throwing) when its call token is cancelled — models
    /// a hung ollama generation that the per-call timeout must abandon.</summary>
    private sealed class HangingChatClient : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    [Fact]
    public void Default_per_call_timeout_is_600_seconds()
    {
        // Guards the PRODUCTION default: no appsettings section sets Ollama:ChatTimeoutSeconds, so this
        // property value is what live extraction runs with. Every other test here passes an explicit
        // timeout, so without this the default could drift back unnoticed. 240s dropped ~7% of the
        // Strixhaven candidates (5 of 69) as "timed out" when the generations were merely slow.
        new OllamaOptions().ChatTimeoutSeconds.Should().Be(600);
    }

    private static IOptions<OllamaOptions> Options(int timeoutSeconds = 240) =>
        Microsoft.Extensions.Options.Options.Create(new OllamaOptions { ChatTimeoutSeconds = timeoutSeconds });

    private static ExtractionRequest Request() => new(
        SystemPrompt: "sys", UserPrompt: "user",
        ToolName: "emit_spell_fields", ToolDescription: "d",
        ToolInputSchema: JsonDocument.Parse("{}").RootElement.Clone(),
        ModelId: "qwen3:8b", MaxOutputTokens: 100);

    [Fact]
    public async Task ExtractAsync_ValidJson_Succeeds()
    {
        var client = new OllamaEntityExtractionClient(
            new StubChatClient("""{"name":"Fireball"}"""),
            new PartialJsonRecoverer(),
            Options(),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeTrue();
        resp.ToolInput!.Value.GetProperty("name").GetString().Should().Be("Fireball");
    }

    [Fact]
    public async Task ExtractAsync_TruncatedJsonWithRecoverablePrefix_RecoversPartial()
    {
        var raw = """{"name":"Fireball","level":3} model kept talking""";
        var client = new OllamaEntityExtractionClient(
            new StubChatClient(raw),
            new PartialJsonRecoverer(),
            Options(),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeTrue();
        resp.ToolInput!.Value.GetProperty("level").GetInt32().Should().Be(3);
        resp.RawJson.Should().Be("""{"name":"Fireball","level":3}""");
    }

    [Fact]
    public async Task ExtractAsync_UnrecoverableGarbage_FailsAsBefore()
    {
        var client = new OllamaEntityExtractionClient(
            new StubChatClient("not json at all"),
            new PartialJsonRecoverer(),
            Options(),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeFalse();
        resp.ErrorMessage.Should().Contain("not valid JSON");
    }

    [Fact]
    public async Task ExtractAsync_HungGeneration_TimesOutAsFailure_WithoutThrowing()
    {
        // A hung ollama call must not block forever: the per-call timeout cancels it and the
        // candidate is recorded as a failed extraction so the run continues. 1s keeps the test fast.
        var client = new OllamaEntityExtractionClient(
            new HangingChatClient(),
            new PartialJsonRecoverer(),
            Options(timeoutSeconds: 1),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeFalse();
        resp.ErrorMessage.Should().Contain("timed out");
        resp.StopReason.Should().Be("timeout");
    }

    [Fact]
    public async Task ExtractAsync_CallerCancellation_PropagatesAndDoesNotSwallow()
    {
        // A real caller cancellation (not a timeout) must still abort the run, not be swallowed.
        var client = new OllamaEntityExtractionClient(
            new HangingChatClient(),
            new PartialJsonRecoverer(),
            Options(timeoutSeconds: 240),
            NullLogger<OllamaEntityExtractionClient>.Instance);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var act = async () => await client.ExtractAsync(Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
