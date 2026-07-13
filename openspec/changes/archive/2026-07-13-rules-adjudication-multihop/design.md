## Context

`rules-adjudication` (shipped) does one source-book-scoped retrieval per `ask_rules` call and returns
cited passages the persona frames into a ruling. Live probe (2026-07-13): the content-`category`
payload is unreliable for scoping (`category=Rule` returns Monster Manual "Swallow" abilities;
`Combat`/`Condition` are clean but each rule type maps to a different category and a miscategorized
rule would be dropped). So category-precision is rejected; multi-hop over the reliable source-book
scope is the robust way to ground a multi-rule question on every rule.

## Goals / Non-Goals

**Goals:**

- Ground every distinct rule a question involves by retrieving each separately.
- Keep it robust — no dependency on the noisy `category` tagging; reuse the shipped `SourceBooks`
  scope.
- Backward-compatible: single-shot stays the default when no topics are given.
- No new LLM call — the chat LLM does the decomposition as part of its tool use.

**Non-Goals:**

- Multi-category filter (rejected — noisy tagging).
- A decomposition LLM call inside the tool.
- Any ownership/campaign/user coupling (`ruleTopics` is a plain string array).
- Migration, HTTP route, `.http`/`.insomnia`, shared-key MCP surface.

## Decisions

### D1 — Optional `ruleTopics`; per-topic scoped retrieval + merge

`AskAsync(string question, IReadOnlyList<string>? ruleTopics, DndVersion? edition, CancellationToken ct)`:

- `ruleTopics` null/empty → one retrieval on `question` at `RuleSources.TopK` (v1, unchanged).
- `ruleTopics` given → for each topic, one `RetrievalQuery(topic, Version: edition, TopK:
  RuleSources.TopicTopK, SourceBooks: RuleSources.Books)`; build a `RuleTopicPassages(topic,
  passages)` per topic; the flat `Passages` = the deduped union across topics (dedup by
  `(Text, SourceBook, Section)`, keep max `Score`).

`RuleSources.TopicTopK` (≈5) keeps N topics from ballooning the passage count; the caller (LLM)
bounds the topic count.

### D2 — Additive result shape

`RulesRulingResult(IReadOnlyList<CitedPassage> Passages, IReadOnlyCollection<string> ScopedBooks,
IReadOnlyList<RuleTopicPassages> Topics)` with `RuleTopicPassages(string Topic,
IReadOnlyList<CitedPassage> Passages)`. Single-shot → `Topics` empty; `Passages` carries the
retrieval (existing tests still assert on `Passages`). Multi-hop → `Topics` grouped per rule, `Passages`
the deduped union, so the persona can cite each rule from its own group.

### D3 — Tool param + decomposition contract

`ask_rules(question, ruleTopics?, edition?)`; `ruleTopics` is a `string[]?`. Description adds: identify
the distinct rules the question involves and pass them as `ruleTopics` (e.g. `["grappling","prone
condition"]`) so each rule is grounded on its own; then cite each; omit for a simple single-rule
question. Still ownership-free — the schema exposes no `userId`/`campaignId`.

## Risks / Trade-offs

- **[N topics → N Qdrant calls]** → bounded by the LLM's topic count and `TopicTopK`; a handful of
  small retrievals. Acceptable.
- **[LLM omits `ruleTopics` on a multi-rule question]** → falls back to v1 single-shot at `TopK=10`,
  which already surfaced both rules in the v1 live smoke; multi-hop is an improvement, not a
  correctness dependency. The description nudges decomposition.
- **[Dedup collapses a passage that legitimately supports two rules]** → the flat `Passages` dedups
  (for the persona's convenience), but the per-topic `Topics` groups keep the passage under each rule
  it was retrieved for, so no grounding is lost.
- **[Result-shape change breaks existing tests]** → additive only (`Topics` appended); v1 tests assert
  `Passages` which is unchanged for single-shot.
