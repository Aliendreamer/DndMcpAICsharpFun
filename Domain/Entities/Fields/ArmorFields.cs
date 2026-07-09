namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record ArmorFields(
    string Category,
    int CostGp,
    double WeightLb,
    string AcFormula,
    int? StrengthRequirement,
    bool StealthDisadvantage);