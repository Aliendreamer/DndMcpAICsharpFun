namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record PlaneFields(
    string Category,
    string Description,
    IReadOnlyList<string> RelatedPlanes);
