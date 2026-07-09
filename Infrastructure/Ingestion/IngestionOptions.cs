namespace DndMcpAICsharpFun.Infrastructure.Ingestion;

public sealed class IngestionOptions
{
    public string BooksPath { get; set; } = "/books";
    public int MaxChunkTokens { get; set; } = 512;
    public int OverlapTokens { get; set; } = 64;
}