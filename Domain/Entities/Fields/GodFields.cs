namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record GodFields(
    string Alignment,
    IReadOnlyList<string> Domains,
    string? Symbol,
    string? Pantheon,
    string? Plane,
    string Description);