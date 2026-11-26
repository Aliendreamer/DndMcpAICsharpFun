using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

/// <summary>An entity with one vector per piece of its text.</summary>
public sealed record EntityPoint(EntityEnvelope Envelope, IReadOnlyList<float[]> Vectors, string FileHash)
{
    public EntityPoint(EntityEnvelope Envelope, float[] Vector, string FileHash)
        : this(Envelope, (IReadOnlyList<float[]>)[Vector], FileHash)
    {
    }
}

/// <param name="PieceVectors">The stored vectors of the entity's text pieces, in order, when the search returned them.</param>
public sealed record EntitySearchHit(
    EntityEnvelope Envelope, float Score, string PointId, IReadOnlyList<float[]>? PieceVectors = null);