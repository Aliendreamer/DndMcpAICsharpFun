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

public sealed record EntitySearchHit(EntityEnvelope Envelope, float Score, string PointId);