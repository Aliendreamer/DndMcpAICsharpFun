namespace DndMcpAICsharpFun.Domain;

public sealed record ChunkMetadata(
    string SourceBook,
    DndVersion Version,
    ContentCategory Category,
    string? EntityName,
    string Chapter,
    int PageNumber,
    int ChunkIndex
);
