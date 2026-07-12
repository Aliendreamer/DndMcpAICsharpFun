namespace DndMcpAICsharpFun.Features.CharacterAdvice;

/// <summary>Grounded build-option package for a concept + chosen class — the assistant composes the
/// actual build FROM these cited menus (never invents). Not the final recommendation.</summary>
public sealed record BuildRecommendation(
    bool ClassInCorpus,
    string ClassName,
    string? HitDie,                 // e.g. "d10", null if unknown
    string? SpellcastingAbility,    // e.g. "int"/"wis"/"cha", null for non-casters
    IReadOnlyList<string> SaveProficiencies,
    string? SubclassTitle,          // e.g. "Martial Archetype"
    IReadOnlyList<CitedOption> Subclasses,
    IReadOnlyList<CitedOption> Feats,
    IReadOnlyList<CitedOption> Spells,
    IReadOnlyList<string> AvailableClasses)   // populated when ClassInCorpus is false
{
    public static BuildRecommendation NotInCorpus(string className, IReadOnlyList<string> available) =>
        new(false, className, null, null, [], null, [], [], [], available);
}
