using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking;

public sealed class DndChunker
{
    private readonly ContentCategoryDetector _categoryDetector;
    private readonly ChapterContextTracker _chapterTracker;
    private readonly int _maxTokens;
    private readonly int _overlap;

    public DndChunker(
        ContentCategoryDetector categoryDetector,
        IOptions<IngestionOptions> options)
    {
        _categoryDetector = categoryDetector;
        _chapterTracker = new ChapterContextTracker();
        _maxTokens = options.Value.MaxChunkTokens;
        _overlap = options.Value.OverlapTokens;
    }

    public IEnumerable<ContentChunk> Chunk(
        IEnumerable<(int PageNumber, string Text)> pages,
        string sourceBook,
        DndVersion version)
    {
        var allLines = new List<(int PageNumber, string Line)>();
        foreach (var (pageNum, text) in pages)
        {
            foreach (var line in text.Split('\n'))
                allLines.Add((pageNum, line));
        }

        var entityBlocks = SplitIntoEntityBlocks(allLines);

        int chunkIndex = 0;
        foreach (var block in entityBlocks)
        {
            var blockLines = block.Lines;
            string blockText = string.Join("\n", blockLines.Select(l => l.Line));
            int pageNumber = block.Lines.Count > 0 ? block.Lines[0].PageNumber : 0;

            var category = _categoryDetector.Detect(blockText, block.ChapterCategory);

            var textChunks = SplitIntoTokenChunks(blockText);
            foreach (var chunkText in textChunks)
            {
                yield return new ContentChunk(
                    chunkText,
                    new ChunkMetadata(
                        SourceBook: sourceBook,
                        Version: version,
                        Category: category,
                        EntityName: block.EntityName,
                        Chapter: block.Chapter,
                        PageNumber: pageNumber,
                        ChunkIndex: chunkIndex++));
            }
        }
    }

    private List<EntityBlock> SplitIntoEntityBlocks(List<(int PageNumber, string Line)> allLines)
    {
        var blocks = new List<EntityBlock>();
        var current = new List<(int PageNumber, string Line)>();
        string currentChapter = _chapterTracker.CurrentChapter;
        ContentCategory currentCategory = _chapterTracker.CurrentCategory;
        string? currentEntityName = null;

        for (int i = 0; i < allLines.Count; i++)
        {
            var (pageNum, line) = allLines[i];

            _chapterTracker.ProcessLine(line);

            if (_chapterTracker.CurrentChapter != currentChapter)
            {
                currentChapter = _chapterTracker.CurrentChapter;
                currentCategory = _chapterTracker.CurrentCategory;
            }

            var boundaryDetector = _categoryDetector.FindBoundaryDetector(line);
            if (boundaryDetector is not null && current.Count > 0)
            {
                // Flush current block
                blocks.Add(new EntityBlock(
                    [.. current],
                    currentEntityName,
                    currentChapter,
                    currentCategory));

                current = [];
                currentEntityName = EntityNameExtractor.Extract(allLines.Select(l => l.Line).ToList(), i);
            }

            current.Add((pageNum, line));
        }

        if (current.Count > 0)
        {
            blocks.Add(new EntityBlock(
                [.. current],
                currentEntityName,
                currentChapter,
                currentCategory));
        }

        return blocks;
    }

    private IEnumerable<string> SplitIntoTokenChunks(string text)
    {
        // Approximate token count as word count (fast, good enough for chunking)
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length <= _maxTokens)
        {
            yield return text;
            yield break;
        }

        int start = 0;
        while (start < words.Length)
        {
            int end = Math.Min(start + _maxTokens, words.Length);
            yield return string.Join(' ', words[start..end]);
            start += _maxTokens - _overlap;
            if (start >= words.Length) break;
        }
    }

    private sealed record EntityBlock(
        IReadOnlyList<(int PageNumber, string Line)> Lines,
        string? EntityName,
        string Chapter,
        ContentCategory ChapterCategory);
}
