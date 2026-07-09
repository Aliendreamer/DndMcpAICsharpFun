using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Qdrant.Client.Grpc;
using Xunit;
using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class Tier1EmbeddingGroundingTests
{
    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f }).ToList());
    }

    private sealed class RecordingQdrantSearchClient : IQdrantSearchClient
    {
        public string? CapturedCollection { get; private set; }
        public Filter? CapturedFilter { get; private set; }
        public IReadOnlyList<ScoredPoint> Result { get; set; } = [];

        public Task<IReadOnlyList<ScoredPoint>> SearchAsync(
            string collectionName,
            ReadOnlyMemory<float> vector,
            Filter? filter = null,
            ulong limit = 10,
            float? scoreThreshold = null,
            CancellationToken cancellationToken = default)
        {
            CapturedCollection = collectionName;
            CapturedFilter = filter;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<ScoredPoint>> QueryAsync(
            string collectionName,
            ReadOnlyMemory<float> denseVector,
            DomainSparseVector sparseVector,
            Filter? filter = null,
            ulong limit = 10,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Tier1EmbeddingGrounding does not use hybrid query.");
    }

    private static ScoredPoint MakePoint(float score) => new() { Score = score };

    private static Tier1EmbeddingGrounding BuildSut(
        RecordingQdrantSearchClient qdrant, double floor = 0.5, int pageWindow = 2) =>
        new(new FakeEmbeddingService(),
            qdrant,
            Options.Create(new QdrantOptions { BlocksCollectionName = "dnd_blocks" }),
            Options.Create(new GroundingOptions { SimilarityFloor = floor, PageWindow = pageWindow }));

    [Fact]
    public async Task ScoreBelowFloor_ReturnsBelowFloorTrue_WithScoreEchoed()
    {
        var qdrant = new RecordingQdrantSearchClient { Result = [MakePoint(0.2f)] };
        var sut = BuildSut(qdrant, floor: 0.5);

        var result = await sut.GroundAsync("some entity text", "PHB", 10, CancellationToken.None);

        result.BelowFloor.Should().BeTrue();
        result.Score.Should().BeApproximately(0.2, 1e-6);
    }

    [Fact]
    public async Task ScoreAtOrAboveFloor_ReturnsBelowFloorFalse()
    {
        var qdrant = new RecordingQdrantSearchClient { Result = [MakePoint(0.5f)] };
        var sut = BuildSut(qdrant, floor: 0.5);

        var result = await sut.GroundAsync("some entity text", "PHB", 10, CancellationToken.None);

        result.BelowFloor.Should().BeFalse();
        result.Score.Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public async Task NoHits_TreatsScoreAsZero_BelowFloorTrue()
    {
        var qdrant = new RecordingQdrantSearchClient { Result = [] };
        var sut = BuildSut(qdrant, floor: 0.5);

        var result = await sut.GroundAsync("some entity text", "PHB", 10, CancellationToken.None);

        result.BelowFloor.Should().BeTrue();
        result.Score.Should().Be(0);
    }

    [Fact]
    public async Task Filter_RestrictsToSourceBookAndPageWindow()
    {
        var qdrant = new RecordingQdrantSearchClient { Result = [MakePoint(0.9f)] };
        var sut = BuildSut(qdrant, pageWindow: 2);

        await sut.GroundAsync("some entity text", "PHB", 10, CancellationToken.None);

        qdrant.CapturedCollection.Should().Be("dnd_blocks");
        var filter = qdrant.CapturedFilter;
        filter.Should().NotBeNull();
        filter!.Must.Should().HaveCount(2);

        var bookCondition = filter.Must.Single(c => c.Field.Key == QdrantPayloadFields.SourceBook);
        bookCondition.Field.Match.Keyword.Should().Be("PHB");

        var pageCondition = filter.Must.Single(c => c.Field.Key == QdrantPayloadFields.PageNumber);
        pageCondition.Field.Range.Gte.Should().Be(8);
        pageCondition.Field.Range.Lte.Should().Be(12);
    }

    [Fact]
    public async Task NullPage_ProducesBookOnlyFilter_NoPageClause()
    {
        var qdrant = new RecordingQdrantSearchClient { Result = [MakePoint(0.9f)] };
        var sut = BuildSut(qdrant);

        await sut.GroundAsync("some entity text", "PHB", null, CancellationToken.None);

        var filter = qdrant.CapturedFilter;
        filter.Should().NotBeNull();
        filter!.Must.Should().HaveCount(1);
        filter.Must.Single().Field.Key.Should().Be(QdrantPayloadFields.SourceBook);
    }
}
