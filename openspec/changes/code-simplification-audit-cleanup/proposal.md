## Why

The 2026-06-14 code audit (`audit.md` in this change) found the codebase is lean and well-factored, with complexity and risk concentrated in a few spots: a 690-LOC extraction orchestrator with ~350 LOC of near-duplicate run-modes, canonical-JSON serialization scattered across four places with inconsistent settings (one missing the enum converter — a latent corruption bug), duplicated hand-rolled test doubles, orphaned config, and two coverage gaps that let runtime-only bugs through this week (a DI lifetime mismatch and an endpoint-binding 400). This change addresses all of them as behavior-preserving refactors plus a few testable invariants, with the 602-test suite as the safety net.

## What Changes

- **Extraction orchestrator (F1+F8):** dedup `RunFullExtractionAsync` / `RunErrorsOnlyAsync` into one per-candidate pipeline + shared loop driver, then split schema-provision, checkpointing, and candidate extraction into focused units. Behavior identical.
- **Canonical JSON (F2):** one shared `CanonicalJson` serializer (single write/read settings + write helper). **Fixes a latent bug** — `CanonicalTypeFixerService` currently writes without the enum converter — and removes the whole-file reformat diff noise.
- **Test doubles (F3):** consolidate duplicated fakes (`FakeEntityVectorStore`/`RecordingStore`, `FakeChatClient`, stub embeddings/reranker) into shared `Tests/TestDoubles/`.
- **Orphaned config (F4):** remove `RerankerOptions.TopK` (superseded by `CandidatePoolSize`).
- **DI scope validation (F5):** a test that builds the composed container with scope/build validation, catching the scoped-from-singleton class of bug.
- **Endpoint integration coverage (F6):** a thin `WebApplicationFactory` smoke suite over the admin endpoints (happy path + default params + auth), external dependencies stubbed.
- **Endpoint plumbing (F7):** a shared admin route-group helper / filter for the repeated auth + result-shaping boilerplate; standardized, backward-compatible result envelope.

No user-facing behavior changes (except F2, which corrects the latent enum-serialization bug). The full suite must stay green throughout.

## Capabilities

### New Capabilities

- `code-quality-invariants`: the testable invariants extracted from the audit — a single canonical-JSON serializer is the only writer, the composed DI container passes scope/build validation, and admin endpoints are integration-covered for binding/auth.

### Modified Capabilities

(none — F1/F3/F7/F8 are pure refactors with no requirement deltas; verified by the unchanged behavior suite)

## Impact

- Code: refactor `EntityExtractionOrchestrator` (+ new `EntitySchemaProvider`, `ExtractionCheckpointStore`, `CandidateExtractor`, lean `ExtractionPipeline`); new `CanonicalJson` consumed by all canonical writers; remove `RerankerOptions.TopK`; new shared admin endpoint helper; `Tests/TestDoubles/`.
- Tests: new DI-validation test, admin-endpoint integration smoke suite, shared doubles; all existing tests migrated where they used local fakes; suite stays green (currently 602).
- Contracts: only if F7 changes a response envelope — kept backward-compatible; `.http` + `.insomnia.json` updated if so.
- Data/behavior: none, other than `CanonicalTypeFixerService` now writing enum-correct canonical JSON (bug fix).
