using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>A hand-correction of one field. <paramref name="Extracted"/> is the extractor's value the admin saw.</summary>
public sealed record FieldOverride(
    string EntityId,
    string Field,
    JsonElement Value,
    JsonElement? Extracted,
    string Reason,
    DateTimeOffset At,
    bool ExtractedAbsent = false);

/// <summary>An admin's sign-off that an entity with failing checks is acceptable.</summary>
public sealed record Approval(string EntityId, string Reason, DateTimeOffset At);

public sealed record EntityOverrides(IReadOnlyList<FieldOverride> Overrides, IReadOnlyList<Approval> Approvals)
{
    public static EntityOverrides Empty { get; } = new([], []);
}
