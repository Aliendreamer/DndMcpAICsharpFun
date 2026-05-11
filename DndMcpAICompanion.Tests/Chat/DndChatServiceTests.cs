using DndMcpAICompanion.Features.Chat;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Xunit;

namespace DndMcpAICompanion.Tests.Chat;

internal sealed class FakeChatClient : IChatClient
{
    public string Reply { get; set; } = "Test reply";
    public bool ShouldThrow { get; set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
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

internal sealed class NullHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

public class DndChatServiceTests
{
    private static DndChatService CreateService(FakeChatClient client) =>
        new(client, [], new NullHttpContextAccessor(), new ChatRateLimiter(1000));

    [Fact]
    public async Task SendAsync_appends_user_and_assistant_messages_to_history()
    {
        var client = new FakeChatClient { Reply = "Fireball deals 8d6 fire damage." };
        var svc = CreateService(client);

        var reply = await svc.SendAsync("What does fireball do?", CancellationToken.None);

        reply.Should().Be("Fireball deals 8d6 fire damage.");
        svc.History.Should().HaveCount(2);
        svc.History[0].Role.Should().Be(ChatRole.User);
        svc.History[0].Text.Should().Be("What does fireball do?");
        svc.History[1].Role.Should().Be(ChatRole.Assistant);
        svc.History[1].Text.Should().Be("Fireball deals 8d6 fire damage.");
    }

    [Fact]
    public async Task SendAsync_returns_error_message_when_ollama_is_unreachable()
    {
        var client = new FakeChatClient { ShouldThrow = true };
        var svc = CreateService(client);

        var reply = await svc.SendAsync("Hello", CancellationToken.None);

        reply.Should().Be("The AI is unavailable right now. Please try again.");
        svc.History.Should().HaveCount(2);
        svc.History[1].Role.Should().Be(ChatRole.Assistant);
    }
}
