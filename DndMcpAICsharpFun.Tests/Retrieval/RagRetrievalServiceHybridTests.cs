using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;

using Qdrant.Client.Grpc;

using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RagRetrievalServiceHybridTests
{
    private readonly IQdrantSearchClient _qdrant = Substitute.For<IQdrantSearchClient>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private RagRetrievalService BuildSut(bool sparseSupported)
    {
        var opts = new RerankerOptions { Enabled = false };
        var reranker = Substitute.For<IReranker>();
        reranker.Enabled.Returns(false);
        var rerankSvc = new RerankingService(reranker, Options.Create(opts));
        return new(_qdrant, _embedding,
            Options.Create(new QdrantOptions { BlocksCollectionName = "test-col" }),
            Options.Create(new RetrievalOptions { MaxTopK = 20, ScoreThreshold = 0.5f }),
            new QdrantSparseState { SparseSupported = sparseSupported },
            rerankSvc,
            Options.Create(opts));
    }

    private void SetupEmbed() =>
        _embedding.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));

    private static ScoredPoint MakePoint(string text, float score)
    {
        var p = new ScoredPoint { Id = new PointId { Uuid = "uuid-1" }, Score = score };
        p.Payload[QdrantPayloadFields.Text] = text;
        p.Payload[QdrantPayloadFields.SourceBook] = "PHB";
        p.Payload[QdrantPayloadFields.Version] = "Edition2014";
        p.Payload[QdrantPayloadFields.Category] = "Spell";
        p.Payload[QdrantPayloadFields.Chapter] = "Ch11";
        p.Payload[QdrantPayloadFields.PageNumber] = 1L;
        p.Payload[QdrantPayloadFields.ChunkIndex] = 0L;
        return p;
    }

    [Fact]
    public async Task SearchAsync_WhenSparseFalse_CallsSearchAsyncNotQuery()
    {
        SetupEmbed();
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));

        var sut = BuildSut(sparseSupported: false);
        await sut.SearchAsync(new RetrievalQuery("fireball"));

        await _qdrant.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<Filter?>(), Arg.Any<ulong>(),
            Arg.Any<float?>(), Arg.Any<CancellationToken>());
        await _qdrant.DidNotReceive().QueryAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<DomainSparseVector>(), Arg.Any<Filter?>(),
            Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_WhenSparseTrue_CallsQueryAsyncNotSearch()
    {
        SetupEmbed();
        _qdrant.QueryAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<DomainSparseVector>(), Arg.Any<Filter?>(),
                Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));

        var sut = BuildSut(sparseSupported: true);
        await sut.SearchAsync(new RetrievalQuery("fireball"));

        await _qdrant.Received(1).QueryAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<DomainSparseVector>(), Arg.Any<Filter?>(),
            Arg.Any<ulong>(), Arg.Any<CancellationToken>());
        await _qdrant.DidNotReceive().SearchAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<Filter?>(), Arg.Any<ulong>(),
            Arg.Any<float?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_HybridPath_ResultMappingUnchanged()
    {
        SetupEmbed();
        var point = MakePoint("Fireball is a 3rd level spell.", 0.85f);
        _qdrant.QueryAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<DomainSparseVector>(), Arg.Any<Filter?>(),
                Arg.Any<ulong>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([point]));

        var sut = BuildSut(sparseSupported: true);
        var results = await sut.SearchAsync(new RetrievalQuery("fireball"));

        Assert.Single(results);
        Assert.Equal("Fireball is a 3rd level spell.", results[0].Text);
        Assert.Equal(0.85f, results[0].Score);
        Assert.Equal("PHB", results[0].Metadata.SourceBook);
    }
}