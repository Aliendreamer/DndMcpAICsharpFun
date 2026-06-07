using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
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
            NullLogger<OllamaEntityExtractionClient>.Instance);

        var resp = await client.ExtractAsync(Request(), CancellationToken.None);

        resp.Success.Should().BeFalse();
        resp.ErrorMessage.Should().Contain("not valid JSON");
    }
}
