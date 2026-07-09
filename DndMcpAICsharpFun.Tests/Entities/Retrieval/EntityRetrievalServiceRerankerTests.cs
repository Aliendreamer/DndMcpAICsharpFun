using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Retrieval;

public sealed class EntityRetrievalServiceRerankerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static EntityEnvelope MakeEnvelope(string id, string canonicalText) =>
        new(id, EntityType.Spell, id, "PHB", "Edition2014",
            null, new FirstAppearance("PHB", "Edition2014"), [],
            [], canonicalText, default);

    private static EntitySearchHit MakeHit(string id, string canonicalText, float score) =>
        new(MakeEnvelope(id, canonicalText), score, id + "-point");

    private static IEmbeddingService MakeEmbedding()
    {
        var emb = Substitute.For<IEmbeddingService>();
        emb.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));
        return emb;
    }

    private static EntityRetrievalService BuildSut(
        IEntityVectorStore store,
        IReranker reranker,
        bool globalEnabled = true,
        bool rerankEntities = true,
        int candidatePoolSize = 20,
        int maxTopK = 50)
    {
        var rerankOpts = new RerankerOptions
        {
            Enabled = globalEnabled,
            RerankEntities = rerankEntities,
            CandidatePoolSize = candidatePoolSize,
        };
        var rerankSvc = new RerankingService(reranker, Options.Create(rerankOpts));
        return new EntityRetrievalService(
            MakeEmbedding(),
            store,
            Options.Create(new RetrievalOptions { MaxTopK = maxTopK }),
            rerankSvc,
            Options.Create(rerankOpts));
    }

    // ── Task 3.1 / Spec: "Entity search returns reranked results" ────────────

    [Fact]
    public async Task SearchAsync_WhenRerankEntitiesEnabled_OverFetchesPoolAndReranks()
    {
        // Arrange: store returns 5 hits; reranker scores reverse the order
        var store = Substitute.For<IEntityVectorStore>();
        var hits = new[]
        {
            MakeHit("e-low",  "lowest score text",  0.1f),
            MakeHit("e-high", "highest score text", 0.9f),
            MakeHit("e-mid",  "mid score text",     0.5f),
        };
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        // Score: e-low=0.95, e-high=0.1, e-mid=0.5 → reranked: e-low, e-mid, e-high
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.95f, 0.1f, 0.5f }));

        var sut = BuildSut(store, mockReranker, globalEnabled: true, rerankEntities: true, candidatePoolSize: 10);
        var query = new EntitySearchQuery("fireball", null, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        // Act
        var results = await sut.SearchAsync(query, CancellationToken.None);

        // Assert: over-fetched pool (10), reranked, top 2 returned
        await store.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<EntityFilters>(), 10, Arg.Any<CancellationToken>());

        results.Should().HaveCount(2);
        results[0].Id.Should().Be("e-low");   // highest rerank score
        results[1].Id.Should().Be("e-mid");   // second
    }

    // ── Task 3.1 / Spec: "Entity reranking can be disabled" ──────────────────

    [Fact]
    public async Task SearchAsync_WhenRerankEntitiesFalse_FetchesTopKAndSkipsRerank()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var hits = new[]
        {
            MakeHit("vec-first",  "alpha", 0.9f),
            MakeHit("vec-second", "beta",  0.7f),
            MakeHit("vec-third",  "gamma", 0.5f),
        };
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var sut = BuildSut(store, mockReranker, globalEnabled: true, rerankEntities: false, candidatePoolSize: 20);
        var query = new EntitySearchQuery("fireball", null, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        var results = await sut.SearchAsync(query, CancellationToken.None);

        // fetched exactly topK=2, not pool size
        await store.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<EntityFilters>(), 2, Arg.Any<CancellationToken>());

        // reranker NOT called
        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

        // vector order preserved
        results.Should().HaveCount(2);
        results[0].Id.Should().Be("vec-first");
        results[1].Id.Should().Be("vec-second");
    }

    // ── Task 2.1 / Spec: "Global kill-switch overrides channel flags" ─────────

    [Fact]
    public async Task SearchAsync_WhenGlobalEnabledFalse_SkipsRerankEvenIfRerankEntitiesTrue()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var hits = new[]
        {
            MakeHit("a", "text-a", 0.9f),
            MakeHit("b", "text-b", 0.7f),
        };
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var sut = BuildSut(store, mockReranker, globalEnabled: false, rerankEntities: true, candidatePoolSize: 20);
        var query = new EntitySearchQuery("test", null, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        await sut.SearchAsync(query, CancellationToken.None);

        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // ── Task 2.1 / Spec: "Candidate pool size is honored" ────────────────────

    [Fact]
    public async Task SearchAsync_WhenRerankEnabled_FetchesCandidatePoolSizeNotTopK()
    {
        var store = Substitute.For<IEntityVectorStore>();
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>([]));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        // CandidatePoolSize=30 but TopK=5 — store should be called with 30
        var sut = BuildSut(store, mockReranker, globalEnabled: true, rerankEntities: true, candidatePoolSize: 30);
        var query = new EntitySearchQuery("test", null, null, null, null, null, null,
            null, null, null, null, TopK: 5);

        await sut.SearchAsync(query, CancellationToken.None);

        await store.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<EntityFilters>(), 30, Arg.Any<CancellationToken>());
    }
}