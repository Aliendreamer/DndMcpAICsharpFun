using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

public sealed record RetrievalQuery(
    string QueryText,
    DndVersion? Version = null,
    ContentCategory? Category = null,
    string? SourceBook = null,
    string? EntityName = null,
    int TopK = 5);
