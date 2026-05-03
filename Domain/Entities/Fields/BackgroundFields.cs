namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record BackgroundFields(
    IReadOnlyList<string> SkillProficiencies,
    IReadOnlyList<string> ToolProficiencies,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Equipment,
    string FeatureName,
    string FeatureSummary);
