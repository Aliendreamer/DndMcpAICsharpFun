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

/// <summary>
/// An admin's sign-off that an entity with failing checks is acceptable. <paramref name="WaivedChecks"/> names the
/// failed checks the admin saw and waived at approval time; a later run failing any other check sends the entity back
/// to review. A missing list (legacy approvals) waives nothing — such an approval must be re-done.
/// </summary>
public sealed record Approval(
    string EntityId,
    string Reason,
    DateTimeOffset At,
    IReadOnlyList<string>? WaivedChecks = null);

public sealed record EntityOverrides(IReadOnlyList<FieldOverride> Overrides, IReadOnlyList<Approval> Approvals)
{
    public static EntityOverrides Empty { get; } = new([], []);
}
