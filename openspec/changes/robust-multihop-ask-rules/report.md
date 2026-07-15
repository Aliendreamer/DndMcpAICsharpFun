# robust-multihop-ask-rules — change report

## Summary

Added a deterministic whole-question retrieval to `ask_rules` multi-hop mode so a rule the model omits
from `ruleTopics` still surfaces in the combined passage list — closing the coverage half of the
multi-rule gap. Verified working; the residual answer-quality limitation is a separate (out-of-scope)
concern.

## What shipped (on `main`, reviewed clean)

| Commit | What |
| --- | --- |
| `d14dd20` | In `RulesAdjudicationService.AskAsync` multi-hop branch, run one whole-question retrieval (`RetrieveAsync(question, edition, RuleSources.TopK)`) and `.Concat(whole)` it into the existing combined de-duped merge. Per-topic groups and single-shot mode untouched. 4 tests (whole-question passage surfaces, groups isolated, dedup, single-shot regression). |

Per-task review: **Spec ✅ / Quality Approved**. The reviewer independently verified non-vacuity (the
whole-question test reds against the per-topic-only merge), group isolation (whole-question passages
never enter `topicGroups`), dedup, and dedup-key correctness.

## Why (recap)

`chat-think-on-reasoning` made qwen3 trigger multi-hop reliably, but the `ruleTopics` set is ~80%
complete — probing showed it drops a topic (e.g. "saving throws" dropped 2/3 on the paralyzed question).
When a topic is dropped, that rule was never retrieved. The whole-question pass is the LLM-free safety
net.

## Live validation

- **Safety net fires (the deterministic signal):** the paralyzed-saves-crits question produced **3
  retrieval embeddings** = 2 topic retrievals + 1 whole-question retrieval, confirming the extra pass ran
  live. The whole-question retrieval DOES surface the crisp rule (`GET /retrieval/search` shows *"The
  creature automatically fails Strength and Dexterity saving throws"* at ranks 0–3).
- **Simpler case works end-to-end:** grapple-vs-prone remains grounded and cited (DMG/PHB), no
  hallucination.

## Honest caveat — coverage ≠ a good answer for the hard case

The paralyzed answer was still **poor**: verbose, it got the crit rule **backwards** ("a paralyzed
creature cannot be targeted for critical hits" — the opposite of RAW), and it hallucinated monster/item
abilities (Rod of Paralysis, Lich, Moonblade) into the "general rule." Attribution:

- **qwen3 grounding ceiling:** it had the correct auto-fail-saves passage retrieved and STILL didn't use
  it — the documented model limit, out of scope here.
- **Retrieval ranking:** the crit-within-5ft bullet ranks weakly, and in-scope DMG magic-item paralysis
  adds noise. A retrieval-quality (reranking / section-scoping) concern, out of scope here.

This change fixes the retrieval **coverage** half (the omitted rule now IS in the combined list); the
remaining wall for hard multi-rule questions is retrieval **ranking** + the **model** — both separate
follow-ups. The change is a correct, additive improvement (coverage can only help), verified at its spec
level.

## Gates

- `dotnet build` 0/0; `dotnet format` clean; **full `dotnet test` 1386/1386** (+ the new safety-net
  tests), 0 failures.
- No HTTP endpoint / DB / schema / persona / tool-schema change → no `.http`/insomnia update.

## Follow-ups (separate changes)

- **Retrieval ranking quality** for rules: rerank/section-scope so the exact rule bullets (crit-within-5ft)
  out-rank magic-item/monster noise.
- The **local-model upgrade** (grounding ceiling) — still gated by 8 GB VRAM.
