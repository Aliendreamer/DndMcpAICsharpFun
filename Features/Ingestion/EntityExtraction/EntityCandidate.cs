using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed record EntityCandidate(
    EntityType Type,
    string DisplayName,
    string Text,
    int? Page);
