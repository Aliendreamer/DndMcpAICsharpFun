using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Encounters;

/// <summary>
/// The result of building an encounter toward a target <see cref="Difficulty"/>: the shared
/// Assessor's rating of the final monster set, whether the target band was actually reached,
/// — when it wasn't — an explanatory <see cref="Note"/>, and the resolved <see cref="PartyLevels"/>
/// actually used for the build (so callers that only pass a campaignId/heroes can still surface
/// the concrete levels, e.g. in a campaign-log payload).
/// </summary>
public sealed record BuiltEncounter(
    EncounterAssessment Assessment,
    bool FullyMatched,
    string? Note,
    IReadOnlyList<int> PartyLevels);

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

        // Each bound above falls back to its OWN independent default, so a caller who pins down
        // only one of crLte/crGte can end up with an inverted range once the other bound
        // defaults: e.g. a low-level party at a low difficulty derives a low default crLte, and
        // an explicit high crGte (the MCP tool's minCr) then exceeds it. Passed straight to the
        // source, that inverted range silently returns zero candidates and produces a confusing
        // "0 candidate(s) in CR [gte, lte]" note instead of honoring the caller's explicit bound.
        // Policy: if BOTH bounds were caller-supplied and are still inverted, that is invalid
        // caller input — fail loudly rather than degrade silently. If only ONE bound was
        // caller-supplied, widen/lower the DEFAULTED bound to meet it, so the explicit bound is
        // always honored (this can also fire when both bounds defaulted to an inverted pair —
        // e.g. an extremely small budget — in which case the floor is lowered to match).
        if (effectiveCrGte > effectiveCrLte)
        {
            if (crGte is not null && crLte is not null)
            {
                throw new ArgumentException("minCr cannot exceed maxCr.", nameof(crGte));
            }

            if (crGte is not null)
            {
                effectiveCrLte = effectiveCrGte;
            }
            else
            {
                effectiveCrGte = effectiveCrLte;
            }
        }

        var candidates = await source.FindAsync(ed, effectiveCrGte, effectiveCrLte, theme, srdOnly: false, CandidateLimit, ct);

        var selected = new List<MonsterRef>();
        var current = assessor.Assess(partyLevels, selected, ed);

        // The XP of the first successful pick — the anchor (boss). Until it is set, the anchor is
        // still being chosen; after it is set, the fill phase prefers strictly-cheaper minions.
        int? anchorXp = null;

        // Tracks *why* the greedy loop stopped short of the target, so the fallback Note can explain
        // the real reason: either every eligible candidate would overshoot the target band, or the
        // pool was empty / the cap was hit without overshooting.
        var overshootBlocked = false;
        Difficulty? overshootBand = null;

        // Bounded greedy with re-selection (no candidate is removed, so the same monster can be
        // picked multiple times → quantity). The loop still runs at most MaxMonsters times because
        // each iteration either adds exactly one monster or breaks. Anchor-then-fill shape: the
        // first pick is the single highest-XP candidate that does not overshoot (the boss); after
        // that the eligible pool narrows to candidates strictly cheaper than the anchor (the
        // minions), falling back to the full pool when nothing is cheaper so a same-CR pool still
        // stacks into a uniform swarm.
        while (current.Difficulty != effectiveTarget && selected.Count < MaxMonsters)
        {
            IEnumerable<MonsterRef> eligible = anchorXp is null
                ? candidates
                : candidates.Any(c => c.Xp < anchorXp.Value)
                    ? candidates.Where(c => c.Xp < anchorXp.Value)
                    : candidates;

            MonsterRef? bestCandidate = null;
            EncounterAssessment? bestAssessment = null;
            Difficulty? roundOvershootBand = null;

            foreach (var candidate in eligible)
            {
                var trial = new List<MonsterRef>(selected) { candidate };
                var trialAssessment = assessor.Assess(partyLevels, trial, ed);

                if (trialAssessment.Difficulty > effectiveTarget)
                {
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
                // No monster could be added this round. If there were candidates at all, every one
                // would have overshot the target band; if the pool was empty, this is scarcity.
                overshootBlocked = candidates.Count > 0;
                overshootBand = roundOvershootBand;
                break;
            }

            selected.Add(bestCandidate);
            anchorXp ??= bestCandidate.Xp; // the first successful pick becomes the anchor
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

        return new BuiltEncounter(current, fullyMatched, note, partyLevels);
    }
}