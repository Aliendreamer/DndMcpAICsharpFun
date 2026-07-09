## 1. Verdict type + pure cascade combiner (TDD)

- [ ] 1.1 Add `GroundingVerdict` (`Status: Grounded|Ungrounded|Uncertain`, `DecidedByTier:int`, `Score:double`) and `GroundingStatus` enum in `Features/Ingestion/EntityExtraction/`.
- [ ] 1.2 Add `EntityDisposition.Ungrounded` to the domain enum; failing tests first for its value + that it is excluded from `dnd_entities` eligibility wherever `Accepted` is the gate.
- [ ] 1.3 Write failing tests for the PURE verdict combiner: given (Tier0 bool, Tier1 score vs floor, optional Tier2 result, judgeEnabled) → expected `Status`/`DecidedByTier`. Cover: Tier0 confirm→Grounded(0); Tier1 below floor + judge→Ungrounded(1); Tier1 below floor + no judge→Uncertain; Tier1 above floor→escalate→Tier2 grounded/ungrounded(2); Tier2 unsure/disabled→Uncertain; Tier1 never Grounded.
- [ ] 1.4 Implement the pure combiner; make 1.1–1.3 pass; build 0/0.

## 2. Tier 1 embedding grounder (TDD)

- [ ] 2.1 Define `IGroundingProse`/embedder seam: embed entity text + query `dnd_blocks` scoped to `SourceBook` + page window, returning top similarity. Reuse existing embedding service + Qdrant block search; add a book+page filter.
- [ ] 2.2 Failing tests with a FAKE prose source returning controlled similarities: below floor→Ungrounded(1); at/above floor→escalate; verify the query is scoped to the entity's book + `±PageWindow`.
- [ ] 2.3 Implement Tier 1; add `GroundingOptions` (`SimilarityFloor`, `PageWindow`) to config + DI; make 2.2 pass; build 0/0.

## 3. Tier 2 judge behind an interface (TDD)

- [ ] 3.1 Define `IGroundingJudge { Task<bool> AreFieldsSupportedAsync(EntityEnvelope, string sourceProse, ct) }`; implement `QwenGroundingJudge` reusing the Ollama/qwen3 client abstraction with a field-support prompt.
- [ ] 3.2 Failing tests with a FAKE judge: judge=grounded→Grounded(2); judge=ungrounded→Ungrounded(2); judge disabled→never invoked, Uncertain. Assert the judge is only called on the escalated residual.
- [ ] 3.3 Implement the judge + wire into the cascade; make 3.2 pass; build 0/0.

## 4. GroundingCascade service (compose the tiers, TDD)

- [ ] 4.1 Failing tests for `GroundingCascade.Grade(entity, sourceProse, options)` end-to-end with fake Tier1/Tier2: short-circuits (Tier0 stops before Tier1; Tier1 reject stops before Tier2); escalation; judge-gated.
- [ ] 4.2 Implement `GroundingCascade` (injects Tier1 grounder + Tier2 judge + Tier0); DI-register; make 4.1 pass; build 0/0.

## 5. Verdict → action policy with the name gate (TDD)

- [ ] 5.1 Failing tests for a `GroundingActionPolicy` (pure): Grounded+clean-name→promote(Accepted); Grounded+ocr-artifact-name→stay NeedsReview; Ungrounded→set Ungrounded; Uncertain→no-op; Ungrounded only when judge ran. Reuse `ExtractionNeedsReview.HasOcrArtifacts`.
- [ ] 5.2 Implement the policy; make 5.1 pass; build 0/0.

## 6. Extraction-time integration

- [ ] 6.1 Failing tests: `EntityExtractionRunner.BuildTypedEnvelope` uses `GroundingCascade` (Tier0→1, Tier2 on residual only if run opts in) and feeds the verdict into `ExtractionDispositionPolicy`; Ungrounded verdict → `Ungrounded` disposition; existing Tier-0-grounded and ungrounded→NeedsReview behavior preserved.
- [ ] 6.2 Replace `HasGroundedContent` with the cascade; keep the disposition contract; make 6.1 + existing extraction tests pass; build 0/0.

## 7. RegroundService (backlog pass, TDD)

- [ ] 7.1 Failing tests (fakes for cascade + store): load a book's NeedsReview entities, grade each, apply the action policy, write canonical + `ReindexEntityAsync` per change; summary counts correct; canonical never deleted; checkpoint written every N and deleted on success; resume from checkpoint skips completed.
- [ ] 7.2 Implement `RegroundService` reusing `CanonicalJsonLoader`/writer + `ReindexEntityAsync`; checkpoint sidecar `<slug>.reground.progress.json`; make 7.1 pass; build 0/0.

## 8. Admin endpoint + contracts

- [ ] 8.1 Failing endpoint tests: `POST /admin/books/{id}/reground-entities` (admin-key guarded; missing key rejected); `?judge=true` opts into Tier 2 (tier2Invoked>0 with fake judge), default fast pass (tier2Invoked=0); returns the summary shape. Reuse the existing admin-books endpoint test harness.
- [ ] 8.2 Map the route in the books-admin endpoints group + DI wiring; make 8.1 pass; build 0/0.
- [ ] 8.3 Update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` (new endpoint, both `judge` variants, `X-Admin-Api-Key`); validate insomnia JSON.

## 9. Verify + review

- [ ] 9.1 Full build 0/0 + full test suite green (incl. Postgres/Qdrant Testcontainers).
- [ ] 9.2 Drive the endpoint against a running host (per `verify`): fast pass then `?judge=true`, confirm promotions/flags in canonical + Qdrant, and `books/canonical/` has no deletions. (Operational — defer honestly if the stack is down; Testcontainers cover the store paths.)
- [ ] 9.3 Whole-branch opus review; cross-check every ADDED/MODIFIED requirement; address findings.
