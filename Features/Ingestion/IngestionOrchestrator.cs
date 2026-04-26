using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfTextExtractor extractor,
    DndChunker chunker,
    IEmbeddingIngestor embeddingIngestor,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
{
    public async Task IngestBookAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            LogRecordNotFound(logger, recordId);
            return;
        }

        LogStartingIngestion(logger, record.DisplayName, recordId);
        await tracker.MarkProcessingAsync(recordId, cancellationToken);

        try
        {
            var currentHash = await ComputeHashAsync(record.FilePath, cancellationToken);

            if (record.Status == IngestionStatus.Completed && record.FileHash == currentHash)
            {
                LogSkippingUnchanged(logger, record.DisplayName);
                return;
            }

            var pages = extractor.ExtractPages(record.FilePath);
            var version = Enum.Parse<DndVersion>(record.Version);
            var chunks = chunker.Chunk(pages, record.SourceName, version).ToList();

            await embeddingIngestor.IngestAsync(chunks, currentHash, cancellationToken);
            await tracker.MarkCompletedAsync(recordId, chunks.Count, cancellationToken);

            LogCompletedIngestion(logger, record.DisplayName, chunks.Count);
        }
        catch (Exception ex)
        {
            LogIngestionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
        }
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion record {Id} not found")]
    private static partial void LogRecordNotFound(ILogger logger, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting ingestion for {DisplayName} (id={Id})")]
    private static partial void LogStartingIngestion(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipping {DisplayName} — already ingested with same hash")]
    private static partial void LogSkippingUnchanged(ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Completed {DisplayName}: {Count} chunks")]
    private static partial void LogCompletedIngestion(ILogger logger, string displayName, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion failed for {DisplayName} (id={Id})")]
    private static partial void LogIngestionFailed(ILogger logger, Exception ex, string displayName, int id);
}
