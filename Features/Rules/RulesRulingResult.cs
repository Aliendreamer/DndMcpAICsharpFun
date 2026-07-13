using DndMcpAICsharpFun.Features.Lore; // CitedPassage

namespace DndMcpAICsharpFun.Features.Rules;

public sealed record RulesRulingResult(
    IReadOnlyList<CitedPassage> Passages,
    IReadOnlyCollection<string> ScopedBooks);
