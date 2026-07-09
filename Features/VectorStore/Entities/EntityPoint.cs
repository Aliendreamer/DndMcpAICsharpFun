using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.VectorStore.Entities;

public sealed record EntityPoint(EntityEnvelope Envelope, float[] Vector, string FileHash);

public sealed record EntitySearchHit(EntityEnvelope Envelope, float Score, string PointId);