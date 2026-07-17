using System.Security.Claims;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.Rules;
using DndMcpAICsharpFun.Features.SessionPrep;
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

    /// <summary>
    /// Builds a real <see cref="DndMcpAICsharpFun.Features.CharacterAdvice.LevelUpAdviceService"/> over
    /// <see cref="NoOpDbFactory"/>-backed repositories and a fake entity search — sealed/concrete, so it
    /// cannot be substituted directly, but its own DB-touching dependencies can, keeping these chat-wiring
    /// tests DB-free.
    /// </summary>
    private static DndMcpAICsharpFun.Features.CharacterAdvice.LevelUpAdviceService BuildLevelUpAdviceService(
        IEntityRetrievalService? search = null)
    {
        var retrieval = search ?? Substitute.For<IEntityRetrievalService>();
        return new DndMcpAICsharpFun.Features.CharacterAdvice.LevelUpAdviceService(
            new DndMcpAICsharpFun.Features.Campaigns.HeroRepository(new NoOpDbFactory()),
            retrieval,
            new DndMcpAICsharpFun.Features.CharacterAdvice.LevelUpPlanner(),
            new DndMcpAICsharpFun.Features.CharacterAdvice.EntityOptionProvider(retrieval));
    }

    /// <summary>
    /// Builds a real <see cref="DndMcpAICsharpFun.Features.CharacterAdvice.BuildRecommenderService"/> over
    /// a fake entity search and the real <see cref="DndMcpAICsharpFun.Features.CharacterAdvice.EntityOptionProvider"/>
    /// — sealed/concrete, so it cannot be substituted directly, but its own retrieval dependency can,
    /// keeping these chat-wiring tests DB-free.
    /// </summary>
    private static DndMcpAICsharpFun.Features.CharacterAdvice.BuildRecommenderService BuildBuildRecommenderService(
        IEntityRetrievalService? search = null)
    {
        var retrieval = search ?? Substitute.For<IEntityRetrievalService>();
        return new DndMcpAICsharpFun.Features.CharacterAdvice.BuildRecommenderService(
            retrieval,
            new DndMcpAICsharpFun.Features.CharacterAdvice.EntityOptionProvider(retrieval));
    }

    /// <summary>
    /// Builds a real <see cref="DndMcpAICsharpFun.Features.CharacterAdvice.BuildCritiqueService"/> over a
    /// <see cref="NoOpDbFactory"/>-backed <c>HeroRepository</c> and a fake entity search — sealed/concrete,
    /// so it cannot be substituted directly, but its own DB-touching/retrieval dependencies can, keeping
    /// these chat-wiring tests DB-free.
    /// </summary>
    private static DndMcpAICsharpFun.Features.CharacterAdvice.BuildCritiqueService BuildBuildCritiqueService(
        IEntityRetrievalService? search = null) =>
        new(
            new DndMcpAICsharpFun.Features.Campaigns.HeroRepository(new NoOpDbFactory()),
            search ?? Substitute.For<IEntityRetrievalService>());


    /// <summary>
    /// Builds a real <see cref="SettingLoreService"/> over a <see cref="NoOpDbFactory"/>-backed
    /// <c>CampaignRepository</c> and a substitute <see cref="IRagRetrievalService"/> — sealed/concrete,
    /// so it cannot be substituted directly, but its own DB-touching/retrieval dependencies can,
    /// keeping these chat-wiring tests DB-free.
    /// </summary>
    private static SettingLoreService BuildSettingLoreService(IRagRetrievalService? rag = null) =>
        new(
            new CampaignRepository(new NoOpDbFactory()),
            rag ?? Substitute.For<IRagRetrievalService>());

    /// <summary>
    /// Builds a real <see cref="RulesAdjudicationService"/> over a substitute
    /// <see cref="IRagRetrievalService"/> — sealed/concrete, so it cannot be substituted directly,
    /// but its own retrieval dependency can, keeping these chat-wiring tests DB-free. Unlike
    /// <see cref="BuildSettingLoreService"/>, there is no repository at all — rules adjudication
    /// is ownership-free.
    /// </summary>
    private static RulesAdjudicationService BuildRulesAdjudicationService(IRagRetrievalService? rag = null) =>
        new(rag ?? Substitute.For<IRagRetrievalService>());

    private static DowntimeService BuildDowntimeService(IRagRetrievalService? rag = null) =>
        new(rag ?? Substitute.For<IRagRetrievalService>());

    /// <summary>
    /// Builds a real <see cref="NpcGenerationService"/> over a substitute <see cref="IEntityRetrievalService"/>
    /// — sealed/concrete, so it cannot be substituted directly, but its own retrieval dependency can, keeping
    /// these chat-wiring tests DB-free. Not ownership-gated, like <see cref="BuildRulesAdjudicationService"/>.
    /// </summary>
    private static NpcGenerationService BuildNpcGenerationService(IEntityRetrievalService? search = null) =>
        new(search ?? Substitute.For<IEntityRetrievalService>());

    // A fake that grounds ANY queried archetype: the returned hit is named after the query text.
    // Mirrors NpcGenerationServicePartyTests.EchoSearch so GeneratePartyAsync resolves a real ensemble
    // through the chat tool delegate, not just an empty not-in-corpus stub.
    private static IEntityRetrievalService EchoNpcSearch()
    {
        var s = Substitute.For<IEntityRetrievalService>();
        s.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var name = ci.Arg<EntitySearchQuery>().QueryText;
                var id = "mm.monster." + name.ToLowerInvariant().Replace(' ', '-');
                return new List<EntityDiagnosticResult> { new(id, EntityType.Monster, name, "MM",
                    "Edition2014", null, [], "pt",
                    JsonDocument.Parse("""{"cr":"1","hp":{"average":11},"dex":14}""").RootElement, 0.9f) };
            });
        s.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var id = ci.Arg<string>();
                var name = id.Replace("mm.monster.", "").Replace('-', ' ');
                return new EntityFullResult(new EntityEnvelope(id, EntityType.Monster, name, "MM",
                    "Edition2014", null, new FirstAppearance("MM", "Edition2014"), [], [],
                    $"{name}\nAC 12\nHP 27\nSTR 10 DEX 15",
                    JsonDocument.Parse("""{"cr":"1","hp":{"average":27},"str":10,"dex":15,"con":10,"int":12,"wis":14,"cha":16}""").RootElement));
            });
        return s;
    }

    /// <summary>
    /// Builds a real <see cref="SessionPrepService"/> over the DB-free
    /// <see cref="EncounterDesignService"/>/<see cref="NpcGenerationService"/>/<see cref="SettingLoreService"/>
    /// sub-service helpers above — sealed/concrete, so it cannot be substituted directly, but its
    /// sub-services' own DB-touching dependencies can, keeping these chat-wiring tests DB-free.
    /// </summary>
    private static SessionPrepService BuildSessionPrepService(
        EncounterDesignService? encounters = null,
        NpcGenerationService? npcs = null,
        SettingLoreService? lore = null) =>
        new(
            encounters ?? BuildEncounterDesignService(),
            npcs ?? BuildNpcGenerationService(),
            lore ?? BuildSettingLoreService());

    private DndChatService CreateService(
        FakeChatClient client,
        IReadOnlyList<AITool>? tools = null,
        PersonaProvider? personaProvider = null,
        IHttpContextAccessor? httpContextAccessor = null,
        EncounterDesignService? encounterService = null,
        DndMcpAICsharpFun.Features.CharacterAdvice.LevelUpAdviceService? levelUpService = null,
        DndMcpAICsharpFun.Features.CharacterAdvice.BuildRecommenderService? buildRecommenderService = null,
        DndMcpAICsharpFun.Features.CharacterAdvice.BuildCritiqueService? critiqueService = null,
        SettingLoreService? settingLoreService = null,
        RulesAdjudicationService? rulesAdjudicationService = null,
        DowntimeService? downtimeService = null,
        NpcGenerationService? npcGenerationService = null,
        SessionPrepService? sessionPrepService = null,
        DndMcpAICsharpFun.Features.Chat.Routing.QueryRouter? queryRouter = null) =>
        new(client,
            new FakeMcpToolsProvider(tools ?? []),
            new ChatRepository(new NoOpDbFactory()),
            httpContextAccessor ?? new NullHttpContextAccessor(),
            new ChatRateLimiter(1000),
            personaProvider ?? CreatePersonaProvider(),
            new DndMcpAICsharpFun.Features.Resolution.CharacterResolutionService(
                new NoOpDbFactory(),
                new DndMcpAICsharpFun.Features.Campaigns.HeroRepository(new NoOpDbFactory())),
            encounterService ?? BuildEncounterDesignService(),
            levelUpService ?? BuildLevelUpAdviceService(),
            buildRecommenderService ?? BuildBuildRecommenderService(),
            critiqueService ?? BuildBuildCritiqueService(),
            settingLoreService ?? BuildSettingLoreService(),
            rulesAdjudicationService ?? BuildRulesAdjudicationService(),
            downtimeService ?? BuildDowntimeService(),
            npcGenerationService ?? BuildNpcGenerationService(),
            sessionPrepService ?? BuildSessionPrepService(),
            queryRouter ?? BuildQueryRouter(enabled: false));

    private sealed class RouterNullEmbedding : DndMcpAICsharpFun.Features.Embedding.IEmbeddingService
    {
        public Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IList<float[]>>(texts.Select(_ => new float[] { 0f }).ToList());
    }

    private sealed class RouterNullIndex : DndMcpAICsharpFun.Features.Chat.Routing.IExemplarIndex
    {
        public Task<(string? Group, double Confidence)> ClassifyAsync(float[] q, CancellationToken ct) =>
            Task.FromResult(((string?)null, 0d));
    }

    // A router whose deterministic signal pass is always active; the embedding path is a no-op stub
    // (unused for signal queries). Disabled by default so existing chat-wiring tests see the full set.
    private static DndMcpAICsharpFun.Features.Chat.Routing.QueryRouter BuildQueryRouter(bool enabled) =>
        new(new RouterNullEmbedding(), new RouterNullIndex(),
            Microsoft.Extensions.Options.Options.Create(
                new DndMcpAICsharpFun.Features.Chat.Routing.QueryRouterOptions { Enabled = enabled }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DndMcpAICsharpFun.Features.Chat.Routing.QueryRouter>.Instance);

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
    public async Task SendAsync_does_not_force_think_mode_off()
    {
        // Guard: qwen3 reasons by default when RawRepresentationFactory is omitted. If a
        // Think=false override is ever re-added, the factory below will yield a ChatRequest
        // with Think explicitly false, and this assertion fails.
        var client = new FakeChatClient();
        var svc = CreateService(client);

        await svc.SendAsync("What does fireball do?", false, CancellationToken.None);

        var factory = client.LastOptions!.RawRepresentationFactory;
        if (factory is not null)
        {
            var raw = factory(client) as OllamaSharp.Models.Chat.ChatRequest;
            // ChatRequest.Think is OllamaSharp.Models.Chat.ThinkValue? (not plain bool?); convert via
            // ToBoolean() to compare — a raw `raw?.Think.Should().NotBe(false)` would trivially never
            // fail, since ThinkValue.Equals(object) returns false for a non-ThinkValue boxed bool.
            raw?.Think?.ToBoolean().Should().NotBe(false);
        }
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
    public async Task SendAsync_offers_the_query_router_narrowed_tools_to_the_llm_turn()
    {
        // chat-query-router (task 4.5): an ENABLED router + a character-referential query ("my ...")
        // narrows the offered Tools to the character-resolution group + the always-safe core.
        var client = new FakeChatClient();
        var tools = new[]
        {
            AIFunctionFactory.Create(() => "", "search_lore"),               // always-safe core
            AIFunctionFactory.Create(() => "", "search_entities"),           // structured-lookup group
            AIFunctionFactory.Create(() => "", "resolve_character_feature"), // character-resolution group
        };
        var svc = CreateService(client, tools, queryRouter: BuildQueryRouter(enabled: true));

        await svc.SendAsync("what is my breath weapon", allowWebSearch: false, CancellationToken.None);

        var offered = client.LastOptions!.Tools!.OfType<AIFunction>().Select(t => t.Name).ToList();
        offered.Should().Contain("resolve_character_feature")
            .And.Contain("search_lore")               // safe core always present
            .And.NotContain("search_entities");       // a different group → narrowed out
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
    public async Task SendAsync_bounds_history_sent_to_model_to_last_12_messages()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client);

        // Seed 14 history messages (well over the 12-message model window).
        for (var i = 0; i < 7; i++)
        {
            svc.History.Add(new ChatMessage(ChatRole.User, $"user turn {i}"));
            svc.History.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {i}"));
        }

        await svc.SendAsync("final question", false, CancellationToken.None);

        // SendAsync appends the new user message AND the assistant reply to History; the
        // reply is added only after the model call, so it was never part of what was sent.
        // Drop it to reconstruct the History snapshot that was actually sent to the model.
        var preResponseHistory = svc.History.Take(svc.History.Count - 1).ToList();
        var expected = preResponseHistory.TakeLast(12).ToList();
        client.LastMessages.Should().HaveCount(13);
        client.LastMessages![0].Role.Should().Be(ChatRole.System);
        for (var i = 0; i < expected.Count; i++)
        {
            client.LastMessages![i + 1].Role.Should().Be(expected[i].Role);
            client.LastMessages![i + 1].Text.Should().Be(expected[i].Text);
        }
    }

    [Fact]
    public async Task SendAsync_sends_entire_history_when_12_or_fewer_messages()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client);

        // Seed 10 history messages (under the 12-message model window).
        for (var i = 0; i < 5; i++)
        {
            svc.History.Add(new ChatMessage(ChatRole.User, $"user turn {i}"));
            svc.History.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {i}"));
        }

        await svc.SendAsync("final question", false, CancellationToken.None);

        // SendAsync appends the new user message AND the assistant reply to History; the
        // reply is added only after the model call, so it was never part of what was sent.
        // Drop it to reconstruct the History snapshot that was actually sent to the model.
        var preResponseHistory = svc.History.Take(svc.History.Count - 1).ToList();
        client.LastMessages.Should().HaveCount(preResponseHistory.Count + 1);
        client.LastMessages![0].Role.Should().Be(ChatRole.System);
        for (var i = 0; i < preResponseHistory.Count; i++)
        {
            client.LastMessages![i + 1].Role.Should().Be(preResponseHistory[i].Role);
            client.LastMessages![i + 1].Text.Should().Be(preResponseHistory[i].Text);
        }
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
        toolNames.Should().NotContain("recommend_build");
        toolNames.Should().NotContain("critique_build");
        toolNames.Should().NotContain("ask_setting_lore");
        toolNames.Should().NotContain("ask_rules");
        toolNames.Should().NotContain("plan_downtime");
        toolNames.Should().NotContain("calculate_crafting");
        toolNames.Should().NotContain("generate_npc");
        toolNames.Should().NotContain("generate_npc_party");
        toolNames.Should().NotContain("prep_session");
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
        toolNames.Should().Contain("recommend_build");
        toolNames.Should().Contain("critique_build");
        toolNames.Should().Contain("ask_setting_lore");
        toolNames.Should().Contain("ask_rules");
        toolNames.Should().Contain("plan_downtime");
        toolNames.Should().Contain("calculate_crafting");
        toolNames.Should().Contain("generate_npc");
        toolNames.Should().Contain("generate_npc_party");
        toolNames.Should().Contain("prep_session");
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
            .Where(t => t.Name is "rate_encounter" or "build_encounter" or "plan_level_up" or "recommend_build"
                or "critique_build" or "ask_setting_lore" or "ask_rules" or "plan_downtime" or "calculate_crafting"
                or "generate_npc" or "generate_npc_party" or "prep_session");
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
            ToArgs(new { campaignId = (long?)null, partyLevels = new[] { 5 }, monsters = Array.Empty<object>(), edition = "2014" }),
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
    public async Task RateEncounterTool_expands_quantity_pairs_into_repeated_monsters()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        search.GetByIdAsync("mm.monster.goblin", Arg.Any<CancellationToken>())
            .Returns(new EntityFullResult(new EntityEnvelope(
                "mm.monster.goblin", EntityType.Monster, "Goblin", "MM", "Edition2014", null,
                new FirstAppearance("MM", "Edition2014"), Array.Empty<Revision>(), Array.Empty<string>(),
                "", System.Text.Json.JsonDocument.Parse("""{"cr":"1/4"}""").RootElement)));
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42),
            encounterService: BuildEncounterDesignService(search: search));

        await svc.SendAsync("Rate it", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "rate_encounter");

        var result = await tool.InvokeAsync(
            ToArgs(new { campaignId = (long?)null, partyLevels = new[] { 5 },
                monsters = new[] { new { name = "mm.monster.goblin", quantity = 8 } }, edition = "2014" }),
            CancellationToken.None);

        var assessment = ((JsonElement)result!).Deserialize<EncounterAssessment>(tool.JsonSerializerOptions);
        assessment!.Monsters.Should().HaveCount(8);
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


    [Fact]
    public async Task AskSettingLoreTool_routes_campaignId_into_SettingLoreService_and_reaches_ownership_check()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

        await svc.SendAsync("Tell me about Eberron", false, CancellationToken.None);

        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "ask_setting_lore");

        // BuildSettingLoreService wires a NoOpDbFactory-backed CampaignRepository (DB-free, mirroring
        // BuildEncounterDesignService), so any real campaignId — foreign or absent — drives
        // SettingLoreService.AskForUserAsync into CampaignRepository.GetByIdAsync, which then fails
        // because there is no real database. That failure proves campaignId/userId were actually
        // forwarded into the service's ownership check rather than the tool short-circuiting.
        var act = () => tool.InvokeAsync(
            ToArgs(new { campaignId = (long)999, question = "who holds power", edition = "2014" }),
            CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task AskRulesTool_exposes_no_user_or_campaign_id_and_reaches_the_service()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RetrievalResult>());
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42),
            rulesAdjudicationService: BuildRulesAdjudicationService(rag));

        await svc.SendAsync("rules?", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "ask_rules");

        tool.JsonSchema.TryGetProperty("properties", out var props);
        props.TryGetProperty("userId", out _).Should().BeFalse();
        props.TryGetProperty("campaignId", out _).Should().BeFalse();

        var result = await tool.InvokeAsync(
            ToArgs(new
            {
                question = "grapple while prone",
                ruleTopics = new[] { "grappling", "prone condition" },
                edition = (string?)null,
            }),
            CancellationToken.None);
        var ruling = ((JsonElement)result!).Deserialize<RulesRulingResult>(tool.JsonSerializerOptions);
        ruling!.Passages.Should().BeEmpty(); // reached the service (empty rag → empty passages)
        ruling.Topics.Should().HaveCount(2); // two topics were retrieved (each empty) — proves ruleTopics routed
    }


    [Fact]
    public async Task PlanDowntimeTool_exposes_no_user_or_campaign_id_and_reaches_the_service()
    {
        var rag = Substitute.For<IRagRetrievalService>();
        rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>()).Returns(new List<RetrievalResult>());
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11), downtimeService: BuildDowntimeService(rag));

        await svc.SendAsync("downtime", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "plan_downtime");

        tool.JsonSchema.TryGetProperty("properties", out var props);
        props.TryGetProperty("userId", out _).Should().BeFalse();
        props.TryGetProperty("campaignId", out _).Should().BeFalse();

        var result = await tool.InvokeAsync(ToArgs(new { activity = "craft plate armor", edition = (string?)null }), CancellationToken.None);
        var plan = ((JsonElement)result!).Deserialize<DowntimePlanResult>(tool.JsonSerializerOptions);
        plan!.Passages.Should().BeEmpty(); // reached the service (empty rag → empty passages)
    }

    [Fact]
    public async Task CalculateCraftingTool_exposes_no_user_or_campaign_id()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        tool.JsonSchema.TryGetProperty("properties", out var props);
        props.TryGetProperty("userId", out _).Should().BeFalse();
        props.TryGetProperty("campaignId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task CalculateCraftingTool_guards_when_both_marketValue_and_rarity_supplied()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        var result = await tool.InvokeAsync(
            ToArgs(new { marketValue = (int?)1500, rarity = "rare", crafters = (int?)null }),
            CancellationToken.None);
        var error = ((JsonElement)result!).GetProperty("error").GetString();
        error.Should().Contain("exactly ONE");
    }

    [Fact]
    public async Task CalculateCraftingTool_guards_when_neither_marketValue_nor_rarity_supplied()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        var result = await tool.InvokeAsync(
            ToArgs(new { marketValue = (int?)null, rarity = (string?)null, crafters = (int?)null }),
            CancellationToken.None);
        var error = ((JsonElement)result!).GetProperty("error").GetString();
        error.Should().Contain("exactly ONE");
    }

    [Fact]
    public async Task CalculateCraftingTool_computes_nonmagical_numbers_and_citation()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        var result = await tool.InvokeAsync(
            ToArgs(new { marketValue = (int?)1500, rarity = (string?)null, crafters = (int?)null }),
            CancellationToken.None);
        var json = (JsonElement)result!;
        json.GetProperty("kind").GetString().Should().Be("nonmagical");
        json.GetProperty("materialsGp").GetInt32().Should().Be(750);
        json.GetProperty("totalWorkweeks").GetDouble().Should().Be(30);
        json.GetProperty("days").GetInt32().Should().Be(150);
        json.GetProperty("citation").GetString().Should().Contain("Crafting");
    }

    [Fact]
    public async Task CalculateCraftingTool_binds_when_optional_keys_are_omitted()
    {
        // Regression: the LLM sends only { "marketValue": 1500 } and omits the rarity/crafters
        // keys entirely. If those parameters lack C# defaults, AIFunctionFactory marks them
        // required and the omission fails argument binding (the "function error" the model then
        // narrates around while fabricating math). The other CalculateCrafting tests pass every
        // key explicitly, so they never exercise this path.
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        // Only the marketValue key is present — exactly what qwen3:8b emits for a nonmagical item.
        var result = await tool.InvokeAsync(ToArgs(new { marketValue = 1500 }), CancellationToken.None);
        var json = (JsonElement)result!;
        json.GetProperty("kind").GetString().Should().Be("nonmagical");
        json.GetProperty("materialsGp").GetInt32().Should().Be(750);
        json.GetProperty("totalWorkweeks").GetDouble().Should().Be(30);
        json.GetProperty("days").GetInt32().Should().Be(150);
    }

    [Fact]
    public async Task CalculateCraftingTool_binds_when_only_rarity_key_is_present()
    {
        // Companion to the omission regression: the magic-item branch when the model sends only
        // { "rarity": "rare" } and omits marketValue/crafters.
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        var result = await tool.InvokeAsync(ToArgs(new { rarity = "rare" }), CancellationToken.None);
        var json = (JsonElement)result!;
        json.GetProperty("kind").GetString().Should().Be("magic-item");
        json.GetProperty("workweeks").GetInt32().Should().Be(10);
        json.GetProperty("goldCostGp").GetInt32().Should().Be(2000);
    }

    [Fact]
    public async Task CalculateCraftingTool_computes_magic_item_numbers_and_citation()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        var result = await tool.InvokeAsync(
            ToArgs(new { marketValue = (int?)null, rarity = "rare", crafters = (int?)null }),
            CancellationToken.None);
        var json = (JsonElement)result!;
        json.GetProperty("kind").GetString().Should().Be("magic-item");
        json.GetProperty("workweeks").GetInt32().Should().Be(10);
        json.GetProperty("goldCostGp").GetInt32().Should().Be(2000);
        json.GetProperty("citation").GetString().Should().Contain("Crafting Magic Items");
    }

    [Fact]
    public async Task CalculateCraftingTool_guards_on_unknown_rarity()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(11));

        await svc.SendAsync("crafting", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "calculate_crafting");

        var result = await tool.InvokeAsync(
            ToArgs(new { marketValue = (int?)null, rarity = "bogus", crafters = (int?)null }),
            CancellationToken.None);
        var error = ((JsonElement)result!).GetProperty("error").GetString();
        error.Should().Contain("Unknown rarity");
    }

    [Fact]
    public async Task GenerateNpcTool_exposes_no_user_or_campaign_id_and_reaches_the_service()
    {
        var search = Substitute.For<IEntityRetrievalService>();
        search.SearchDiagnosticAsync(Arg.Any<EntitySearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<EntityDiagnosticResult>()); // no hit → not-in-corpus
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(9),
            npcGenerationService: BuildNpcGenerationService(search));

        await svc.SendAsync("npc", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "generate_npc");

        tool.JsonSchema.TryGetProperty("properties", out var props);
        props.TryGetProperty("userId", out _).Should().BeFalse();
        props.TryGetProperty("campaignId", out _).Should().BeFalse();

        var result = await tool.InvokeAsync(
            ToArgs(new { concept = "a shifty dockworker", archetype = "Spy", maxCr = (double?)null }),
            CancellationToken.None);
        var npc = ((JsonElement)result!).Deserialize<GeneratedNpc>(tool.JsonSerializerOptions);
        npc!.ArchetypeInCorpus.Should().BeFalse();            // empty search → not-in-corpus (reached the service)
        npc.AvailableArchetypes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GenerateNpcPartyTool_exposes_a_single_theme_param_and_returns_a_grounded_ensemble()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(9),
            npcGenerationService: BuildNpcGenerationService(EchoNpcSearch()));

        await svc.SendAsync("npc party", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "generate_npc_party");

        tool.JsonSchema.TryGetProperty("properties", out var props);
        props.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("theme");
        props.TryGetProperty("theme", out var themeSchema).Should().BeTrue();
        themeSchema.GetProperty("type").GetString().Should().Be("string");
        props.TryGetProperty("userId", out _).Should().BeFalse();

        var result = await tool.InvokeAsync(
            ToArgs(new { theme = "temple cult" }),
            CancellationToken.None);
        var party = ((JsonElement)result!).Deserialize<GeneratedNpcParty>(tool.JsonSerializerOptions);
        party!.Template.Should().Be("cult");
        party.Members[0].Role.Should().Be("high priest");
        party.Members[0].Npc.StatBlock!.Name.Should().Be("Cult Fanatic");
        party.Members.Should().OnlyContain(m => m.Npc.ArchetypeInCorpus);
    }

    [Fact]
    public async Task PrepSessionTool_reaches_the_service_and_exposes_no_userId()
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));
        await svc.SendAsync("prep", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "prep_session");

        tool.JsonSchema.TryGetProperty("properties", out var props);
        props.TryGetProperty("userId", out _).Should().BeFalse();

        // With a NoOpDbFactory-backed campaign repo, the encounter sub-service's ownership fetch throws
        // (NotSupportedException) — proving prep_session routed campaignId/userId through to PrepForUserAsync.
        var act = () => tool.InvokeAsync(
            ToArgs(new { campaignId = 1L, theme = "Sharn intrigue", difficulty = "Medium", npcArchetype = "Spy" }),
            CancellationToken.None).AsTask();
        await act.Should().ThrowAsync<Exception>();
    }


    public static IEnumerable<object[]> NoReorderOptionalParams() =>
    [
        ["plan_level_up", "targetClass"],
        ["plan_level_up", "considerDip"],
        ["ask_setting_lore", "edition"],
        ["ask_rules", "ruleTopics"],
        ["ask_rules", "edition"],
        ["plan_downtime", "edition"],
        ["generate_npc", "maxCr"],
        ["recommend_build", "targetLevel"],
    ];

    [Theory]
    [MemberData(nameof(NoReorderOptionalParams))]
    public async Task Optional_chat_tool_param_is_not_in_the_schema_required_set(
        string toolName, string paramName)
    {
        // AIFunctionFactory marks a parameter required unless it has a C# default value; a nullable
        // type is NOT enough. These params are documented-optional, so the model must be able to omit
        // them — i.e. they must be absent from the tool schema's `required` array.
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

        await svc.SendAsync("hello", false, CancellationToken.None);

        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == toolName);
        var required = tool.JsonSchema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()).ToArray()
            : Array.Empty<string?>();
        required.Should().NotContain(paramName,
            $"{toolName}.{paramName} is optional and the model must be able to omit it");
    }

    public static IEnumerable<object[]> ReorderOptionalParams() =>
    [
        ["build_encounter", "campaignId"],
        ["build_encounter", "theme"],
        ["build_encounter", "maxCr"],
        ["build_encounter", "minCr"],
        ["prep_session", "difficulty"],
        ["rate_encounter", "campaignId"],
        ["rate_encounter", "partyLevels"],
    ];

    [Theory]
    [MemberData(nameof(ReorderOptionalParams))]
    public async Task Reordered_optional_chat_tool_param_is_not_in_the_schema_required_set(
        string toolName, string paramName)
    {
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

        await svc.SendAsync("hello", false, CancellationToken.None);

        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == toolName);
        var required = tool.JsonSchema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()).ToArray()
            : Array.Empty<string?>();
        required.Should().NotContain(paramName);
    }

    [Fact]
    public async Task BuildEncounterTool_binds_when_the_model_omits_all_optional_params()
    {
        // Regression for the required-param binding bug: the model calls build_encounter with only the
        // required difficulty/edition and OMITS campaignId/theme/maxCr/minCr. Binding must succeed and
        // reach EncounterDesignService's party-resolution guard (proving the tool ran), rather than
        // throwing a MEAI "missing required parameter" binding error.
        var client = new FakeChatClient();
        var svc = CreateService(client, httpContextAccessor: AuthenticatedAs(42));

        await svc.SendAsync("Build it", false, CancellationToken.None);
        var tool = client.LastOptions!.Tools!.OfType<AIFunction>().Single(t => t.Name == "build_encounter");

        var act = () => tool.InvokeAsync(
            ToArgs(new { difficulty = "Hard", edition = "2024" }), CancellationToken.None).AsTask();

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*supply campaignId or partyLevels*");
    }
}