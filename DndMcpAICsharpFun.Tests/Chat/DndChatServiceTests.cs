using System.Security.Claims;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.Rules;
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

    /// <summary>
    /// Builds a real <see cref="NpcGenerationService"/> over a substitute <see cref="IEntityRetrievalService"/>
    /// — sealed/concrete, so it cannot be substituted directly, but its own retrieval dependency can, keeping
    /// these chat-wiring tests DB-free. Not ownership-gated, like <see cref="BuildRulesAdjudicationService"/>.
    /// </summary>
    private static NpcGenerationService BuildNpcGenerationService(IEntityRetrievalService? search = null) =>
        new(search ?? Substitute.For<IEntityRetrievalService>());

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
        NpcGenerationService? npcGenerationService = null) =>
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
            npcGenerationService ?? BuildNpcGenerationService());

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
        toolNames.Should().NotContain("recommend_build");
        toolNames.Should().NotContain("critique_build");
        toolNames.Should().NotContain("ask_setting_lore");
        toolNames.Should().NotContain("ask_rules");
        toolNames.Should().NotContain("generate_npc");
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
        toolNames.Should().Contain("generate_npc");
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
                or "critique_build" or "ask_setting_lore" or "ask_rules" or "generate_npc");
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
}