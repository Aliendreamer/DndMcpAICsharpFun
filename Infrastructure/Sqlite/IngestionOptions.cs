namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public sealed class IngestionOptions
{
    public string BooksPath { get; set; } = "/books";
    public string DatabasePath { get; set; } = "/data/ingestion.db";
    public int MinPageCharacters { get; set; } = 100;
    public int MaxChunkTokens { get; set; } = 512;
    public int OverlapTokens { get; set; } = 64;
    public int EmbeddingBatchSize { get; set; } = 2;
    public int LlmExtractionRetries { get; set; } = 1;
}
