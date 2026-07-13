## Why

The shipped `ask_rules` does one single-shot retrieval for the whole question. A question spanning
several rules (grapple + prone + unarmed strike) relies on all of them landing in one top-K result
set; a rule that ranks below the cut-off is silently missed, so the ruling grounds on only part of
the interaction. Live probing also showed the content-`category` tagging is too noisy to scope by
(the `Rule` category returns monster abilities), ruling out category-precision as the fix. Multi-hop
decomposition — retrieve each distinct rule the question involves separately — grounds every
interacting rule independently, using the reliable shipped source-book scope.

## What Changes

- **`ask_rules(question, ruleTopics?, edition?)`** — the tool gains an optional `ruleTopics` string
  array. The chat LLM identifies the distinct rules the question involves and passes them; the tool
  runs **one scoped retrieval per topic** (each at a smaller per-topic `TopicTopK`), all scoped to
  the core rulebooks via the shipped `SourceBooks` filter, so every named rule gets its own grounded
  retrieval regardless of ranking. Omitting `ruleTopics` keeps the v1 single-shot behavior
  (backward-compatible).
- **`RulesAdjudicationService.AskAsync(question, ruleTopics, edition, ct)`** — per-topic retrieval +
  merge: a deduped flat `Passages` union AND a per-topic grouping so the LLM sees which passages
  ground which rule. No new LLM call (the chat LLM does the decomposition).
- **Result shape (additive):** `RulesRulingResult` gains `IReadOnlyList<RuleTopicPassages> Topics`
  (empty for single-shot); the existing `Passages`/`ScopedBooks` are unchanged.

## Capabilities

### New Capabilities

<!-- None. -->

### Modified Capabilities

- `rules-adjudication`: the `ask_rules` tool + service gain optional multi-hop (`ruleTopics`)
  per-rule retrieval; single-shot remains the default.

## Impact

- **Code:** `Features/Rules/` — `RulesAdjudicationService.AskAsync` gains a `ruleTopics` param and
  per-topic retrieval/merge; `RulesRulingResult` gains `Topics`; new `RuleTopicPassages` record;
  `RuleSources.TopicTopK`. `Features/Chat/DndChatService.cs` — `ask_rules` gains the `ruleTopics`
  param + description guidance.
- **No** migration, HTTP route, `.http`/`.insomnia`, or shared-key MCP change. Reuses the shipped
  `RetrievalQuery.SourceBooks` filter unchanged. Category-scoping is explicitly NOT pursued (noisy
  tagging).
