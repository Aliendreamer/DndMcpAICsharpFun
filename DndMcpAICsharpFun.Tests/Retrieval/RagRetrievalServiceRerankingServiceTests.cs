using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;

using FluentAssertions;

using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Tests.Retrieval;

/// <summary>
/// Regression tests that pin the current prose selection behaviour after
/// <see cref="RagRetrievalService"/> is refactored to use <see cref="RerankingService"/>.
/// These tests must stay green before and after the refactor (Task 4.1 / 4.2).
/// </summary>
public sealed class RagRetrievalServiceRerankingServiceTests
{
    private readonly IQdrantSearchClient _qdrant = Substitute.For<IQdrantSearchClient>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private void SetupEmbed() =>
        _embedding.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));

    private static ScoredPoint MakePoint(string text, float score, string uuid)
    {
        var p = new ScoredPoint { Id = new PointId { Uuid = uuid }, Score = score };
        p.Payload[QdrantPayloadFields.Text] = text;
        p.Payload[QdrantPayloadFields.SourceBook] = "PHB";
        p.Payload[QdrantPayloadFields.Version] = "Edition2014";
        p.Payload[QdrantPayloadFields.Category] = "Spell";
        p.Payload[QdrantPayloadFields.Chapter] = "Ch1";
        p.Payload[QdrantPayloadFields.PageNumber] = 1L;
        p.Payload[QdrantPayloadFields.ChunkIndex] = 0L;
        return p;
    }

    private RagRetrievalService BuildSut(IReranker reranker, bool rerankBlocks = true, int maxTopK = 20)
    {
        var rerankOpts = new RerankerOptions
        {
            Enabled = reranker.Enabled,
            RerankBlocks = rerankBlocks,
            CandidatePoolSize = 20,
        };
        var rerankSvc = new RerankingService(reranker, Options.Create(rerankOpts));
        return new RagRetrievalService(
            _qdrant, _embedding,
            Options.Create(new QdrantOptions { BlocksCollectionName = "test-col" }),
            Options.Create(new RetrievalOptions { MaxTopK = maxTopK, ScoreThreshold = 0.5f }),
            new QdrantSparseState { SparseSupported = false },
            rerankSvc,
            Options.Create(rerankOpts));
    }

    // ── Task 4.1 regression: disabled reranker → first topK in vector order ──

    [Fact]
    public async Task Regression_DisabledReranker_ReturnsFirstTopKInVectorOrder()
    {
        SetupEmbed();
        var points = Enumerable.Range(1, 5)
            .Select(i => MakePoint($"text-{i}", 0.9f - i * 0.1f, $"uuid-{i}"))
            .ToList();

        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(points));

        // Disabled reranker: IReranker.Enabled = false
        var disabledReranker = Substitute.For<IReranker>();
        disabledReranker.Enabled.Returns(false);

        var sut = BuildSut(disabledReranker);

        var results = await sut.SearchAsync(new RetrievalQuery("fireball", TopK: 3));

        results.Should().HaveCount(3);
        results[0].Text.Should().Be("text-1");
        results[1].Text.Should().Be("text-2");
        results[2].Text.Should().Be("text-3");

        // Reranker model must not be called
        await disabledReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // ── Task 4.1 regression: enabled reranker → selection driven by scores ───

    [Fact]
    public async Task Regression_EnabledReranker_SelectsTopNByScore()
    {
        SetupEmbed();
        var points = new[]
        {
            MakePoint("low-relevance",  0.8f, "uuid-1"),
            MakePoint("high-relevance", 0.8f, "uuid-2"),
            MakePoint("mid-relevance",  0.8f, "uuid-3"),
        };
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(points));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        // scores: low=0.1, high=0.9, mid=0.5  → expected order: high, mid
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.1f, 0.9f, 0.5f }));

        var sut = BuildSut(mockReranker);

        var results = await sut.SearchAsync(new RetrievalQuery("fireball", TopK: 2));

        results.Should().HaveCount(2);
        results[0].Text.Should().Be("high-relevance");
        results[1].Text.Should().Be("mid-relevance");
    }

    // ── RerankBlocks=false → vector order, no reranker call ──────────────────

    [Fact]
    public async Task RerankBlocksFalse_SkipsReranking()
    {
        SetupEmbed();
        var points = new[]
        {
            MakePoint("vec-first",  0.9f, "uuid-1"),
            MakePoint("vec-second", 0.8f, "uuid-2"),
        };
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>(points));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        // RerankBlocks=false → no rerank
        var opts = new RerankerOptions { Enabled = true, RerankBlocks = false, CandidatePoolSize = 20 };
        var rerankSvc = new RerankingService(mockReranker, Options.Create(opts));
        var sut = new RagRetrievalService(
            _qdrant, _embedding,
            Options.Create(new QdrantOptions { BlocksCollectionName = "test-col" }),
            Options.Create(new RetrievalOptions { MaxTopK = 20, ScoreThreshold = 0.5f }),
            new QdrantSparseState { SparseSupported = false },
            rerankSvc,
            Options.Create(opts));

        var results = await sut.SearchAsync(new RetrievalQuery("fireball", TopK: 2));

        results.Should().HaveCount(2);
        results[0].Text.Should().Be("vec-first");
        results[1].Text.Should().Be("vec-second");

        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }
}