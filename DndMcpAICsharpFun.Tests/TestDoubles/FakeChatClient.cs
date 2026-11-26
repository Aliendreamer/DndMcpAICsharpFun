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

    /// <summary>When set, the reply texts of successive requests; then <see cref="Reply"/>.</summary>
    public Queue<string>? Replies { get; set; }

    /// <summary>When set, returned verbatim instead of a plain-text <see cref="Reply"/> response.</summary>
    public ChatResponse? Response { get; set; }

    /// <summary>When set, the responses of successive requests, before <see cref="Response"/> and <see cref="Replies"/>.</summary>
    public Queue<ChatResponse>? Responses { get; set; }

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
        if (Responses is { Count: > 0 }) return Task.FromResult(Responses.Dequeue());
        if (Response is not null) return Task.FromResult(Response);
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
            Replies is { Count: > 0 } ? Replies.Dequeue() : Reply)));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public object? GetService(Type serviceType, object? key = null) => null;
    public void Dispose() { }
}