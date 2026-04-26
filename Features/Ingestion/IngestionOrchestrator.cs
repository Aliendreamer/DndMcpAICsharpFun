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
            Log.RecordNotFound(logger, recordId);
            return;
        }

        Log.StartingIngestion(logger, record.DisplayName, recordId);
        await tracker.MarkProcessingAsync(recordId, cancellationToken);

        try
        {
            var currentHash = await ComputeHashAsync(record.FilePath, cancellationToken);

            if (record.Status == IngestionStatus.Completed && record.FileHash == currentHash)
            {
                Log.SkippingUnchanged(logger, record.DisplayName);
                return;
            }

            var pages = extractor.ExtractPages(record.FilePath);
            var version = Enum.Parse<DndVersion>(record.Version);
            var chunks = chunker.Chunk(pages, record.SourceName, version).ToList();

            await embeddingIngestor.IngestAsync(chunks, currentHash, cancellationToken);
            await tracker.MarkCompletedAsync(recordId, chunks.Count, cancellationToken);

            Log.CompletedIngestion(logger, record.DisplayName, chunks.Count);
        }
        catch (Exception ex)
        {
            Log.IngestionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, cancellationToken);
        }
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion record {Id} not found")]
        public static partial void RecordNotFound(ILogger logger, int id);

        [LoggerMessage(Level = LogLevel.Information, Message = "Starting ingestion for {DisplayName} (id={Id})")]
        public static partial void StartingIngestion(ILogger logger, string displayName, int id);

        [LoggerMessage(Level = LogLevel.Information, Message = "Skipping {DisplayName} — already ingested with same hash")]
        public static partial void SkippingUnchanged(ILogger logger, string displayName);

        [LoggerMessage(Level = LogLevel.Information, Message = "Completed {DisplayName}: {Count} chunks")]
        public static partial void CompletedIngestion(ILogger logger, string displayName, int count);

        [LoggerMessage(Level = LogLevel.Error, Message = "Ingestion failed for {DisplayName} (id={Id})")]
        public static partial void IngestionFailed(ILogger logger, Exception ex, string displayName, int id);
    }
}
