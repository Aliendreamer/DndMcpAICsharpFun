using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// A compact row in a filter-set result (entity-set-query): identifying + discriminating fields only,
/// NO canonical text or full field payload — a set answers "which ones", and the caller drills into a
/// specific entity via <c>get_entity</c>. The discriminators are best-effort from the entity's fields.
/// </summary>
public sealed record EntitySetRow(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    int? Page,
    string? Cr,
    int? SpellLevel,
    string? DamageType);

/// <summary>
/// A complete filter-set result. <see cref="Total"/> is the TRUE match count; <see cref="Returned"/>
/// is how many rows are included (capped). When Total &gt; Returned the set is truncated — callers
/// must surface that (never present a capped set as complete).
/// </summary>
public sealed record EntitySetResult(
    int Total,
    int Returned,
    IReadOnlyList<EntitySetRow> Rows);
