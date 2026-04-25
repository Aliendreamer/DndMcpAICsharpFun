using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Retrieval;

public record RetrievalResult(
    string Text,
    ChunkMetadata Metadata,
    float Score);

public sealed record RetrievalDiagnosticResult(
    string Text,
    ChunkMetadata Metadata,
    float Score,
    string PointId) : RetrievalResult(Text, Metadata, Score);
