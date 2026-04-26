using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Tests.Embedding;

public sealed class EmbeddingIngestorTests
{
    private readonly IEmbeddingService _embeddingService = Substitute.For<IEmbeddingService>();
    private readonly IVectorStoreService _vectorStore = Substitute.For<IVectorStoreService>();

    private EmbeddingIngestor BuildSut(int batchSize = 32)
    {
        var opts = Options.Create(new IngestionOptions { EmbeddingBatchSize = batchSize });
        return new EmbeddingIngestor(_embeddingService, _vectorStore, opts, NullLogger<EmbeddingIngestor>.Instance);
    }

    private static List<ContentChunk> MakeChunks(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ContentChunk($"text {i}", new ChunkMetadata(
                SourceBook: "PHB", Version: DndVersion.Edition2014, Category: ContentCategory.Spell,
                EntityName: null, Chapter: "Ch1", PageNumber: 1, ChunkIndex: i)))
            .ToList();

    private void SetupEmbed(int batchSize)
    {
        _embeddingService.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.Arg<IList<string>>();
                IList<float[]> vectors = texts.Select(_ => new float[] { 0.1f, 0.2f }).ToList();
                return Task.FromResult(vectors);
            });
    }

    [Fact]
    public async Task IngestAsync_AllChunksUpserted_SingleBatch()
    {
        SetupEmbed(3);
        var chunks = MakeChunks(3);
        var sut = BuildSut(batchSize: 32);

        await sut.IngestAsync(chunks, "hash123");

        await _vectorStore.Received(1).UpsertAsync(
            Arg.Is<IList<(ContentChunk, float[], string)>>(p => p.Count == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_MultipleBatches_WhenCountExceedsBatchSize()
    {
        SetupEmbed(2);
        var chunks = MakeChunks(5);
        var sut = BuildSut(batchSize: 2);

        await sut.IngestAsync(chunks, "hash123");

        // 5 chunks at batch size 2: ceil(5/2) = 3 calls
        await _vectorStore.Received(3).UpsertAsync(
            Arg.Any<IList<(ContentChunk, float[], string)>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_EachBatchIsNoLargerThanBatchSize()
    {
        SetupEmbed(2);
        var chunks = MakeChunks(5);
        var sut = BuildSut(batchSize: 2);
        var batchSizes = new List<int>();

        _vectorStore.UpsertAsync(Arg.Do<IList<(ContentChunk, float[], string)>>(b => batchSizes.Add(b.Count)), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await sut.IngestAsync(chunks, "hash123");

        Assert.All(batchSizes, size => Assert.True(size <= 2));
    }
}
