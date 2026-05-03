using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

public sealed record EntitySearchQuery(
    string QueryText,
    EntityType? Type,
    string? SourceBook,
    string? Edition,
    string? BookType,
    string? SettingTag,
    string? Keyword,
    double? CrNumericLte,
    double? CrNumericGte,
    int? SpellLevel,
    string? DamageType,
    int TopK);
