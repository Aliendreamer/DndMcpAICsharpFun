using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed record EntitySearchResult(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    string Edition,
    int? Page,
    IReadOnlyList<string> SettingTags,
    string Snippet,
    float Score);

public sealed record EntityDiagnosticResult(
    string Id,
    EntityType Type,
    string Name,
    string SourceBook,
    string Edition,
    int? Page,
    IReadOnlyList<string> SettingTags,
    string PointId,
    System.Text.Json.JsonElement Fields,
    float Score);