## Why

`ask_rules` already supports multi-hop retrieval (one scoped retrieval per named `ruleTopic`), and the
`chat-think-on-reasoning` change made qwen3 decompose multi-rule questions reliably enough to trigger it
(grapple-vs-prone now returns a grounded, cited answer). But coverage still depends on the model passing
a COMPLETE `ruleTopics` set, and probing shows it is ~80% reliable — it occasionally drops a topic (e.g.
"saving throws" dropped 2/3 on a paralyzed-saves-crits question) or omits `ruleTopics` entirely. When a
topic is dropped, that rule's passages are never retrieved in multi-hop mode, so the model reasons over
an incomplete rule set.

## What Changes

- In `RulesAdjudicationService.AskAsync` multi-hop mode (when `ruleTopics` is present), ALSO run one
  single-shot retrieval on the WHOLE question and merge its passages into the de-duplicated combined
  passage list — a deterministic, LLM-free safety net so a dropped topic's rule still surfaces via the
  broad whole-question pass.
- The per-topic groupings and the single-shot (no-`ruleTopics`) path are UNCHANGED. The whole-question
  passages are purely additive to the combined list (de-duped, highest score kept) — they can only add
  coverage, never remove a per-topic passage.

## Capabilities

### New Capabilities

<!-- None. -->

### Modified Capabilities

- `rules-adjudication`: the multi-hop contract gains a whole-question safety-net retrieval whose passages
  are merged into the combined de-duplicated list, so coverage no longer depends solely on the model
  supplying a complete `ruleTopics` set.

## Impact

- `Features/Rules/RulesAdjudicationService.cs` — add the whole-question retrieval to the multi-hop merge.
- `DndMcpAICsharpFun.Tests/Rules/*` — tests for the merge (whole-question-only passage appears in the
  combined list; per-topic groups unchanged; single-shot unchanged; dedup holds).
- No HTTP endpoint / DB / schema / persona change → no `.http`/insomnia update. `ask_rules`'s tool schema
  is unchanged (still `question, ruleTopics?, edition?`). Deployed via image rebuild.
- **Out of scope:** an LLM decomposition step; changing what qwen3 passes as `ruleTopics`; the
  soft-conclusion / list-iness of answers (a qwen3 quality ceiling, not a retrieval gap).
