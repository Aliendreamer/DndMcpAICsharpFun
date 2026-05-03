namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record SubraceFields(
    string ParentRace,
    IReadOnlyList<AbilityBonus> AbilityBonuses,
    IReadOnlyList<TraitRef> Traits);
