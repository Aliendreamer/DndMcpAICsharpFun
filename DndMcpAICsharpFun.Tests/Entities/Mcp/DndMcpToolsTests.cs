using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Mcp;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Mcp;

file sealed class FakeRagService : IRagRetrievalService
{
    public IList<RetrievalResult> Results { get; set; } = [];
    public Task<IList<RetrievalResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default)
        => Task.FromResult(Results);
    public Task<IList<RetrievalDiagnosticResult>> SearchDiagnosticAsync(RetrievalQuery query, CancellationToken ct = default)
        => Task.FromResult<IList<RetrievalDiagnosticResult>>([]);
}

file sealed class FakeEntityService : IEntityRetrievalService
{
    public EntityFullResult? GetResult { get; set; }
    public IList<EntitySearchResult> SearchResults { get; set; } = [];
    public Task<EntityFullResult?> GetByIdAsync(string id, CancellationToken ct) => Task.FromResult(GetResult);
    public Task<IList<EntitySearchResult>> SearchAsync(EntitySearchQuery query, CancellationToken ct) => Task.FromResult(SearchResults);
    public Task<IList<EntityDiagnosticResult>> SearchDiagnosticAsync(EntitySearchQuery query, CancellationToken ct) => Task.FromResult<IList<EntityDiagnosticResult>>([]);
}

file sealed class FakeFusedService : IFusedRetrievalService
{
    public IReadOnlyList<FusedCandidate> FusedResults { get; set; } = [];

    public Task<IReadOnlyList<FusedCandidate>> SearchAsync(string query, int topK, CancellationToken ct = default)
        => Task.FromResult(FusedResults);
}

public class DndMcpToolsTests
{
    private static ChunkMetadata Metadata(string sourceBook = "PHB") =>
        new(sourceBook, DndVersion.Edition2014, ContentCategory.Spell, null, "Spells", 150, 0);

    private static DndMcpTools MakeTools(
        IRagRetrievalService? rag = null,
        IEntityRetrievalService? entity = null,
        IFusedRetrievalService? fused = null) =>
        new(rag ?? new FakeRagService(), entity ?? new FakeEntityService(), fused ?? new FakeFusedService());

    [Fact]
    public async Task search_lore_returns_json_with_results()
    {
        var fakeRag = new FakeRagService
        {
            Results = [new RetrievalResult("Fireball deals 8d6 fire damage.", Metadata(), 0.95f)]
        };
        var tools = MakeTools(rag: fakeRag);

        var result = await tools.search_lore("fireball");

        result.Should().Contain("Fireball");
        result.Should().Contain("PHB");
    }

    [Fact]
    public async Task search_lore_with_no_results_returns_message()
    {
        var tools = MakeTools();

        var result = await tools.search_lore("xyzzy");

        result.Should().Be("No lore results found.");
    }

    [Fact]
    public async Task search_lore_with_unknown_version_returns_gracefully()
    {
        var tools = MakeTools();

        var result = await tools.search_lore("fireball", version: "Edition9999");

        result.Should().Be("No lore results found.");
    }

    [Fact]
    public async Task search_entities_returns_json_with_results()
    {
        var fakeEntity = new FakeEntityService
        {
            SearchResults =
            [
                new EntitySearchResult("phb.spell.fireball", EntityType.Spell, "Fireball",
                    "PHB", "Edition2014", null, [], "8d6 fire damage", 0.97f)
            ]
        };
        var tools = MakeTools(entity: fakeEntity);

        var result = await tools.search_entities("fireball");

        result.Should().Contain("phb.spell.fireball");
        result.Should().Contain("Fireball");
        result.Should().Contain("Spell");
    }

    [Fact]
    public async Task search_entities_with_no_results_returns_message()
    {
        var tools = MakeTools();

        var result = await tools.search_entities("xyzzy");

        result.Should().Be("No entities found.");
    }

    [Fact]
    public async Task search_entities_with_unknown_type_returns_gracefully()
    {
        var tools = MakeTools();

        var result = await tools.search_entities("fireball", type: "NotARealType");

        result.Should().Be("No entities found.");
    }

    [Fact]
    public async Task get_entity_returns_json_for_known_id()
    {
        var envelope = new EntityEnvelope(
            Id: "phb.spell.fireball",
            Type: EntityType.Spell,
            Name: "Fireball",
            SourceBook: "PHB",
            Edition: "Edition2014",
            Page: 241,
            FirstAppearedIn: new FirstAppearance("PHB", "Edition2014"),
            RevisedIn: [],
            SettingTags: [],
            CanonicalText: "A bright streak flashes from your pointing finger...",
            Fields: default);

        var fakeEntity = new FakeEntityService { GetResult = new EntityFullResult(envelope) };
        var tools = MakeTools(entity: fakeEntity);

        var result = await tools.get_entity("phb.spell.fireball");

        result.Should().Contain("phb.spell.fireball");
        result.Should().Contain("Fireball");
        result.Should().Contain("A bright streak");
    }

    [Fact]
    public async Task get_entity_returns_not_found_message_for_unknown_id()
    {
        var tools = MakeTools();

        var result = await tools.get_entity("fake.id.does-not-exist");

        result.Should().Be("Entity not found: fake.id.does-not-exist");
    }

    // ── search_dnd (Task 5.3) ────────────────────────────────────────────────

    [Fact]
    public async Task search_dnd_returns_mixed_source_tagged_results()
    {
        var fakeFused = new FakeFusedService
        {
            FusedResults = new[]
            {
                new FusedCandidate("entity", "phb.spell.fireball", "Fireball",
                    "3rd-level evocation.", 0.92),
                new FusedCandidate("prose", "uuid-prose-1", "Evocation Spells",
                    "Fireball deals 8d6 fire damage.", 0.85),
            }
        };
        var tools = MakeTools(fused: (IFusedRetrievalService)fakeFused);

        var result = await tools.search_dnd("fireball");

        result.Should().Contain("\"source\":\"entity\"");
        result.Should().Contain("\"source\":\"prose\"");
        result.Should().Contain("phb.spell.fireball");
        result.Should().Contain("Fireball");
    }

    [Fact]
    public async Task search_dnd_with_no_results_returns_message()
    {
        var tools = MakeTools();

        var result = await tools.search_dnd("xyzzy-nonexistent");

        result.Should().Be("No results found.");
    }

    [Fact]
    public async Task search_dnd_empty_query_returns_error()
    {
        var tools = MakeTools();

        var result = await tools.search_dnd("");

        result.Should().Be("Error: query must not be empty.");
    }
}
