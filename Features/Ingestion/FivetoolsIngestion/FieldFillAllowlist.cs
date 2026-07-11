using System.Collections.Frozen;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Per-type STRUCTURED-field allowlist for the 5etools field-fill. Structured mechanics only —
/// never <c>entries</c>/prose (extraction owns content). Covering a type = adding an entry here.</summary>
public static class FieldFillAllowlist
{
    private static readonly FrozenDictionary<EntityType, IReadOnlySet<string>> Map =
        new Dictionary<EntityType, IReadOnlySet<string>>
        {
            [EntityType.Class] = Set("hd", "classFeatures", "subclassTitle", "proficiency", "spellcastingAbility", "casterProgression", "classTableGroups", "startingProficiencies", "multiclassing"),
            [EntityType.Subclass] = Set("subclassFeatures", "subclassTableGroups", "spellcastingAbility", "casterProgression"),
            [EntityType.Spell] = Set("level", "school", "range", "components", "duration", "time", "classes"),
            [EntityType.Monster] = Set("environment", "traitTags", "senseTags", "languageTags"),
        }.ToFrozenDictionary();

    public static IReadOnlySet<string>? For(EntityType type) => Map.TryGetValue(type, out var s) ? s : null;

    private static IReadOnlySet<string> Set(params string[] fields) => fields.ToFrozenSet();
}
