## Context

`DeclineRecovery.TryRecoverAsync` rebinds a declined candidate to `[Rule, Lore]` and re-runs the
extraction runner with a recovery-framed system prompt:

```csharp
var rebound = candidate with { TypePrior = new[] { EntityType.Rule, EntityType.Lore } };
var (envelope, _) = await runner.ExtractOneAsync(..., systemPromptOverride: prompt);
if (envelope is null || envelope.Disposition != EntityDisposition.Accepted) return null;
```

There is no input filter. Every still-declined candidate gets a model call, and the only defence
against a heading being admitted is the prompt asking for `none`. The class is otherwise careful — it
admits only `Accepted`, and it re-derives an honest id from the disposed type — so the weakness is
specifically the absence of an input gate, not the recovery logic.

Measured on the shipped corpus: GGR 97 recovered entities, all `Lore`/`Rule`, all headings; EGW 10;
SCC 12. The trigger is book shape — a bestiary and item chapter organised into per-guild subsections
yields dozens of `<Guild> Creatures` / `<Guild> Items` / `<Guild> NPCs` headings, each of which the
scanner emits as a candidate, the main gate declines, and this pass then resurrects.

## Goals / Non-Goals

**Goals:**

- Stop the pass admitting section and category headings, deterministically rather than by model judgment.
- Keep genuine rule/lore prose and named NPCs recovering exactly as today.
- Make suppressions visible in the log.
- Avoid the wasted LLM call — 97 on GGR — rather than filtering after the fact.

**Non-Goals:**

- Changing the recovery prompt, the grounding cascade, the disposition policy, or which dispositions
  are admitted. The pass's *output* handling is correct; only its *input* is unguarded.
- Touching the main extraction gate or `ExtractionSignatures.IsEntityLikeName`.
- Retro-cleaning canonical files. Already done by hand for the three affected books.

## Decisions

**The guard lives in the recovery pass, not in the shared `IsEntityLikeName` gate.** These patterns are
too aggressive for the primary path: a real entity can legitimately be named with a category-plural
suffix, and `IsEntityLikeName` is deliberately conservative ("rejects only clear non-entities; when
unsure returns true") because a false positive there destroys a real entity outright. In the recovery
pass the asymmetry inverts — every candidate arriving has *already been declined*, so a false positive
merely leaves it declined, while a false negative fabricates an entity. Same patterns, opposite
risk profile, therefore a separate predicate.

**Deterministic pre-filter, not a better prompt.** Prompt tuning is the lever this repo has repeatedly
found to be capped for qwen3:8b — real for routing, near-zero for judgment. Whether `Boros Creatures`
is a heading is decidable from the string; it should not cost a model call or depend on one.

**Bare-plural detection is vocabulary-based and length-bounded.** The rule matches a name that consists
*entirely* of one or two known creature-type plurals (optionally joined by `and`), rather than any
name ending in `s`. `Zombies` is rejected, `Zombie` is not, and a real entity that merely happens to
end in `s` is untouched. Anchoring on the whole name is what keeps `Sphinxes of Judgment`-style real
titles from being caught by a naive suffix rule — that heading is instead caught (correctly) by nothing,
and stays a judgment call for the model, which is acceptable: the goal is removing the systematic
failure, not every last heading.

**Log every rejection.** A pass that silently drops candidates is indistinguishable from one that
found nothing — the same traceless-drop failure mode this repo has been bitten by before. The existing
scanner logs its skips for exactly this reason.

## Risks / Trade-offs

**A real entity could be suppressed.** Mitigated structurally: the candidate was already declined by
the main gate, so the guard cannot remove anything the pipeline was otherwise keeping. The worst case
is a rule/lore passage that would have been recovered is not — a recall loss bounded to content the
primary path already rejected, and visible in the new log line.

**The vocabulary list is hand-maintained.** It is the standard D&D creature-type set plus the observed
category suffixes, and a book inventing a new category word will slip through. That is acceptable: the
guard is a floor, not a proof, and the model still gets the `none` option for anything it does not
catch. This is explicitly *not* an attempt to enumerate every heading shape — that would be the
whack-a-mole pattern this repo has already been burned by.

**Unit tests can fully cover this one.** Unlike the extraction changes, the guard is a pure string
predicate with no model, corpus or infrastructure dependency, so a fixture over the real observed GGR
names is a complete test — the 97 actual names are the specification. The only live check worth doing
is confirming a re-extract of GGR no longer produces them.
