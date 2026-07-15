## 1. Whole-question safety-net retrieval in multi-hop (TDD)

- [x] 1.1 In `DndMcpAICsharpFun.Tests/Rules/` (follow existing `RulesAdjudicationService` tests; use a
  fake `IRagRetrievalService` that returns DISTINCT passages keyed by query text), add tests:
  - **Whole-question passage surfaces:** call `AskAsync` with `ruleTopics=["grappling","prone condition"]`
    where the fake returns, for the WHOLE-QUESTION query, a passage returned by NEITHER topic. Assert
    that passage appears in the combined/merged passage list. Confirm this FAILS against the current code
    (which only merges per-topic passages) — RED.
  - **Per-topic groups unchanged:** assert each topic group contains ONLY its own topic's passages (the
    whole-question passage is NOT added to any topic group).
  - **Dedup holds:** a passage returned by both a topic and the whole-question query appears exactly once
    in the combined list.
  - **Single-shot unchanged:** `AskAsync` with no `ruleTopics` still does one retrieval on the question
    and returns empty per-topic groups (regression guard — should stay green).
- [x] 1.2 In `Features/Rules/RulesAdjudicationService.cs` multi-hop branch, after the per-topic loop,
  add `var whole = await RetrieveAsync(question, edition, RuleSources.TopK, ct);` and include `whole` in
  the `SelectMany(...).GroupBy((Text,SourceBook,Section)).Select(highest-score)` merge that builds the
  combined list. Do NOT add `whole` to `topicGroups`. Add a brief comment (why: deterministic safety net
  for an incomplete `ruleTopics` set). Tests GREEN.
- [x] 1.3 `dotnet build` 0/0; `dotnet test DndMcpAICsharpFun.Tests --filter "FullyQualifiedName~RulesAdjudication"`
  green; FULL `dotnet test` suite green. Commit.

## 2. Live validation + report

- [x] 2.1 Rebuild the app image (`docker compose up -d --build app`), wait healthy. Live smoke: a
  multi-rule question whose model decomposition tends to DROP a topic (e.g. "Does a paralyzed creature
  auto-fail Dexterity saves, and do melee hits crit?") → confirm via the persisted `ChatTurns` row that
  the answer grounds AND cites BOTH the paralyzed-condition rule and the crit/saves rule (the dropped
  topic's rule now surfaces via the whole-question pass), with no `<think>` leak. Validate via the DB
  (per dev-flow flaky-smoke guidance); check the app logs show the extra whole-question retrieval
  embedding. Re-confirm grapple-vs-prone still grounds+cites.
- [x] 2.2 Write the change report (`report.md`): the merge change, the tests, and the live-smoke result
  (dropped-topic rule now covered), plus the honest caveat that answer prose quality (soft conclusion /
  list-iness) is unchanged (qwen3 ceiling). Commit.
