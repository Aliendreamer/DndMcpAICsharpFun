namespace DndMcpAICsharpFun.Domain;

public sealed record ChunkMetadata(
    string SourceBook,
    DndVersion Version,
    ContentCategory Category,
    string? EntityName,
    string Chapter,
    int PageNumber,
    int ChunkIndex,
    int? PageEnd = null,
    string? SectionTitle = null,
    int? SectionStart = null,
    int? SectionEnd = null);
