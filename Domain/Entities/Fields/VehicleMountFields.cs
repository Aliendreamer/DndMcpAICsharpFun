namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record VehicleMountFields(
    string Kind,
    int? Speed,
    int? CapacityLb,
    string Description);
