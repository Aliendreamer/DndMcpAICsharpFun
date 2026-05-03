using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class BlockIngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfBookmarkReader bookmarkReader,
    IPdfBlockExtractor blockExtractor,
    IEmbeddingService embedding,
    IVectorStoreService vectorStore,
    ILogger<BlockIngestionOrchestrator> logger) : IBlockIngestionOrchestrator
{
    private const string NoBookmarksError =
        "PDF has no embedded bookmarks; bookmark-driven block ingestion requires them.";

    private const int EmbedBatchSize = 32;
    private const int MinBlockChars = 40;

    public async Task IngestBlocksAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            LogRecordNotFound(logger, recordId);
            return;
        }

        LogStarting(logger, record.DisplayName, recordId);

        try
        {
            var hash = string.IsNullOrEmpty(record.FileHash)
                ? await ComputeHashAsync(record.FilePath, cancellationToken)
                : record.FileHash;
            await tracker.MarkHashAsync(recordId, hash, cancellationToken);

            var bookmarks = bookmarkReader.ReadBookmarks(record.FilePath);
            if (bookmarks.Count == 0)
            {
                LogNoBookmarks(logger, record.DisplayName, recordId);
                await tracker.MarkFailedAsync(recordId, NoBookmarksError, CancellationToken.None);
                return;
            }

            if (record.ChunkCount.HasValue)
                await vectorStore.DeleteBlocksByHashAsync(hash, record.ChunkCount.Value, CancellationToken.None);

            var tocEntries = BookmarkTocMapper.Map(bookmarks);
            var tocMap = new TocCategoryMap(tocEntries);

            var version = Enum.TryParse<DndVersion>(record.Version, ignoreCase: true, out var v)
                ? v : DndVersion.Edition2014;

            var chunks = new List<BlockChunk>();
            var globalIndex = 0;
            foreach (var block in blockExtractor.ExtractBlocks(record.FilePath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (block.Text.Length < MinBlockChars) continue;
                if (IsMostlyNumeric(block.Text)) continue;

                var entry = tocMap.GetEntry(block.PageNumber);
                if (entry is null) continue;

                var meta = new BlockMetadata(
                    SourceBook:   record.DisplayName,
                    Version:      version,
                    Category:     entry.Category ?? ContentCategory.Rule,
                    SectionTitle: entry.Title,
                    SectionStart: entry.StartPage,
                    SectionEnd:   entry.EndPage ?? int.MaxValue,
                    PageNumber:   block.PageNumber,
                    BlockOrder:   block.Order,
                    GlobalIndex:  globalIndex++,
                    BookType:     record.BookType);
                chunks.Add(new BlockChunk(block.Text, meta));
            }

            if (chunks.Count == 0)
            {
                LogNoBlocksMatched(logger, record.DisplayName, recordId);
                await tracker.MarkFailedAsync(recordId,
                    "No blocks fell within any bookmark section.", CancellationToken.None);
                return;
            }

            for (var i = 0; i < chunks.Count; i += EmbedBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = chunks.Skip(i).Take(EmbedBatchSize).ToList();
                var vectors = await embedding.EmbedAsync(
                    batch.Select(static c => c.Text).ToList(), cancellationToken);
                var points = batch
                    .Zip(vectors, (chunk, vec) => (chunk, vec, hash))
                    .Select(static t => (t.chunk, t.vec, t.hash))
                    .ToList();
                await vectorStore.UpsertBlocksAsync(points, cancellationToken);
                LogBatch(logger, i + batch.Count, chunks.Count, recordId);
            }

            await tracker.MarkJsonIngestedAsync(recordId, chunks.Count, cancellationToken);
            LogDone(logger, record.DisplayName, recordId, chunks.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, CancellationToken.None);
        }
    }

    private static bool IsMostlyNumeric(string text)
    {
        var letters = 0;
        var digitsOrPunct = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c)) letters++;
            else if (char.IsDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c)) digitsOrPunct++;
        }
        var total = letters + digitsOrPunct;
        return total > 0 && letters * 100 / total < 40;
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Block ingestion record {Id} not found")]
    private static partial void LogRecordNotFound(ILogger logger, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting block ingestion for {DisplayName} (id={Id})")]
    private static partial void LogStarting(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No bookmarks for {DisplayName} (id={Id}) — block ingestion aborted")]
    private static partial void LogNoBookmarks(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No blocks matched any section for {DisplayName} (id={Id})")]
    private static partial void LogNoBlocksMatched(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Block batch {Done}/{Total} upserted for book {BookId}")]
    private static partial void LogBatch(ILogger logger, int done, int total, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Block ingestion completed for {DisplayName} (id={Id}): {Count} blocks")]
    private static partial void LogDone(ILogger logger, string displayName, int id, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Block ingestion failed for {DisplayName} (id={Id})")]
    private static partial void LogFailed(ILogger logger, Exception ex, string displayName, int id);
}
