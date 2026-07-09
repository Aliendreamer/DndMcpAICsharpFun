using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// Retrieves monster candidates for encounter building: filtered by edition, a CR range, an
/// optional theme, and (when true) restricted to SRD-only content. Backed by the structured
/// entity store in production; a hand-written fake stands in for it in
/// <c>EncounterGeneratorTests</c>.
/// </summary>
public interface IEncounterMonsterSource
{
    Task<IReadOnlyList<MonsterRef>> FindAsync(
        DndVersion ed,
        double crGte,
        double crLte,
        string? theme,
        bool srdOnly,
        int limit,
        CancellationToken ct);
}
