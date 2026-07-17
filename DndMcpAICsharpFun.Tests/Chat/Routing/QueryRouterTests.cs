using DndMcpAICsharpFun.Features.Chat.Routing;
using DndMcpAICsharpFun.Features.Embedding;

using FluentAssertions;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Chat.Routing;

public sealed class QueryRouterTests
{
    // ── QuerySignals (pure) ──────────────────────────────────────────────────

    [Theory]
    [InlineData("what is my breath weapon", ToolGroups.CharacterResolution)]
    [InlineData("can I cast counterspell", ToolGroups.CharacterResolution)]
    [InlineData("what do I get at level 8", ToolGroups.CharacterResolution)]
    [InlineData("list all CR 5 flying monsters", ToolGroups.StructuredLookup)]
    [InlineData("how many legendary actions does a beholder have", ToolGroups.StructuredLookup)]
    [InlineData("generate an NPC innkeeper", ToolGroups.Generation)]
    [InlineData("prep a session for tonight", ToolGroups.Generation)]
    public void Signals_detect_unambiguous_intent(string query, string expected) =>
        QuerySignals.Detect(query).Should().Be(expected);

    [Theory]
    [InlineData("describe the city of Waterdeep")] // no signal → embedding backstop
    [InlineData("list all my spells")]             // ambiguous: structured + resolution → defer
    [InlineData("")]
    public void Signals_return_null_when_ambiguous_or_absent(string query) =>
        QuerySignals.Detect(query).Should().BeNull();

    // ── QueryRouter (fakes) ──────────────────────────────────────────────────

    private sealed class FakeEmbedding(Func<string, float[]> map) : IEmbeddingService
    {
        public Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IList<float[]>>(texts.Select(map).ToList());
    }

    private sealed class FakeIndex(string? group, double confidence) : IExemplarIndex
    {
        public Task<(string? Group, double Confidence)> ClassifyAsync(float[] q, CancellationToken ct) =>
            Task.FromResult((group, confidence));
    }

    private static AITool Tool(string name) => AIFunctionFactory.Create(() => "", name: name);

    private static readonly AITool[] AllTools =
    [
        Tool("search_lore"), Tool("search_entities"), Tool("get_entity"),
        Tool("resolve_character_feature"), Tool("calculate_crafting"), Tool("generate_npc"),
        Tool("custom_tool"), // unmapped
    ];

    private static QueryRouter Build(IExemplarIndex index, QueryRouterOptions? opts = null) =>
        new(new FakeEmbedding(_ => [0f]), index,
            Options.Create(opts ?? new QueryRouterOptions()),
            NullLogger<QueryRouter>.Instance);

    private static IReadOnlyList<string> Names(IReadOnlyList<AITool> tools) =>
        tools.OfType<AIFunction>().Select(f => f.Name).ToList();

    [Fact]
    public async Task Signal_query_narrows_to_its_group_plus_safe_core()
    {
        var router = Build(new FakeIndex(null, 0)); // signal path — index unused
        var offered = await router.RouteAsync("what is my breath weapon", AllTools, CancellationToken.None);

        Names(offered).Should().Contain("resolve_character_feature")
            .And.Contain("search_lore")   // always-safe core
            .And.Contain("custom_tool")   // unmapped → always offered
            .And.NotContain("search_entities")
            .And.NotContain("generate_npc")
            .And.NotContain("calculate_crafting");
    }

    [Fact]
    public async Task Embedding_verdict_narrows_to_its_group()
    {
        // No deterministic signal fires → embedding path; the fake index returns a confident group.
        var router = Build(new FakeIndex(ToolGroups.StructuredLookup, 0.9));
        var offered = await router.RouteAsync("tell me about the realms of faerun", AllTools, CancellationToken.None);

        Names(offered).Should().Contain("search_entities").And.Contain("get_entity")
            .And.Contain("search_lore") // safe core
            .And.NotContain("resolve_character_feature");
    }

    [Fact]
    public async Task Low_confidence_falls_back_to_the_full_tool_set()
    {
        var router = Build(new FakeIndex(ToolGroups.Generation, 0.2)); // below default 0.45
        var offered = await router.RouteAsync("some vague thing", AllTools, CancellationToken.None);

        Names(offered).Should().BeEquivalentTo(Names(AllTools));
    }

    [Fact]
    public async Task Disabled_router_is_a_no_op()
    {
        var router = Build(new FakeIndex(ToolGroups.Generation, 0.99),
            new QueryRouterOptions { Enabled = false });
        var offered = await router.RouteAsync("generate an NPC", AllTools, CancellationToken.None);

        Names(offered).Should().BeEquivalentTo(Names(AllTools));
    }

    [Fact]
    public async Task Every_narrowed_set_includes_the_always_safe_core_and_unmapped_tools()
    {
        var router = Build(new FakeIndex(null, 0));
        // A generation signal → offered set is generation group; safe core + unmapped must remain.
        var offered = await router.RouteAsync("generate an NPC villain", AllTools, CancellationToken.None);

        Names(offered).Should().Contain("generate_npc")
            .And.Contain("search_lore")   // safe core even though it's a different group
            .And.Contain("custom_tool");
    }

    // ── ExemplarIndex (real, with a scoped fake embedding) ───────────────────

    [Fact]
    public async Task ExemplarIndex_picks_the_argmax_cosine_group()
    {
        static float[] Map(string t) =>
            t.Contains("lore", StringComparison.OrdinalIgnoreCase) ? [1f, 0f]
            : t.Contains("list", StringComparison.OrdinalIgnoreCase) ? [0f, 1f]
            : [0.5f, 0.5f];

        var services = new ServiceCollection();
        services.AddScoped<IEmbeddingService>(_ => new FakeEmbedding(Map));
        using var sp = services.BuildServiceProvider();

        var opts = Options.Create(new QueryRouterOptions
        {
            Exemplars = new()
            {
                [ToolGroups.RetrievalLore] = ["lore"],
                [ToolGroups.StructuredLookup] = ["list"],
            },
        });
        var index = new ExemplarIndex(sp.GetRequiredService<IServiceScopeFactory>(), opts);

        var (loreGroup, loreCos) = await index.ClassifyAsync(Map("lore query"), CancellationToken.None);
        loreGroup.Should().Be(ToolGroups.RetrievalLore);
        loreCos.Should().BeApproximately(1.0, 1e-6);

        var (listGroup, _) = await index.ClassifyAsync(Map("list query"), CancellationToken.None);
        listGroup.Should().Be(ToolGroups.StructuredLookup);
    }
}
