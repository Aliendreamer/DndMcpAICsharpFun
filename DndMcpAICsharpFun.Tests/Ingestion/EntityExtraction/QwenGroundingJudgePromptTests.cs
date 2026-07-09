using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class QwenGroundingJudgePromptTests
{
    private sealed class RecordingChatClient(string reply) : IChatClient
    {
        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedMessages = messages.ToList();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("simulated transport failure");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static EntityEnvelope MakeEnvelope(string fieldsJson) => new(
        Id: "phb.spell.fireball",
        Type: EntityType.Spell,
        Name: "Fireball",
        SourceBook: "PHB",
        Edition: "Edition2014",
        Page: 241,
        FirstAppearedIn: new FirstAppearance("PHB", "Edition2014", 241),
        RevisedIn: [],
        SettingTags: [],
        CanonicalText: "A bright streak flashes from your pointing finger...",
        Fields: JsonDocument.Parse(fieldsJson).RootElement.Clone());

    private static QwenGroundingJudge BuildSut(IChatClient chat) =>
        new(chat, Options.Create(new OllamaOptions { ChatModel = "qwen3:8b" }),
            NullLogger<QwenGroundingJudge>.Instance);

    [Fact]
    public async Task AffirmativeReply_ReturnsTrue()
    {
        var chat = new RecordingChatClient("yes");
        var judge = BuildSut(chat);

        var result = await judge.AreFieldsSupportedAsync(
            MakeEnvelope("""{"damage":"8d6 fire"}"""), "A bright streak of fire deals 8d6 fire damage.",
            CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task NegativeReply_ReturnsFalse()
    {
        var chat = new RecordingChatClient("no");
        var judge = BuildSut(chat);

        var result = await judge.AreFieldsSupportedAsync(
            MakeEnvelope("""{"damage":"8d6 fire"}"""), "You hurl a bubble of acid at a creature.",
            CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task HedgedOrGarbageReply_ReturnsNull()
    {
        var chat = new RecordingChatClient("I'm not entirely sure about this one.");
        var judge = BuildSut(chat);

        var result = await judge.AreFieldsSupportedAsync(
            MakeEnvelope("""{"damage":"8d6 fire"}"""), "A bright streak of fire deals 8d6 fire damage.",
            CancellationToken.None);

        result.Should().BeNull("a reply that isn't clearly \"yes\" or \"no\" must not be treated as a confirmed fabrication");
    }

    [Fact]
    public async Task TransportException_ReturnsNull()
    {
        var judge = BuildSut(new ThrowingChatClient());

        var result = await judge.AreFieldsSupportedAsync(
            MakeEnvelope("""{"damage":"8d6 fire"}"""), "A bright streak of fire deals 8d6 fire damage.",
            CancellationToken.None);

        result.Should().BeNull("a transient transport failure must not be mislabeled as a confirmed fabrication");
    }

    [Fact]
    public async Task Prompt_ContainsEntityFieldsAndSourceProse()
    {
        var chat = new RecordingChatClient("yes");
        var judge = BuildSut(chat);
        const string sourceProse = "A bright streak of fire deals 8d6 fire damage to everything in a 20-foot radius.";
        var entity = MakeEnvelope("""{"damage":"8d6 fire","areaOfEffect":"20-foot radius"}""");

        await judge.AreFieldsSupportedAsync(entity, sourceProse, CancellationToken.None);

        var promptText = string.Join(
            "\n", chat.CapturedMessages!.Select(m => m.Text));
        promptText.Should().Contain(entity.Fields.GetRawText());
        promptText.Should().Contain(sourceProse);
    }
}
