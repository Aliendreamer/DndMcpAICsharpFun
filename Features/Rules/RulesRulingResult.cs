using DndMcpAICsharpFun.Features.Lore; // CitedPassage

namespace DndMcpAICsharpFun.Features.Rules;

public sealed record RulesRulingResult(
    IReadOnlyList<CitedPassage> Passages,
    IReadOnlyCollection<string> ScopedBooks,
    IReadOnlyList<RuleTopicPassages> Topics);

/// <summary>The passages grounding one named rule in a multi-hop rules query.</summary>
public sealed record RuleTopicPassages(string Topic, IReadOnlyList<CitedPassage> Passages);
