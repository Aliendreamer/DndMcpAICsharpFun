using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// Rates a party+monster list encounter, pure over <see cref="EncounterMath"/>.
/// </summary>
public sealed class EncounterAssessor
{
    public EncounterAssessment Assess(IReadOnlyList<int> partyLevels, IReadOnlyList<MonsterRef> monsters, DndVersion ed)
    {
        ArgumentNullException.ThrowIfNull(partyLevels);
        ArgumentNullException.ThrowIfNull(monsters);

        var total = monsters.Sum(m => m.Xp);
        var multiplier = EncounterMath.Multiplier(monsters.Count, partyLevels.Count, ed);
        var adjusted = (int)Math.Round(total * multiplier, MidpointRounding.AwayFromZero);
        var difficulty = EncounterMath.Classify(total, partyLevels, monsters.Count, ed);
        var budget = EncounterMath.PartyBudget(partyLevels, ed);

        IReadOnlyList<BandBoundary> boundaries = ed == DndVersion.Edition2014
            ?
            [
                new BandBoundary(Difficulty.Easy, budget[0]),
                new BandBoundary(Difficulty.Medium, budget[1]),
                new BandBoundary(Difficulty.Hard, budget[2]),
                new BandBoundary(Difficulty.Deadly, budget[3])
            ]
            :
            [
                new BandBoundary(Difficulty.Easy, budget[0]),
                new BandBoundary(Difficulty.Medium, budget[1]),
                new BandBoundary(Difficulty.Hard, budget[2])
            ];

        return new EncounterAssessment(difficulty, total, adjusted, boundaries, monsters);
    }
}
