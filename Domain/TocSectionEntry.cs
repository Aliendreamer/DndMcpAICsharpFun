namespace DndMcpAICsharpFun.Domain;

public sealed record TocSectionEntry(
    string Title,
    ContentCategory? Category,
    int StartPage,
    int? EndPage = null);
