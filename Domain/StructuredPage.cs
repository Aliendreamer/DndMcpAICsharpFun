namespace DndMcpAICsharpFun.Domain;

public sealed record StructuredPage(
    int PageNumber,
    string RawText,
    IReadOnlyList<PageBlock> Blocks);
