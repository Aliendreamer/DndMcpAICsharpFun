## Context

`RulesAdjudicationService.AskAsync(question, ruleTopics, edition, ct)` (no LLM; retrieves via
`IRagRetrievalService`, scoped to `RuleSources.Keys = ["PHB","DMG"]`):

- `ruleTopics` empty → single-shot: `RetrieveAsync(question, TopK=10)`; per-topic groups empty.
- `ruleTopics` present → multi-hop: one `RetrieveAsync(topic, TopicTopK=5)` per topic; returns per-topic
  groups AND a combined list de-duped by `(Text, SourceBook, Section)` keeping the highest-scoring copy.

Live/probe findings from `chat-think-on-reasoning`: think-on makes qwen3 trigger multi-hop reliably, but
the `ruleTopics` set is ~80% complete — it occasionally drops a topic, so that rule's passages never get
retrieved and the combined list misses it.

## Goals / Non-Goals

**Goals:**

- Make multi-hop coverage robust to an incomplete `ruleTopics` set, deterministically and without an LLM
  call, by adding a whole-question retrieval to the combined list.

**Non-Goals:**

- No LLM decomposition step; no change to what qwen3 passes; no change to the `ask_rules` tool schema.
- No change to single-shot (no-`ruleTopics`) behaviour or to the per-topic grouping.
- Not fixing answer prose quality (soft conclusion, list-iness) — that is a qwen3 ceiling, not retrieval.

## Decisions

1. **Add the whole-question pass to the multi-hop merge, not to the groups.** In the multi-hop branch,
   after the per-topic loop, call `RetrieveAsync(question, edition, RuleSources.TopK, ct)` and feed its
   passages into the same `SelectMany(...).GroupBy((Text,SourceBook,Section)).Select(highest-score)`
   merge that already produces the combined list. `topicGroups` is built solely from the per-topic
   retrievals and is untouched — so the model still sees each named rule under its own topic, and the
   whole-question passages only broaden the combined list.
2. **Purely additive, dedup-safe.** Because the merge de-dupes by citation identity and keeps the
   highest score, a passage returned by both a topic and the whole-question appears once; a passage only
   the whole-question returns is added. It can never remove a per-topic passage — so it cannot regress
   the grapple/prone coverage that already works.
3. **`TopK` for the whole-question pass = `RuleSources.TopK` (10)**, identical to single-shot, so the
   safety net has the same breadth as the default path. Total combined size is bounded
   (`topics × 5 + 10`, de-duped) — fine for the think-on context.
4. **No LLM, no schema change.** `RulesAdjudicationService` stays LLM-free; `ask_rules` keeps its
   `(question, ruleTopics?, edition?)` signature. The change is invisible to the model and the tool
   contract — only the returned combined list is broader.

## Risks / Trade-offs

- **One extra retrieval per multi-hop call** (~an embed + vector search). Negligible next to think-on
  composition latency, and only on multi-rule (`ruleTopics`-bearing) questions.
- **Slightly larger passage set to the model.** Bounded and de-duped; the per-topic grouping still
  foregrounds the named rules, so the extra whole-question passages aid rather than dilute (the model
  already handled the combined+grouped shape).
- **Does not fix a totally wrong `ruleTopics`** (e.g. the model names irrelevant rules) — but the
  whole-question pass still retrieves on the actual question text, which is the robust anchor.
