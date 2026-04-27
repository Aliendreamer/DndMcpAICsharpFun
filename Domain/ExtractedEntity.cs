using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Domain;

public sealed record ExtractedEntity(
    int Page,
    string SourceBook,
    string Version,
    bool Partial,
    string Type,
    string Name,
    JsonObject Data);
