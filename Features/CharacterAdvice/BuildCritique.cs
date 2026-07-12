namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public enum CritiqueKind { UntakenChoice, StatConsistency, AbilityAlignment }

/// <summary>One grounded observation about the build, anchored to a concrete sheet fact and, where
/// relevant, a real cited rule (Cite). The assistant frames these into a critique — it does not judge.</summary>
public sealed record CritiqueFinding(CritiqueKind Kind, string Observation, CitedOption? Cite);

public sealed record BuildCritique(
    long HeroSnapshotId,
    IReadOnlyList<CritiqueFinding> Findings,
    IReadOnlyList<string> Strengths);
