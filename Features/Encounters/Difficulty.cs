namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// Encounter difficulty band. The 2024 DMG's three-tier scale (Low/Moderate/High) maps onto
/// this five-value scale as Low→Easy, Moderate→Medium, High→Hard; there is no 2024 "Deadly"
/// band — High is the top band. Anything below the lowest configured threshold is Trivial.
/// </summary>
public enum Difficulty
{
    Trivial,
    Easy,
    Medium,
    Hard,
    Deadly
}
