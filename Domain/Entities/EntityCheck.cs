namespace DndMcpAICsharpFun.Domain.Entities;

/// <summary>One deterministic self-check run by the programmatic extractor against an entity.</summary>
public sealed record EntityCheck(string Name, bool Passed, string Reason);
