using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record EntityCandidate(
    EntityType Type,
    string DisplayName,
    string Text,
    int? Page,
    IReadOnlyList<EntityType> TypePrior = null!)
{
    /// <summary>
    /// The ranked set of plausible entity types offered to the model as the discriminated-union
    /// branches (the decline branch is added by the union builder). <see cref="Type"/> remains the
    /// keyword-derived primary used for stable identity/checkpointing. Defaults to just the primary.
    /// </summary>
    public IReadOnlyList<EntityType> TypePrior { get; init; } = TypePrior ?? [Type];
}
