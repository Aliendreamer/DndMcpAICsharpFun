using System.Security.Cryptography;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using DndMcpAICsharpFun.Infrastructure.Search;

namespace DndMcpAICsharpFun.Features.Ingestion;

public sealed partial class BlockIngestionOrchestrator(
    IIngestionTracker tracker,
    IPdfBookmarkReader bookmarkReader,
    IPdfBlockExtractor blockExtractor,
    IEmbeddingService embedding,
    IVectorStoreService vectorStore,
    IBm25CorpusStats bm25Stats,
    ILogger<BlockIngestionOrchestrator> logger) : IBlockIngestionOrchestrator
{
    private const int EmbedBatchSize = 32;
    private const int MinBlockChars = 40;
    private const int MaxBlockChars = 800;
    private const int SplitOverlapChars = 100;

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

            var extraction = await blockExtractor.ExtractBlocksAsync(record.FilePath, cancellationToken);

            var bookmarks = bookmarkReader.ReadBookmarks(record.FilePath);
            TocCategoryMap tocMap;
            if (bookmarks.Count > 0)
            {
                tocMap = new TocCategoryMap(BookmarkTocMapper.Map(bookmarks));
            }
            else
            {
                LogHeadingFallback(logger, record.DisplayName, recordId, extraction.Headings.Count);
                tocMap = new TocCategoryMap(
                    FullCoverageHeadingTocMapper.Map(extraction.Headings, record.DisplayName));
            }

            if (record.ChunkCount.HasValue)
                await vectorStore.DeleteBlocksByHashAsync(hash, cancellationToken);

            var version = Enum.TryParse<DndVersion>(record.Version, ignoreCase: true, out var v)
                ? v : DndVersion.Edition2014;

            var chunks = BuildChunks(extraction.Blocks, tocMap, record, version);

            // The bookmark TOC (from PdfPig) and the blocks (from MinerU) are independent page sources;
            // when they don't align, every block misses its section and chunks is empty. Rebuild from the
            // same-source heading TOC before giving up. Guarded on bookmarks.Count > 0 so the already
            // bookmarkless path (tocMap is already heading-derived) never rebuilds redundantly.
            if (chunks.Count == 0 && extraction.Headings.Count > 0 && bookmarks.Count > 0)
            {
                LogBookmarkTocFallback(logger, record.DisplayName, recordId, extraction.Headings.Count);
                var headingTocMap = new TocCategoryMap(
                    FullCoverageHeadingTocMapper.Map(extraction.Headings, record.DisplayName));
                chunks = BuildChunks(extraction.Blocks, headingTocMap, record, version);
            }

            if (chunks.Count == 0)
            {
                LogNoBlocksMatched(logger, record.DisplayName, recordId);
                await tracker.MarkFailedAsync(recordId,
                    "No ingestable content found: the PDF produced no prose blocks in any section.",
                    CancellationToken.None);
                return;
            }

            // Contribute this book's corpus statistics to the global BM25 store so doc-side IDF
            // is computed once from the whole corpus (not per-batch). df = number of this book's
            // chunks containing the term; totalTokens = sum of every chunk's token count.
            var termDf = new Dictionary<string, int>();
            long totalTokens = 0;
            foreach (var chunk in chunks)
            {
                var tokens = Bm25Vectorizer.Tokenize(chunk.Text);
                totalTokens += tokens.Count;
                foreach (var term in new HashSet<string>(tokens))
                    termDf[term] = termDf.TryGetValue(term, out var c) ? c + 1 : 1;
            }

            await bm25Stats.ApplyBookAsync(hash, termDf, chunks.Count, totalTokens, cancellationToken);
            var stats = await bm25Stats.ReadAsync(cancellationToken);

            for (var i = 0; i < chunks.Count; i += EmbedBatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = chunks.Skip(i).Take(EmbedBatchSize).ToList();
                var batchTexts = batch.Select(static c => c.Text).ToList();
                var vectors = await embedding.EmbedAsync(batchTexts, cancellationToken);
                var sparseVectors = Bm25Vectorizer.ComputeDocVectors(batchTexts, stats);
                var points = batch
                    .Zip(vectors, static (chunk, vec) => (chunk, vec))
                    .Zip(sparseVectors, (t, sparse) => (t.chunk, t.vec, sparse, hash))
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
            var failureMessage = $"Block ingestion failed (PDF conversion via the MinerU service): {ex.Message}";
            await tracker.MarkFailedAsync(recordId, failureMessage, CancellationToken.None);
        }
    }

    /// <summary>
    /// Splits a block's text into overlapping windows so each window fits comfortably
    /// in the embedding model's context. Blocks shorter than <see cref="MaxBlockChars"/>
    /// pass through unchanged.
    /// </summary>
    public static IEnumerable<string> SplitLongBlock(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxBlockChars)
        {
            yield return text;
            yield break;
        }

        var pos = 0;
        while (pos < text.Length)
        {
            var end = Math.Min(pos + MaxBlockChars, text.Length);

            // Prefer breaking at a word boundary near the end of the window.
            if (end < text.Length)
            {
                var searchFrom = Math.Max(pos + MaxBlockChars - 200, pos + 1);
                var lastSpace = text.LastIndexOf(' ', end - 1, end - searchFrom);
                if (lastSpace > pos + 100) end = lastSpace;
            }

            yield return text[pos..end];

            if (end >= text.Length) yield break;
            pos = Math.Max(pos + 1, end - SplitOverlapChars);
        }
    }

    internal static List<BlockChunk> BuildChunks(
        IReadOnlyList<PdfBlock> blocks,
        TocCategoryMap tocMap,
        IngestionRecord record,
        DndVersion version)
    {
        var chunks = new List<BlockChunk>();
        var globalIndex = 0;
        foreach (var block in blocks)
        {
            if (block.Text.Length < MinBlockChars) continue;
            if (IsMostlyNumeric(block.Text)) continue;

            var entry = tocMap.GetEntry(block.PageNumber);
            if (entry is null) continue;

            foreach (var splitText in SplitLongBlock(block.Text))
            {
                var meta = new BlockMetadata(
                    SourceBook: record.DisplayName,
                    Version: version,
                    Category: entry.Category ?? ContentCategory.Rule,
                    SectionTitle: entry.Title,
                    SectionStart: entry.StartPage,
                    SectionEnd: entry.EndPage ?? int.MaxValue,
                    PageNumber: block.PageNumber,
                    BlockOrder: block.Order,
                    GlobalIndex: globalIndex++,
                    BookType: record.BookType,
                    SourceKey: record.FivetoolsSourceKey);
                chunks.Add(new BlockChunk(splitText, meta));
            }
        }
        return chunks;
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

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No bookmarks for {DisplayName} (id={Id}) — using heading-derived full-coverage TOC from {HeadingCount} headings")]
    private static partial void LogHeadingFallback(ILogger logger, string displayName, int id, int headingCount);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Bookmark TOC matched 0 blocks for {DisplayName} (id={Id}) — falling back to heading-derived TOC from {HeadingCount} headings")]
    private static partial void LogBookmarkTocFallback(ILogger logger, string displayName, int id, int headingCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No blocks matched any section for {DisplayName} (id={Id})")]
    private static partial void LogNoBlocksMatched(ILogger logger, string displayName, int id);

    [LoggerMessage(Level = LogLevel.Information, Message = "Block batch {Done}/{Total} upserted for book {BookId}")]
    private static partial void LogBatch(ILogger logger, int done, int total, int bookId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Block ingestion completed for {DisplayName} (id={Id}): {Count} blocks")]
    private static partial void LogDone(ILogger logger, string displayName, int id, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Block ingestion failed for {DisplayName} (id={Id})")]
    private static partial void LogFailed(ILogger logger, Exception ex, string displayName, int id);
}