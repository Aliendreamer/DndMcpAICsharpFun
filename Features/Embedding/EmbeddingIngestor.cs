using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Embedding;

public sealed class EmbeddingIngestor : IEmbeddingIngestor
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStoreService _vectorStore;
    private readonly int _batchSize;
    private readonly ILogger<EmbeddingIngestor> _logger;

    public EmbeddingIngestor(
        IEmbeddingService embeddingService,
        IVectorStoreService vectorStore,
        IOptions<IngestionOptions> options,
        ILogger<EmbeddingIngestor> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _batchSize = options.Value.EmbeddingBatchSize;
        _logger = logger;
    }

    public async Task IngestAsync(IList<ContentChunk> chunks, string fileHash, CancellationToken ct = default)
    {
        int total = chunks.Count;
        int upserted = 0;

        for (int offset = 0; offset < total; offset += _batchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = chunks.Skip(offset).Take(_batchSize).ToList();
            var texts = batch.Select(c => c.Text).ToList();

            var vectors = await _embeddingService.EmbedAsync(texts, ct);

            var points = batch
                .Zip(vectors, (chunk, vector) => (chunk, vector, fileHash))
                .ToList();

            await _vectorStore.UpsertAsync(points, ct);
            upserted += batch.Count;

            _logger.LogDebug("Upserted {Upserted}/{Total} chunks", upserted, total);
        }

        _logger.LogInformation("Ingested {Total} chunks into vector store", total);
    }
}
