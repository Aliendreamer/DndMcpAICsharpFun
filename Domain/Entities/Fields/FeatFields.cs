namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record FeatFields(
    IReadOnlyList<string> Prerequisites,
    string Description,
    IReadOnlyList<string> Grants);