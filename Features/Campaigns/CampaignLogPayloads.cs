namespace DndMcpAICsharpFun.Features.Campaigns;

public sealed record RollLogPayload(
    string Expression,
    string Breakdown,
    int Total,
    IReadOnlyList<int> Dice,
    IReadOnlyList<int> Kept,
    string Mode);

public sealed record EncounterMonsterLog(string Id, string Name, double Cr, int Xp);

public sealed record EncounterLogPayload(
    string Difficulty,
    int TotalXp,
    int AdjustedXp,
    IReadOnlyList<int> PartyLevels,
    IReadOnlyList<EncounterMonsterLog> Monsters,
    bool FullyMatched,
    string? Note);