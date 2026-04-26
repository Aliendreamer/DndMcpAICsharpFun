using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed partial class EmbeddingIngestor(
    IEmbeddingService embeddingService,
    IVectorStoreService vectorStore,
    IOptions<IngestionOptions> options,
    ILogger<EmbeddingIngestor> logger) : IEmbeddingIngestor
{
    private readonly int _batchSize = options.Value.EmbeddingBatchSize;

    public async Task IngestAsync(IList<ContentChunk> chunks, string fileHash, CancellationToken ct = default)
    {
        int total = chunks.Count;
        int upserted = 0;

        for (int offset = 0; offset < total; offset += _batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = chunks.Skip(offset).Take(_batchSize).ToList();
            var texts = batch.Select(static c => c.Text).ToList();
            var vectors = await embeddingService.EmbedAsync(texts, ct);

            var points = batch
                .Zip(vectors, (chunk, vector) => (chunk, vector, fileHash))
                .ToList();

            await vectorStore.UpsertAsync(points, ct);
            upserted += batch.Count;

            LogUpsertedChunks(logger, upserted, total);
        }

        LogIngestedChunks(logger, total);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Upserted {Upserted}/{Total} chunks")]
    private static partial void LogUpsertedChunks(ILogger logger, int upserted, int total);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ingested {Total} chunks into vector store")]
    private static partial void LogIngestedChunks(ILogger logger, int total);
}
