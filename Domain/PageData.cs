namespace DndMcpAICsharpFun.Domain;

public sealed record PageData(
    int PageNumber,
    string RawText,
    IReadOnlyList<PageBlock> Blocks,
    IReadOnlyList<ExtractedEntity> Entities);
