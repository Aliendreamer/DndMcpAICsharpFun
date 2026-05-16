namespace DndMcpAICsharpFun.Infrastructure.Sqlite;

public sealed class IngestionOptions
{
    public string BooksPath { get; set; } = "/books";
    public string DatabasePath { get; set; } = "/data/ingestion.db";
    public int MaxChunkTokens { get; set; } = 512;
    public int OverlapTokens { get; set; } = 64;
}
