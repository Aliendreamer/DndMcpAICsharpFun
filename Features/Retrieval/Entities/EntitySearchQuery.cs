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
    int TopK,
    bool? Srd = null,
    bool? Srd52 = null,
    bool? BasicRules2024 = null,
    // spell-class-join: restrict to spells this class can cast (resolved via SpellClassIndex).
    string? CastableByClass = null);