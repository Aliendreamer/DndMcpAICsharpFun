namespace DndMcpAICsharpFun.Domain;

/// <summary>Global per-term document frequency across the whole dnd_blocks corpus.</summary>
public sealed class Bm25TermStat
{
    public string Term { get; set; } = string.Empty;      // PK
    public long DocumentFrequency { get; set; }
}

/// <summary>Global corpus totals (single row, Id = 1). avgDocLen = TotalTokenLength / max(DocumentCount,1).</summary>
public sealed class Bm25CorpusStat
{
    public int Id { get; set; }                            // PK, always 1
    public long DocumentCount { get; set; }
    public long TotalTokenLength { get; set; }
}

/// <summary>One row per ingested book (keyed by FileHash): the book's contribution to the global stats,
/// so re-ingest/delete can subtract it exactly. TermDfJson = { term: dfInThisBook }.</summary>
public sealed class Bm25BookStat
{
    public string FileHash { get; set; } = string.Empty;  // PK
    public long DocumentCount { get; set; }
    public long TotalTokenLength { get; set; }
    public string TermDfJson { get; set; } = "{}";
}