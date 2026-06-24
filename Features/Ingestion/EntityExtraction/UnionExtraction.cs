using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>Outcome of a content-first union extraction for one candidate.</summary>
public enum UnionOutcome
{
    /// <summary>The model selected a type branch; <see cref="UnionExtraction.Type"/> and <see cref="UnionExtraction.Fields"/> are valid.</summary>
    Typed,

    /// <summary>The model chose the <c>none</c> branch; <see cref="UnionExtraction.DeclineReason"/> is valid.</summary>
    Declined,

    /// <summary>No valid output after retries; <see cref="UnionExtraction.ErrorMessage"/> is valid.</summary>
    Failed,
}

/// <summary>
/// Result of <see cref="CandidateExtractor.ExtractUnionAsync"/> — the model-selected type and merged
/// fields, a decline, or a failure. Keeps the orchestrator free of response-parsing concerns.
/// </summary>
public sealed record UnionExtraction(
    UnionOutcome Outcome,
    EntityType Type,
    JsonElement Fields,
    string? DeclineReason,
    string? Confidence,
    string? ErrorMessage)
{
    public static UnionExtraction Typed(EntityType type, JsonElement fields, string? confidence) =>
        new(UnionOutcome.Typed, type, fields, null, confidence, null);

    public static UnionExtraction Decline(string? reason) =>
        new(UnionOutcome.Declined, default, default, reason, null, null);

    public static UnionExtraction Failure(string? error) =>
        new(UnionOutcome.Failed, default, default, null, null, error);
}
