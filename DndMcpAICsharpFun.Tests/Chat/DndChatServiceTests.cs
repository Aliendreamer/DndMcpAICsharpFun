using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tests.Chat;

internal sealed class FakeChatClient : IChatClient
{
    public string Reply { get; set; } = "Test reply";
    public bool ShouldThrow { get; set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
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

internal sealed class NullHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }
}

internal sealed class FakeMcpToolsProvider(IReadOnlyList<AITool> tools) : IMcpToolsProvider
{
    public Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default) =>
        Task.FromResult(tools);
}

// Chat persistence only runs for an authenticated user; these tests use a null
// HttpContext, so the repository is never touched — a throwing factory is fine.
internal sealed class NoOpDbFactory : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => throw new NotSupportedException();
}

public sealed class DndChatServiceTests
{
    private static DndChatService CreateService(FakeChatClient client, IReadOnlyList<AITool>? tools = null) =>
        new(client,
            new FakeMcpToolsProvider(tools ?? []),
            new ChatRepository(new NoOpDbFactory()),
            new NullHttpContextAccessor(),
            new ChatRateLimiter(1000));

    [Fact]
    public async Task SendAsync_appends_user_and_assistant_messages_to_history()
    {
        var client = new FakeChatClient { Reply = "Fireball deals 8d6 fire damage." };
        var svc = CreateService(client);

        var ok = await svc.SendAsync("What does fireball do?", false, CancellationToken.None);

        ok.Should().BeTrue();
        svc.History.Should().HaveCount(2);
        svc.History[0].Role.Should().Be(ChatRole.User);
        svc.History[0].Text.Should().Be("What does fireball do?");
        svc.History[1].Role.Should().Be(ChatRole.Assistant);
        svc.History[1].Text.Should().Be("Fireball deals 8d6 fire damage.");
    }

    [Fact]
    public async Task SendAsync_returns_false_and_adds_no_assistant_message_when_unreachable()
    {
        var client = new FakeChatClient { ShouldThrow = true };
        var svc = CreateService(client);

        var ok = await svc.SendAsync("Hello", false, CancellationToken.None);

        ok.Should().BeFalse();
        svc.History.Should().ContainSingle();          // user message only
        svc.History[0].Role.Should().Be(ChatRole.User);
    }

    [Fact]
    public async Task SendAsync_excludes_search_web_tool_when_web_search_disabled()
    {
        var client = new FakeChatClient();
        var searchWeb = AIFunctionFactory.Create(() => "result", "search_web");
        var searchLore = AIFunctionFactory.Create(() => "result", "search_lore");
        var svc = CreateService(client, [searchWeb, searchLore]);

        await svc.SendAsync("Hello", allowWebSearch: false, CancellationToken.None);

        client.LastOptions.Should().NotBeNull();
        var activeTools = client.LastOptions!.Tools!;
        activeTools.Should().ContainSingle();
        activeTools.OfType<AIFunction>()
            .Should().ContainSingle(t => t.Name == "search_lore");
        activeTools.OfType<AIFunction>()
            .Should().NotContain(t => t.Name == "search_web");
    }

    [Fact]
    public async Task SendAsync_includes_search_web_tool_when_web_search_enabled()
    {
        var client = new FakeChatClient();
        var searchWeb = AIFunctionFactory.Create(() => "result", "search_web");
        var searchLore = AIFunctionFactory.Create(() => "result", "search_lore");
        var svc = CreateService(client, [searchWeb, searchLore]);

        await svc.SendAsync("Hello", allowWebSearch: true, CancellationToken.None);

        client.LastOptions.Should().NotBeNull();
        var activeTools = client.LastOptions!.Tools!;
        activeTools.Should().HaveCount(2);
        activeTools.OfType<AIFunction>()
            .Should().Contain(t => t.Name == "search_web");
    }
}
