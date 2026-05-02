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
    ILlmEntityExtractor entityExtractor,
    IEntityJsonStore jsonStore,
    IJsonIngestionPipeline jsonPipeline,
    IOptions<IngestionOptions> ingestionOptions,
    ITocMapExtractor tocMapExtractor,
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

        if (record.TocPage is null)
        {
            LogTocPageMissing(logger, record.DisplayName, recordId);
            await tracker.MarkFailedAsync(recordId, "TocPage is required for extraction.", CancellationToken.None);
            return;
        }

        LogStartingExtraction(logger, record.DisplayName, recordId);

        try
        {
            var currentHash = string.IsNullOrEmpty(record.FileHash)
                ? await ComputeHashAsync(record.FilePath, cancellationToken)
                : record.FileHash;
            await tracker.MarkHashAsync(recordId, currentHash, cancellationToken);

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

            var tocPage = extractor.ExtractSinglePage(record.FilePath, record.TocPage.Value);
            if (tocPage is null)
            {
                LogTocPageNotFound(logger, record.TocPage.Value, record.DisplayName, recordId);
                await tracker.MarkFailedAsync(recordId, $"TOC page {record.TocPage.Value} could not be extracted.", CancellationToken.None);
                return;
            }

            var tocEntries = await tocMapExtractor.ExtractMapAsync(tocPage.RawText, cancellationToken);
            var tocMap = new TocCategoryMap(tocEntries);

            if (tocMap.IsEmpty)
                LogTocFallback(logger, record.DisplayName, recordId);
            else
                LogTocGuided(logger, record.DisplayName, recordId);

            var pages = extractor.ExtractPages(record.FilePath).ToList();

            foreach (var structuredPage in pages)
            {
                if (structuredPage.RawText.Length < ingestionOptions.Value.MinPageCharacters)
                    continue;

                var entry = tocMap.GetEntry(structuredPage.PageNumber);
                if (entry is null)
                    continue;

                var groups = PageBlockGrouper.Group(structuredPage.Blocks);
                var pageEntities = new List<ExtractedEntity>();

                foreach (var group in groups)
                {
                    var promptText = BuildPromptText(group);
                    var entityType = entry.Category?.ToString() ?? "Rule";
                    var extracted = await entityExtractor.ExtractAsync(
                        promptText, entityType, structuredPage.PageNumber,
                        record.DisplayName, record.Version,
                        entry.Title, entry.StartPage, entry.EndPage!.Value,
                        cancellationToken);
                    pageEntities.AddRange(extracted);
                }

                await jsonStore.SavePageAsync(recordId, structuredPage, pageEntities, cancellationToken);
                LogExtractedPage(logger, structuredPage.PageNumber, pages.Count, recordId);
            }

            await tracker.MarkExtractedAsync(recordId, cancellationToken);
            LogExtractedBook(logger, record.DisplayName, recordId);
        }
        catch (OperationCanceledException)
        {
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

        List<ExtractedEntity> entities = [];

        if (record.TocPage.HasValue)
        {
            var tocPage = extractor.ExtractSinglePage(record.FilePath, record.TocPage.Value);
            if (tocPage is not null)
            {
                var tocEntries = await tocMapExtractor.ExtractMapAsync(tocPage.RawText, ct);
                var tocMap = new TocCategoryMap(tocEntries);
                var entry = tocMap.GetEntry(pageNumber);

                if (entry is not null)
                {
                    var groups = PageBlockGrouper.Group(structuredPage.Blocks);
                    foreach (var group in groups)
                    {
                        var promptText = BuildPromptText(group);
                        var entityType = entry.Category?.ToString() ?? "Rule";
                        var extracted = await entityExtractor.ExtractAsync(
                            promptText, entityType, pageNumber,
                            record.DisplayName, record.Version,
                            entry.Title, entry.StartPage, entry.EndPage!.Value, ct);
                        entities.AddRange(extracted);
                    }
                }
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "TocPage is not set for {DisplayName} (id={Id}) — extraction requires tocPage")]
    private static partial void LogTocPageMissing(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC page {TocPage} not found in PDF for {DisplayName} (id={Id})")]
    private static partial void LogTocPageNotFound(ILogger logger, int tocPage, string displayName, int id);

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Extracted page {Page}/{Total} for book {BookId}")]
    private static partial void LogExtractedPage(ILogger logger, int page, int total, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "TOC-guided extraction active for {DisplayName} (id={Id})")]
    private static partial void LogTocGuided(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "TOC map empty for {DisplayName} (id={Id}) — all pages will be skipped")]
    private static partial void LogTocFallback(ILogger logger, string displayName, int id);
}
