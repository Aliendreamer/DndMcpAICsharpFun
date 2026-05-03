namespace DndMcpAICsharpFun.Domain.Entities.Fields;

public sealed record AbilityBonus(string Ability, int Bonus);

public sealed record RaceFields(
    string Size,
    int Speed,
    IReadOnlyList<AbilityBonus> AbilityBonuses,
    IReadOnlyList<string> Languages,
    IReadOnlyList<TraitRef> Traits,
    IReadOnlyList<string> Subraces);
