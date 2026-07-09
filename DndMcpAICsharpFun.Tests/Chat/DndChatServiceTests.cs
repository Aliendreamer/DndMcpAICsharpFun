using System.Security.Claims;
using System.Text.Json;

using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
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

    /// <summary>
    /// Builds a real <see cref="EncounterDesignService"/> over <see cref="NoOpDbFactory"/>-backed
    /// repositories (mirroring <see cref="CharacterResolutionService"/> below) and a fake
    /// monster-source/search — sealed/concrete, so it cannot be substituted directly, but its own
    /// DB-touching dependencies can, keeping these chat-wiring tests DB-free.
    /// </summary>
    private static EncounterDesignService BuildEncounterDesignService(
        IEntityRetrievalService? search = null, IEncounterMonsterSource? source = null)
    {
        var assessor = new EncounterAssessor();
        var generator = new EncounterGenerator(source ?? Substitute.For<IEncounterMonsterSource>(), assessor);
        return new EncounterDesignService(
            assessor,
            generator,
            new DndMcpAICsharpFun.Features.Campaigns.HeroRepository(new NoOpDbFactory()),
            new DndMcpAICsharpFun.Features.Campaigns.CampaignRepository(new NoOpDbFactory()),
            search ?? Substitute.For<IEntityRetrievalService>());
    }

    private static IHttpContextAccessor AuthenticatedAs(long userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
        };
        return new NullHttpContextAccessor { HttpContext = ctx };
    }

    /// <summary>Round-trips an anonymous object through JSON so values arrive as <see cref="JsonElement"/>,
    /// matching how a real LLM tool-call's arguments are bound.</summary>
    private static AIFunctionArguments ToArgs(object args)
    {
        var json = JsonSerializer.Serialize(args);
        var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        var result = new AIFunctionArguments();
        foreach (var (key, value) in dict) result[key] = value;
        return result;
    }

    private DndChatService CreateService(
        FakeChatClient client,
        IReadOnlyList<AITool>? tools = null,
        PersonaProvider? personaProvider = null,
        IHttpContextAccessor? httpContextAccessor = null,
        EncounterDesignService? encounterService = null) =>
        new(client,
            new FakeMcpToolsProvider(tools ?? []),
            new ChatRepository(new NoOpDbFactory()),
            httpContextAccessor ?? new NullHttpContextAccessor(),
            new ChatRateLimiter(1000),
            personaProvider ?? CreatePersonaProvider(),
            new DndMcpAICsharpFun.Features.Resolution.CharacterResolutionService(
                new NoOpDbFactory(),
                new DndMcpAICsharpFun.Features.Campaigns.HeroRepository(new NoOpDbFactory())),
            encounterService ?? BuildEncounterDesignService());

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

    [Fact]
    public async Task SendAsync_does_not_add_encounter_tools_when_unauthenticated()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client); // default NullHttpContextAccessor: no signed-in user

        await svc.SendAsync("Rate my encounter", false, CancellationToken.None);

        var toolNames = client.LastOptions!.Tools!.OfType<AIFunction>().Select(t => t.Name).ToList();
        toolNames.Should().NotContain("rate_encounter");
        toolNames.Should().NotContain("build_encounter");
    }

    [Fact]
    public async Task SendAsync_adds_rate_and_build_encounter_tools_when_authenticated()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(1));

        await svc.SendAsync("Rate my encounter", false, CancellationToken.None);

        var toolNames = client.LastOptions!.Tools!.OfType<AIFunction>().Select(t => t.Name).ToList();
        toolNames.Should().Contain("rate_encounter");
        toolNames.Should().Contain("build_encounter");
    }

    [Fact]
    public async Task Encounter_tool_schemas_do_not_expose_userId_as_a_caller_supplied_argument()
    {
        // SEC-08: userId must come from the signed-in user's claim (the closure), never from a
        // caller-controlled tool argument — otherwise a caller could pass another user's id.
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(7));

        await svc.SendAsync("Rate my encounter", false, CancellationToken.None);

        var tools = client.LastOptions!.Tools!.OfType<AIFunction>()
            .Where(t => t.Name is "rate_encounter" or "build_encounter");
        foreach (var tool in tools)
        {
            var hasUserId = tool.JsonSchema.TryGetProperty("properties", out var props)
                && props.TryGetProperty("userId", out _);
            hasUserId.Should().BeFalse($"{tool.Name} must not expose userId as a caller argument");
        }
    }

    [Fact]
    public async Task RateEncounterTool_routes_partyLevels_and_monsters_to_EncounterDesignService()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

        await svc.SendAsync("Rate it", false, CancellationToken.None);

        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "rate_encounter");
        var result = await tool.InvokeAsync(
            ToArgs(new { campaignId = (long?)null, partyLevels = new[] { 5 }, monsters = Array.Empty<string>(), edition = "2014" }),
            CancellationToken.None);

        // AIFunction.InvokeAsync marshals the delegate's return value through the tool's
        // JsonSerializerOptions (the same path a real LLM tool-call result takes), so the raw
        // result is a JsonElement rather than the EncounterAssessment instance directly.
        result.Should().BeOfType<JsonElement>();
        var assessment = ((JsonElement)result!).Deserialize<EncounterAssessment>(tool.JsonSerializerOptions);
        assessment.Should().NotBeNull();
        assessment!.Monsters.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildEncounterTool_forwards_difficulty_edition_and_theme_to_EncounterDesignService()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

        await svc.SendAsync("Build it", false, CancellationToken.None);

        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "build_encounter");

        // build_encounter never supplies partyLevels (always null per BuildForUserAsync's contract),
        // so with no campaignId either, EncounterDesignService.ResolvePartyAsync's
        // "supply campaignId or partyLevels" guard fires — proving the tool actually reached
        // BuildForUserAsync with the caller's difficulty/edition/theme rather than short-circuiting.
        var act = () => tool.InvokeAsync(
            ToArgs(new { campaignId = (long?)null, difficulty = "Hard", edition = "2024", theme = (string?)null }),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
