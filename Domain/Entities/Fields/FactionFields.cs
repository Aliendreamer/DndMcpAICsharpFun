namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record FactionFields(
    string? Headquarters,
    IReadOnlyList<string> Goals,
    string Description);
