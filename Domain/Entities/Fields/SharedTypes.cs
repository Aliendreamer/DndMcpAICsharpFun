namespace DndMcpAICsharpFun.Domain.Entities.Fields;

// Shared supporting types used across multiple Fields records.

public sealed record AttackRange(int Normal, int? Long);

public sealed record DamagePart(string Dice, int Average, string Type, string? Versatile = null);