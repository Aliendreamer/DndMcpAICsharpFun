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
        _chapterTracker.Reset();

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
        // nomic-embed-text (nomic-bert) hard limit is 2048 tokens; ~4 chars/token.
        int charBudget = _maxTokens * 4;

        if (text.Length <= charBudget)
        {
            yield return text;
            yield break;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int start = 0;
        while (start < words.Length)
        {
            var chunk = new System.Text.StringBuilder();
            int i = start;
            while (i < words.Length)
            {
                string word = words[i];

                // Word alone exceeds budget (e.g. PDF extracted without spaces):
                // flush any partial chunk, then slice the word by characters.
                if (word.Length > charBudget)
                {
                    if (chunk.Length > 0)
                    {
                        yield return chunk.ToString();
                        chunk.Clear();
                    }
                    for (int c = 0; c < word.Length; c += charBudget)
                        yield return word.Substring(c, Math.Min(charBudget, word.Length - c));
                    i++;
                    start = i;
                    goto nextChunk;
                }

                int addLen = (chunk.Length > 0 ? 1 : 0) + word.Length;
                if (chunk.Length + addLen > charBudget) break;
                if (chunk.Length > 0) chunk.Append(' ');
                chunk.Append(word);
                i++;
            }
            if (chunk.Length > 0)
                yield return chunk.ToString();
            int consumed = i - start;
            start += Math.Max(1, consumed - _overlap);
            if (start >= words.Length) break;
            nextChunk:;
        }
    }

    private sealed record EntityBlock(
        IReadOnlyList<(int PageNumber, string Line)> Lines,
        string? EntityName,
        string Chapter,
        ContentCategory ChapterCategory);
}
