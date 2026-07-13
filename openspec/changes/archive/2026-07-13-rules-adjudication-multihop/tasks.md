## 1. Service multi-hop + result shape

- [ ] 1.1 (TDD) Tests: `AskAsync` with `ruleTopics=["grappling","prone condition"]` runs ONE scoped retrieval per topic (each `SourceBooks`=RuleSources.Books, `TopK`=RuleSources.TopicTopK), returns a `Topics` group per topic + a de-duped flat `Passages` union; `ruleTopics` null/empty → single-shot on the question (v1, `Topics` empty, `Passages` = the retrieval); dedup keeps max score and the per-topic groups retain overlapping passages
- [ ] 1.2 Add `RuleSources.TopicTopK` (≈5); `RuleTopicPassages(string Topic, IReadOnlyList<CitedPassage> Passages)`; extend `RulesRulingResult` with `IReadOnlyList<RuleTopicPassages> Topics` (additive); change `RulesAdjudicationService.AskAsync` to `(question, IReadOnlyList<string>? ruleTopics, edition, ct)` — per-topic retrieval + merge; single-shot path unchanged. Update the v1 service tests that call `AskAsync` to pass `ruleTopics: null` (behavior-preserving)

## 2. ask_rules tool param

- [ ] 2.1 Add the `ruleTopics` (`string[]?`) param to the `ask_rules` delegate in `DndChatService`, pass it through to `AskAsync`; extend the description to guide the LLM to decompose the question into `ruleTopics` for per-rule grounding (omit for single-rule)
- [ ] 2.2 (TDD) Update/extend the chat guard test: `ask_rules` schema still exposes NO `userId`/`campaignId` (now also has `ruleTopics`/`question`/`edition`); a routing test that invoking it with `ruleTopics` reaches the service and returns a `RulesRulingResult`

## 3. Real-Qdrant multi-hop non-vacuity test

- [ ] 3.1 Extend/add a real-Qdrant test: seed a grappling rule block + a prone rule block (both `source_book`="PlayerHandbook 2014") + an off-scope Monster Manual block; call `AskAsync("...", ruleTopics: ["grappling","prone"], null, ct)`; assert the flat `Passages` include BOTH rule blocks and EXCLUDE MM, and `Topics` has one group per topic each carrying its rule block. Reuse the shipped `RulesScopingIntegrationTests` seeding pattern (GUID collection; give the two rule blocks embeddings that rank each under its own topic query, or seed identical vectors and assert per-topic retrieval still returns them)

## 4. Verification

- [ ] 4.1 Build 0/0 + full `dotnet test` green (real Postgres + Qdrant) — the FULL suite, not a filter (dev-flow: value/shape changes can break sibling tests)
- [ ] 4.2 Rebuild app container; live smoke (Ollama): ask "can I grapple a creature that's already prone?" → the LLM passes `ruleTopics` (grappling + prone), the ruling grounds on BOTH rules each cited; a single-rule question still works via the single-shot fallback
- [ ] 4.3 Final opus whole-branch review; on READY, refresh the `companion_roadmap` memory
