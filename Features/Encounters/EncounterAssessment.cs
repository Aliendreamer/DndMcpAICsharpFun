namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// A single monster's identity and XP contribution to an encounter, as evaluated by
/// <see cref="EncounterAssessor"/>.
/// </summary>
public sealed record MonsterRef(string Id, string Name, double Cr, int Xp);

/// <summary>
/// The minimum total monster XP (post-multiplier for 2014, raw for 2024) required to reach
/// <see cref="Band"/>, for a specific party.
/// </summary>
public sealed record BandBoundary(Difficulty Band, int MinXp);

/// <summary>
/// The result of rating a party+monster list encounter: its difficulty, XP totals, the party's
/// band boundaries (so a caller can say "Deadly starts at N"), and the monsters assessed.
/// </summary>
public sealed record EncounterAssessment(
    Difficulty Difficulty,
    int TotalMonsterXp,
    int AdjustedXp,
    IReadOnlyList<BandBoundary> Boundaries,
    IReadOnlyList<MonsterRef> Monsters);