namespace DndMcpAICsharpFun.Features.Retrieval;

/// <summary>
/// A candidate produced by fused cross-channel retrieval, carrying its source
/// ("prose" from dnd_blocks, or "entity" from dnd_entities) plus enough
/// data for the caller (MCP tool, re-ranker, etc.) to use it.
/// </summary>
public sealed record FusedCandidate(
    string Source,   // "prose" or "entity"
    string Id,
    string Title,
    string Text,
    double Score);