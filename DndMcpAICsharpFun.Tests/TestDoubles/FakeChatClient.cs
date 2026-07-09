using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tests.TestDoubles;

/// <summary>
/// Shared <see cref="IChatClient"/> test double used by DndChatServiceTests.
/// Captures outgoing messages and options; optionally simulates a network failure.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    /// <summary>The reply text the client will return for every request.</summary>
    public string Reply { get; set; } = "Test reply";

    /// <summary>When true, <see cref="GetResponseAsync"/> throws <see cref="HttpRequestException"/>.</summary>
    public bool ShouldThrow { get; set; }

    /// <summary>The <see cref="ChatOptions"/> from the last <see cref="GetResponseAsync"/> call.</summary>
    public ChatOptions? LastOptions { get; private set; }

    /// <summary>The messages from the last <see cref="GetResponseAsync"/> call.</summary>
    public IReadOnlyList<ChatMessage>? LastMessages { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = chatMessages.ToList();
        LastOptions = options;
        if (ShouldThrow) throw new HttpRequestException("Ollama unreachable");
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? key = null) => null;
    public void Dispose() { }
}