namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record WeaponFields(
    string Category,
    string WeaponType,
    int CostCp,
    double WeightLb,
    DamagePart Damage,
    AttackRange? Range,
    IReadOnlyList<string> Properties);
