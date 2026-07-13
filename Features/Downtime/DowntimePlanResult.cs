using DndMcpAICsharpFun.Features.Lore; // CitedPassage

namespace DndMcpAICsharpFun.Features.Downtime;

public sealed record DowntimePlanResult(
    IReadOnlyList<CitedPassage> Passages, IReadOnlyCollection<string> ScopedBooks);
