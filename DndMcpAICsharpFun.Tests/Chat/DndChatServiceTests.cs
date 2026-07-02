using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tests.Chat;

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

public sealed class DndChatServiceTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        // Clean up the per-test persona temp dirs instead of leaking one per invocation (COR-07).
        foreach (var dir in _tempDirs)
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private PersonaProvider CreatePersonaProvider(string personaText = "Test persona.")
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        _tempDirs.Add(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "companion.md"), personaText);
        var opts = Options.Create(new ChatPersonaOptions { PersonasDirectory = tempDir });
        return new PersonaProvider(opts);
    }

    private DndChatService CreateService(
        FakeChatClient client,
        IReadOnlyList<AITool>? tools = null,
        PersonaProvider? personaProvider = null) =>
        new(client,
            new FakeMcpToolsProvider(tools ?? []),
            new ChatRepository(new NoOpDbFactory()),
            new NullHttpContextAccessor(),
            new ChatRateLimiter(1000),
            personaProvider ?? CreatePersonaProvider());

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

    [Fact]
    public async Task SendAsync_prepends_system_message_equal_to_active_persona()
    {
        const string personaText = "You are a D&D companion.";
        var client = new FakeChatClient();
        var provider = CreatePersonaProvider(personaText);
        var svc = CreateService(client, personaProvider: provider);

        await svc.SendAsync("Tell me about fighters.", false, CancellationToken.None);

        client.LastMessages.Should().NotBeNull();
        client.LastMessages![0].Role.Should().Be(ChatRole.System);
        client.LastMessages![0].Text.Should().Be(personaText);
    }

    [Fact]
    public async Task SendAsync_sends_system_then_history_messages_to_chat_client()
    {
        const string personaText = "You are a D&D companion.";
        var client = new FakeChatClient();
        var provider = CreatePersonaProvider(personaText);
        var svc = CreateService(client, personaProvider: provider);

        await svc.SendAsync("What is a paladin?", false, CancellationToken.None);

        // First message is system; second is the user message
        client.LastMessages.Should().HaveCount(2);
        client.LastMessages![0].Role.Should().Be(ChatRole.System);
        client.LastMessages![1].Role.Should().Be(ChatRole.User);
        client.LastMessages![1].Text.Should().Be("What is a paladin?");
    }

    [Fact]
    public async Task SendAsync_does_not_add_system_message_to_History()
    {
        const string personaText = "You are a D&D companion.";
        var client = new FakeChatClient();
        var provider = CreatePersonaProvider(personaText);
        var svc = CreateService(client, personaProvider: provider);

        await svc.SendAsync("Hello", false, CancellationToken.None);

        svc.History.Should().NotContain(m => m.Role == ChatRole.System);
        svc.History.Should().HaveCount(2); // user + assistant only
    }

    [Fact]
    public async Task SendAsync_history_contains_only_user_and_assistant_roles()
    {
        var client = new FakeChatClient { Reply = "Here is your answer." };
        var svc = CreateService(client);

        await svc.SendAsync("A question", false, CancellationToken.None);

        svc.History.Should().OnlyContain(m =>
            m.Role == ChatRole.User || m.Role == ChatRole.Assistant);
    }
}
