using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// The result of building an encounter toward a target <see cref="Difficulty"/>: the shared
/// Assessor's rating of the final monster set, whether the target band was actually reached, and
/// — when it wasn't — an explanatory <see cref="Note"/>.
/// </summary>
public sealed record BuiltEncounter(EncounterAssessment Assessment, bool FullyMatched, string? Note);

/// <summary>
/// Builds an encounter toward a target difficulty band by greedily adding monster candidates and
/// re-rating the running set with the shared <see cref="EncounterAssessor"/> after every addition,
/// so a build can never disagree with what <see cref="EncounterAssessor.Assess"/> would report for
/// the very same monster list. Re-assessing after each addition also folds the 2014 monster-count
/// multiplier (which can jump the assessed band the moment a new monster is added) into the
/// stopping condition itself, instead of requiring the caller to reason about it separately.
/// </summary>
public sealed class EncounterGenerator(IEncounterMonsterSource source, EncounterAssessor assessor)
{
    /// <summary>Hard cap on monsters in a generated encounter — bounds the greedy loop.</summary>
    private const int MaxMonsters = 15;

    /// <summary>How many candidates to request from the source per build.</summary>
    private const int CandidateLimit = 50;

    /// <summary>
    /// Builds an encounter for <paramref name="partyLevels"/> toward <paramref name="target"/>.
    /// </summary>
    /// <param name="crLte">Optional CR upper bound for candidates; derived from the target
    /// difficulty band's own XP budget when omitted (see the derivation comment in the body).</param>
    /// <param name="crGte">Optional CR lower bound for candidates; a fixed low floor when omitted.</param>
    public async Task<BuiltEncounter> BuildAsync(
        IReadOnlyList<int> partyLevels,
        Difficulty target,
        DndVersion ed,
        string? theme,
        double? crLte,
        double? crGte,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(partyLevels);
        if (partyLevels.Count == 0)
        {
            throw new ArgumentException("Party must have at least one member.", nameof(partyLevels));
        }

        // 2024 has no Deadly band — EncounterMath.Classify never returns Difficulty.Deadly for
        // Edition2024, its top band is Hard. Left unclamped, a Deadly target under 2024 is
        // unsatisfiable: the greedy loop below would never see current.Difficulty == target and
        // would run all the way to MaxMonsters/candidate exhaustion, returning a misleading
        // "only N candidates" scarcity note instead of the true best-effort Hard result. Clamping
        // here targets the real top 2024 band; 2014 (which does have Deadly) is untouched.
        var effectiveTarget = ed == DndVersion.Edition2024 && target == Difficulty.Deadly
            ? Difficulty.Hard
            : target;

        // Default per-monster CR ceiling: when the caller doesn't pin one down, derive it from
        // the TARGET band's own total-party XP budget rather than from the party's average level.
        // The old avg-level cap (Math.Max(1, avgLevel)) excludes the single strong monster that
        // would naturally fill a Hard/Deadly encounter for a high-level party — a solo boss's
        // 2014 monster-count multiplier is 1.0, so its raw XP is its adjusted XP — so the build
        // silently fell back to a lower band instead of reaching the one requested. Picking the
        // highest CR whose EncounterMath.CrToXp does not exceed the target band's budget lets one
        // monster nearly fill the band without blowing past it (the greedy loop below still adds
        // more if one monster undershoots, and the overshoot guard still prevents exceeding the
        // target when there IS a band above it to overshoot into).
        // effectiveCrGte keeps a fixed, low floor (CR 1/8) rather than one scaled to party level:
        // low enough that low-target bands (built from several weak monsters) aren't starved of
        // candidates, but above CR 0 so the pool isn't dominated by zero-XP fodder.
        var budget = EncounterMath.PartyBudget(partyLevels, ed);
        var bandIndex = Math.Clamp((int)effectiveTarget - 1, 0, budget.Length - 1);
        var effectiveCrLte = crLte ?? EncounterMath.HighestCrAtOrBelowXp(budget[bandIndex]);
        var effectiveCrGte = crGte ?? 0.125;

        var candidates = await source.FindAsync(ed, effectiveCrGte, effectiveCrLte, theme, srdOnly: false, CandidateLimit, ct);

        var selected = new List<MonsterRef>();
        var remaining = new List<MonsterRef>(candidates);
        var current = assessor.Assess(partyLevels, selected, ed);

        // Tracks *why* the greedy loop stopped short of the target, so the fallback Note below
        // can explain the real reason instead of always blaming candidate scarcity: either every
        // remaining candidate was tried and rejected for overshooting past the target band, or
        // the loop simply ran out of candidates (or hit MaxMonsters) without overshooting.
        var overshootBlocked = false;
        Difficulty? overshootBand = null;

        // Bounded greedy: each iteration moves exactly one candidate from `remaining` into
        // `selected` (or breaks), so this can never loop more than min(MaxMonsters,
        // candidates.Count) times — no separate iteration counter is needed for termination.
        while (current.Difficulty != effectiveTarget && selected.Count < MaxMonsters && remaining.Count > 0)
        {
            MonsterRef? bestCandidate = null;
            EncounterAssessment? bestAssessment = null;
            Difficulty? roundOvershootBand = null;

            foreach (var candidate in remaining)
            {
                var trial = new List<MonsterRef>(selected) { candidate };
                var trialAssessment = assessor.Assess(partyLevels, trial, ed);

                if (trialAssessment.Difficulty > effectiveTarget)
                {
                    // Track the nearest band this (and other) rejected candidates would have
                    // jumped to, so the fallback Note can name it if every candidate overshoots.
                    if (roundOvershootBand is null || trialAssessment.Difficulty < roundOvershootBand)
                    {
                        roundOvershootBand = trialAssessment.Difficulty;
                    }

                    continue; // this candidate would overshoot past the target band
                }

                if (bestAssessment is null || trialAssessment.AdjustedXp > bestAssessment.AdjustedXp)
                {
                    bestCandidate = candidate;
                    bestAssessment = trialAssessment;
                }
            }

            if (bestCandidate is null || bestAssessment is null)
            {
                // remaining.Count > 0 here (the while guard ensures it), so every candidate this
                // round was rejected by the overshoot guard above — this is never candidate
                // scarcity.
                overshootBlocked = true;
                overshootBand = roundOvershootBand;
                break; // every remaining candidate would overshoot past the target band
            }

            selected.Add(bestCandidate);
            remaining.Remove(bestCandidate);
            current = bestAssessment;
        }

        var fullyMatched = current.Difficulty == effectiveTarget;
        var note = fullyMatched
            ? null
            : overshootBlocked
                ? $"Couldn't reach {effectiveTarget} without overshooting to {overshootBand}; " +
                  $"best achievable is {current.Difficulty} with {selected.Count} monster(s)."
                : $"Only {candidates.Count} candidate(s) in CR [{effectiveCrGte:0.###}, {effectiveCrLte:0.###}]; " +
                  $"best achievable is {current.Difficulty} with {selected.Count} monster(s).";

        return new BuiltEncounter(current, fullyMatched, note);
    }
}