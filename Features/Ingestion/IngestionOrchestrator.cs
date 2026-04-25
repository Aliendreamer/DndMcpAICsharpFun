using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed class IngestionOrchestrator : IIngestionOrchestrator
{
    private readonly IIngestionTracker _tracker;
    private readonly IPdfTextExtractor _extractor;
    private readonly DndChunker _chunker;
    private readonly IEmbeddingIngestor _embeddingIngestor;
    private readonly ILogger<IngestionOrchestrator> _logger;

    public IngestionOrchestrator(
        IIngestionTracker tracker,
        IPdfTextExtractor extractor,
        DndChunker chunker,
        IEmbeddingIngestor embeddingIngestor,
        ILogger<IngestionOrchestrator> logger)
    {
        _tracker = tracker;
        _extractor = extractor;
        _chunker = chunker;
        _embeddingIngestor = embeddingIngestor;
        _logger = logger;
    }

    public async Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await _tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            _logger.LogWarning("Ingestion record {Id} not found", recordId);
            return;
        }

        _logger.LogInformation("Starting ingestion for {DisplayName} (id={Id})", record.DisplayName, recordId);

        await _tracker.MarkProcessingAsync(recordId, cancellationToken);

        try
        {
            var currentHash = await ComputeHashAsync(record.FilePath, cancellationToken);

            if (record.Status == IngestionStatus.Completed && record.FileHash == currentHash)
            {
                _logger.LogInformation("Skipping {DisplayName} — already ingested with same hash", record.DisplayName);
                return;
            }

            var pages = _extractor.ExtractPages(record.FilePath);
            var version = Enum.Parse<DndVersion>(record.Version);

            var chunks = _chunker.Chunk(pages, record.SourceName, version).ToList();

            await _embeddingIngestor.IngestAsync(chunks, currentHash, cancellationToken);

            await _tracker.MarkCompletedAsync(recordId, chunks.Count, cancellationToken);
            _logger.LogInformation("Completed {DisplayName}: {Count} chunks", record.DisplayName, chunks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ingestion failed for {DisplayName} (id={Id})", record.DisplayName, recordId);
            await _tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
        }
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
