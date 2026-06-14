using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using FluentAssertions;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class FusedRetrievalServiceTests
{
    private readonly IQdrantSearchClient _qdrant = Substitute.For<IQdrantSearchClient>();
    private readonly IEntityVectorStore _entityStore = Substitute.For<IEntityVectorStore>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private void SetupEmbed() =>
        _embedding.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));

    private static ScoredPoint MakePoint(string text, string title, float score, string uuid)
    {
        var p = new ScoredPoint { Id = new PointId { Uuid = uuid }, Score = score };
        p.Payload[QdrantPayloadFields.Text] = text;
        p.Payload[QdrantPayloadFields.SourceBook] = "PHB";
        p.Payload[QdrantPayloadFields.Version] = "Edition2014";
        p.Payload[QdrantPayloadFields.Category] = "Spell";
        p.Payload[QdrantPayloadFields.Chapter] = title;
        p.Payload[QdrantPayloadFields.PageNumber] = 1L;
        p.Payload[QdrantPayloadFields.ChunkIndex] = 0L;
        return p;
    }

    private static EntitySearchHit MakeEntityHit(string id, string name, string canonicalText, float score)
    {
        var envelope = new EntityEnvelope(
            id, EntityType.Spell, name, "PHB", "Edition2014",
            null, new FirstAppearance("PHB", "Edition2014"), [],
            [], canonicalText, default);
        return new EntitySearchHit(envelope, score, id + "-pt");
    }

    private FusedRetrievalService BuildSut(IReranker reranker, bool globalEnabled = true)
    {
        var rerankOpts = new RerankerOptions
        {
            Enabled = globalEnabled,
            CandidatePoolSize = 20,
            RerankBlocks = true,
            RerankEntities = true,
        };
        var rerankSvc = new RerankingService(reranker, Options.Create(rerankOpts));
        return new FusedRetrievalService(
            _embedding, _qdrant, _entityStore,
            rerankSvc,
            Options.Create(new QdrantOptions
            {
                BlocksCollectionName = "dnd_blocks",
                EntitiesCollectionName = "dnd_entities"
            }),
            Options.Create(new RetrievalOptions { MaxTopK = 50, ScoreThreshold = 0.0f }),
            Options.Create(rerankOpts),
            new QdrantSparseState { SparseSupported = false });
    }

    // ── Task 5.1 / Spec: "Fused result mixes both sources, jointly ranked" ────

    [Fact]
    public async Task SearchAsync_ReturnsResultsFromBothSourcesJointlyReranked()
    {
        SetupEmbed();

        // Prose: 1 point
        var prosePoint = MakePoint("Fireball is a 3rd level spell.", "Evocation Spells", 0.8f, "prose-uuid");
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([prosePoint]));

        // Entity: 1 hit
        var entityHit = MakeEntityHit("phb.spell.fireball", "Fireball", "3rd-level evocation spell.", 0.7f);
        _entityStore.SearchAsync(
                Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<EntitySearchHit>>([entityHit]));

        // Reranker: entity scores higher than prose
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.4f, 0.9f })); // prose=0.4, entity=0.9

        var sut = BuildSut(mockReranker);

        var results = await sut.SearchAsync("fireball", topK: 2, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Source.Should().Be("entity");  // highest rerank score
        results[1].Source.Should().Be("prose");
    }

    // ── Task 5.1 / Spec: "Each fused result is source-tagged" ────────────────

    [Fact]
    public async Task SearchAsync_AllResultsHaveSourceTag()
    {
        SetupEmbed();

        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(
            [
                MakePoint("Rule text A.", "Rules", 0.8f, "p1"),
                MakePoint("Rule text B.", "Combat", 0.7f, "p2"),
            ]));

        _entityStore.SearchAsync(
                Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<EntitySearchHit>>(
            [
                MakeEntityHit("ent-1", "Entity One", "canonical text one", 0.6f),
            ]));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.8f, 0.7f, 0.6f }));

        var sut = BuildSut(mockReranker);

        var results = await sut.SearchAsync("query", topK: 5, CancellationToken.None);

        results.Should().OnlyContain(r => r.Source == "prose" || r.Source == "entity");
        results.Where(r => r.Source == "prose").Should().HaveCount(2);
        results.Where(r => r.Source == "entity").Should().HaveCount(1);
    }

    // ── Task 5.1 / Spec: "Fused result respects top-K" ──────────────────────

    [Fact]
    public async Task SearchAsync_RespectsTopK()
    {
        SetupEmbed();

        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(
                Enumerable.Range(1, 5).Select(i => MakePoint($"prose {i}", "Ch", 0.5f, $"p{i}")).ToList()));

        _entityStore.SearchAsync(
                Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<EntitySearchHit>>(
                Enumerable.Range(1, 5).Select(i => MakeEntityHit($"e{i}", $"E{i}", $"entity text {i}", 0.5f)).ToList()));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        // Return all 10 scored uniformly — truncation to topK is done by RerankingService
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var passages = ci.Arg<IReadOnlyList<string>>();
                return Task.FromResult(Enumerable.Range(0, passages.Count)
                    .Select(i => (float)(passages.Count - i) / passages.Count)
                    .ToArray());
            });

        var sut = BuildSut(mockReranker);

        var results = await sut.SearchAsync("test", topK: 3, CancellationToken.None);

        results.Should().HaveCount(3);
    }

    // ── Embedding called only once (single embed) ─────────────────────────────

    [Fact]
    public async Task SearchAsync_EmbedQueryOnce()
    {
        SetupEmbed();

        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));
        _entityStore.SearchAsync(
                Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<EntitySearchHit>>([]));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var sut = BuildSut(mockReranker);

        await sut.SearchAsync("fireball", topK: 5, CancellationToken.None);

        // Embedding called exactly once for the query
        await _embedding.Received(1).EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>());
    }
}
