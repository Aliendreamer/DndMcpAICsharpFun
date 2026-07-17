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
    float Score,
    // Authority label (extraction-authority-ladder T3): canon / canon-unindexed /
    // verified-thirdparty / homebrew. Surfaced so consumers can down-weight or filter on provenance.
    string Authority = "");

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
    float Score,
    string Authority = "");