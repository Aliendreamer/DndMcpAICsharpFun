namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record TrapFields(
    string Difficulty,
    int? DetectDc,
    int? DisarmDc,
    string Description);
