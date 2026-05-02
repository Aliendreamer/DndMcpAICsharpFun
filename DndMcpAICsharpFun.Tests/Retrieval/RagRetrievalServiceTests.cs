using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using Qdrant.Client.Grpc;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RagRetrievalServiceTests
{
    private readonly IQdrantSearchClient _qdrant = Substitute.For<IQdrantSearchClient>();
    private readonly IEmbeddingService _embedding = Substitute.For<IEmbeddingService>();

    private RagRetrievalService BuildSut(int maxTopK = 20, float scoreThreshold = 0.5f) =>
        new(_qdrant, _embedding,
            Options.Create(new QdrantOptions { BlocksCollectionName = "test-col" }),
            Options.Create(new RetrievalOptions { MaxTopK = maxTopK, ScoreThreshold = scoreThreshold }));

    private void SetupEmbed() =>
        _embedding.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));

    private static ScoredPoint MakePoint(string text, float score, string uuid = "uuid-1")
    {
        var p = new ScoredPoint { Id = new PointId { Uuid = uuid }, Score = score };
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
    public async Task SearchAsync_MapsScoredPointsToRetrievalResults()
    {
        SetupEmbed();
        var point = MakePoint("A bright streak flashes...", 0.9f);
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([point]));
        var sut = BuildSut();

        var results = await sut.SearchAsync(new RetrievalQuery("fireball"));

        Assert.Single(results);
        Assert.Equal("A bright streak flashes...", results[0].Text);
        Assert.Equal(0.9f, results[0].Score);
        Assert.Equal("PHB", results[0].Metadata.SourceBook);
        Assert.Equal(DndVersion.Edition2014, results[0].Metadata.Version);
    }

    [Fact]
    public async Task SearchAsync_CapsTopKAtMaxTopK()
    {
        SetupEmbed();
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));
        var sut = BuildSut(maxTopK: 5);

        await sut.SearchAsync(new RetrievalQuery("fireball", TopK: 100));

        await _qdrant.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Any<Filter?>(), 5ul,
            Arg.Any<float?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchDiagnosticAsync_IncludesPointId()
    {
        SetupEmbed();
        var point = MakePoint("spell text", 0.8f, "diag-uuid");
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([point]));
        var sut = BuildSut();

        var results = await sut.SearchDiagnosticAsync(new RetrievalQuery("test"));

        Assert.Single(results);
        Assert.Equal("diag-uuid", results[0].PointId);
        Assert.Equal("spell text", results[0].Text);
    }

    [Fact]
    public async Task SearchAsync_WithFilters_PassesNonNullFilter()
    {
        SetupEmbed();
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));
        var sut = BuildSut();

        await sut.SearchAsync(new RetrievalQuery("fireball",
            Version: DndVersion.Edition2014,
            Category: ContentCategory.Spell,
            SourceBook: "PHB",
            EntityName: "Fireball"));

        await _qdrant.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            Arg.Is<Filter?>(f => f != null && f.Must.Count == 4),
            Arg.Any<ulong>(),
            Arg.Any<float?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchAsync_NoFilter_WhenQueryHasNoConstraints()
    {
        SetupEmbed();
        _qdrant.SearchAsync(
                Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
                Arg.Any<Filter?>(), Arg.Any<ulong>(),
                Arg.Any<float?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredPoint>>([]));
        var sut = BuildSut();

        await sut.SearchAsync(new RetrievalQuery("fireball"));

        await _qdrant.Received(1).SearchAsync(
            Arg.Any<string>(), Arg.Any<ReadOnlyMemory<float>>(),
            null, Arg.Any<ulong>(),
            Arg.Any<float?>(), Arg.Any<CancellationToken>());
    }
}
