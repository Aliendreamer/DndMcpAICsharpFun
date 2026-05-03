namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SpellComponents(bool V, bool S, bool M, string? Material = null);

public sealed record SpellFields(
    int Level,
    string School,
    string CastingTime,
    string Range,
    SpellComponents Components,
    string Duration,
    bool Ritual,
    bool Concentration,
    string Description,
    string? AtHigherLevels,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> DamageTypes);
