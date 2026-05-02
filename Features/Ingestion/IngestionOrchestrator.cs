using System.Security.Cryptography;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class IngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfStructuredExtractor extractor,
    IVectorStoreService vectorStore,
    ILlmClassifier classifier,
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IJsonIngestionPipeline jsonPipeline,
    IOptions<IngestionOptions> ingestionOptions,
    IPdfBookmarkReader bookmarkReader,
    ITocCategoryClassifier tocClassifier,
    ILogger<IngestionOrchestrator> logger) : IIngestionOrchestrator
{
    public async Task ExtractBookAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            LogRecordNotFound(logger, recordId);
            return;
        }

        LogStartingExtraction(logger, record.DisplayName, recordId);

        try
        {
            var currentHash = string.IsNullOrEmpty(record.FileHash)
                ? await ComputeHashAsync(record.FilePath, cancellationToken)
                : record.FileHash;
            await tracker.MarkHashAsync(recordId, currentHash, cancellationToken);

            // Clean up any previous extraction before starting fresh
            if (record.Status == IngestionStatus.JsonIngested)
            {
                await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount!.Value, CancellationToken.None);
                jsonStore.DeleteAllPages(recordId);
                await tracker.ResetForReingestionAsync(recordId, CancellationToken.None);
            }
            else if (record.Status == IngestionStatus.Extracted)
            {
                jsonStore.DeleteAllPages(recordId);
                await tracker.ResetForReingestionAsync(recordId, CancellationToken.None);
            }

            // TOC-guided classification: read bookmarks and classify into a category map
            var bookmarks = bookmarkReader.ReadBookmarks(record.FilePath);
            LogClassifyingToc(logger, bookmarks.Count, recordId);
            var tocMap = await tocClassifier.ClassifyAsync(bookmarks, cancellationToken);

            if (tocMap.IsEmpty)
                LogTocFallback(logger, record.DisplayName, recordId);
            else
                LogTocGuided(logger, record.DisplayName, recordId);

            var pages = extractor.ExtractPages(record.FilePath).ToList();

            foreach (var structuredPage in pages)
            {
                if (structuredPage.RawText.Length < ingestionOptions.Value.MinPageCharacters)
                    continue;

                var promptText = BuildPromptText(structuredPage.Blocks);

                if (!tocMap.IsEmpty)
                {
                    // TOC-guided: at most one LLM call per page
                    var category = tocMap.GetCategory(structuredPage.PageNumber);
                    if (category is null)
                    {
                        // This page doesn't belong to any recognized D&D content section — skip it
                        continue;
                    }

                    LogClassifyingPage(logger, structuredPage.PageNumber, pages.Count, recordId);
                    var extracted = await entityExtractor.ExtractAsync(
                        promptText, category.Value.ToString(), structuredPage.PageNumber,
                        record.DisplayName, record.Version, cancellationToken);

                    if (extracted.Count > 0)
                        await jsonStore.SavePageAsync(recordId, structuredPage, extracted, cancellationToken);
                }
                else
                {
                    // Fallback: run all category extractors (original behavior)
                    LogClassifyingPage(logger, structuredPage.PageNumber, pages.Count, recordId);
                    var types = await classifier.ClassifyPageAsync(structuredPage.RawText, cancellationToken);
                    var pageEntities = new List<ExtractedEntity>();

                    foreach (var type in types)
                    {
                        var extracted = await entityExtractor.ExtractAsync(
                            promptText, type, structuredPage.PageNumber,
                            record.DisplayName, record.Version, cancellationToken);
                        pageEntities.AddRange(extracted);
                    }

                    if (pageEntities.Count > 0)
                        await jsonStore.SavePageAsync(recordId, structuredPage, pageEntities, cancellationToken);
                }

                LogExtractedPage(logger, structuredPage.PageNumber, pages.Count, recordId);
            }

            await tracker.MarkExtractedAsync(recordId, cancellationToken);
            LogExtractedBook(logger, record.DisplayName, recordId);
        }
        catch (OperationCanceledException)
        {
            // Clean up partial extraction data so re-ingestion starts fresh
            jsonStore.DeleteAllPages(recordId);
            await tracker.ResetForReingestionAsync(recordId, CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            LogExtractionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, CancellationToken.None);
        }
    }

    public async Task IngestJsonAsync(int recordId, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(recordId, cancellationToken);
        if (record is null)
        {
            LogRecordNotFound(logger, recordId);
            return;
        }

        LogStartingJsonIngestion(logger, record.DisplayName, recordId);

        try
        {
            await tracker.MarkHashAsync(recordId, record.FileHash, cancellationToken);
            await jsonPipeline.IngestAsync(recordId, record.FileHash, cancellationToken);

            var pages = await jsonStore.LoadAllPagesAsync(recordId, cancellationToken);
            var chunkCount = pages.Sum(p => p.Entities.Count(e =>
                !string.IsNullOrWhiteSpace(e.Data["description"]?.GetValue<string>())));

            await tracker.MarkJsonIngestedAsync(recordId, chunkCount, cancellationToken);
            LogJsonIngested(logger, record.DisplayName, recordId, chunkCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogJsonIngestionFailed(logger, ex, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, ex.Message, CancellationToken.None);
        }
    }

    public async Task<DeleteBookResult> DeleteBookAsync(int id, CancellationToken cancellationToken = default)
    {
        var record = await tracker.GetByIdAsync(id, cancellationToken);
        if (record is null)
            return DeleteBookResult.NotFound;

        if (record.Status == IngestionStatus.Processing)
            return DeleteBookResult.Conflict;

        if (record.Status == IngestionStatus.JsonIngested && record.ChunkCount.HasValue)
            await vectorStore.DeleteByHashAsync(record.FileHash, record.ChunkCount.Value, cancellationToken);

        jsonStore.DeleteAllPages(id);

        if (File.Exists(record.FilePath))
            File.Delete(record.FilePath);

        await tracker.DeleteAsync(id, cancellationToken);
        LogBookDeleted(logger, record.DisplayName, id);
        return DeleteBookResult.Deleted;
    }

    public async Task<PageData?> ExtractSinglePageAsync(
        int bookId, int pageNumber, bool save, CancellationToken ct = default)
    {
        var record = await tracker.GetByIdAsync(bookId, ct);
        if (record is null) return null;

        var structuredPage = extractor.ExtractSinglePage(record.FilePath, pageNumber);
        if (structuredPage is null) return null;

        var promptText = BuildPromptText(structuredPage.Blocks);
        var bookmarks  = bookmarkReader.ReadBookmarks(record.FilePath);
        var tocMap     = await tocClassifier.ClassifyAsync(bookmarks, ct);

        List<ExtractedEntity> entities;
        var category = tocMap.IsEmpty ? null : tocMap.GetCategory(pageNumber);

        if (category is not null)
        {
            entities = [.. await entityExtractor.ExtractAsync(
                promptText, category.Value.ToString(), pageNumber,
                record.DisplayName, record.Version, ct)];
        }
        else
        {
            var types = await classifier.ClassifyPageAsync(structuredPage.RawText, ct);
            entities = [];
            foreach (var type in types)
            {
                var extracted = await entityExtractor.ExtractAsync(
                    promptText, type, pageNumber, record.DisplayName, record.Version, ct);
                entities.AddRange(extracted);
            }
        }

        var pageData = new PageData(pageNumber, structuredPage.RawText, structuredPage.Blocks, entities);

        if (save)
            await jsonStore.SavePageAsync(bookId, structuredPage, entities, ct);

        return pageData;
    }

    internal static string BuildPromptText(IReadOnlyList<PageBlock> blocks)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var block in blocks)
        {
            var prefix = block.Level switch
            {
                "h1" => "[H1] ",
                "h2" => "[H2] ",
                "h3" => "[H3] ",
                _    => string.Empty
            };
            sb.AppendLine(prefix + block.Text);
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ingestion record {Id} not found")]
    private static partial void LogRecordNotFound(ILogger logger, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted book {DisplayName} (id={Id})")]
    private static partial void LogBookDeleted(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting extraction for {DisplayName} (id={Id})")]
    private static partial void LogStartingExtraction(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted {DisplayName} (id={Id}) — JSON files saved")]
    private static partial void LogExtractedBook(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Extraction failed for {DisplayName} (id={Id})")]
    private static partial void LogExtractionFailed(ILogger logger, Exception ex, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting JSON ingestion for {DisplayName} (id={Id})")]
    private static partial void LogStartingJsonIngestion(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "JSON ingestion completed for {DisplayName} (id={Id}): {Count} chunks")]
    private static partial void LogJsonIngested(ILogger logger, string displayName, int id, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "JSON ingestion failed for {DisplayName} (id={Id})")]
    private static partial void LogJsonIngestionFailed(ILogger logger, Exception ex, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Classifying page {Page}/{Total} for book {BookId}")]
    private static partial void LogClassifyingPage(ILogger logger, int page, int total, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted page {Page}/{Total} for book {BookId}")]
    private static partial void LogExtractedPage(ILogger logger, int page, int total, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Classifying {BookmarkCount} TOC bookmarks for book {BookId}")]
    private static partial void LogClassifyingToc(ILogger logger, int bookmarkCount, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC classification succeeded for {DisplayName} (id={Id}) — using TOC-guided dispatch")]
    private static partial void LogTocGuided(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC classification empty for {DisplayName} (id={Id}) — falling back to per-page classifier")]
    private static partial void LogTocFallback(ILogger logger, string displayName, int id);
}
