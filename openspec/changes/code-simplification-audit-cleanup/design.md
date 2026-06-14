## Context

Source of truth: `audit.md` in this change directory (findings F1–F8). The suite is 602 green; this change must keep it green — that is the behavior-preservation contract for the pure refactors. Only F2 changes an observable on the canonical-write path, and only to fix a latent enum-serialization bug.

## Goals / Non-Goals

**Goals:**
- Remove the largest duplication (extraction run-modes) and split the over-large orchestrator.
- One canonical-JSON serializer; fix the missing-enum-converter latent bug; end diff noise.
- Close the two coverage gaps that produced runtime-only bugs (DI lifetime, endpoint binding).
- Consolidate duplicated test doubles and endpoint boilerplate.

**Non-Goals:**
- No new product features or user-facing behavior change.
- No rewrite of the extraction algorithm — same logic, reorganized.
- No new HTTP endpoints; response-envelope standardization (F7) stays backward-compatible.

## Decisions

**Sequencing (low-risk first).** F4 → F2 → F1 → F5 → F6 → F3 → F7 → F8. Each workstream is its own commit so any regression is bisectable; run the full suite after each.

**F4 — orphaned config.** Confirm no reference to `RerankerOptions.TopK` anywhere, then delete the property and any `Reranker:TopK` in `appsettings*.json`. Mechanical.

**F2 — `CanonicalJson` consolidation.** New static `CanonicalJson` exposing `WriteOptions`, `ReadOptions`, and `Task WriteAsync(string path, CanonicalJsonFile file, CancellationToken)` (indented, `JsonStringEnumConverter`, trailing newline). Re-point `CanonicalNameNormalizerService`, `CanonicalTypeFixerService`, `CanonicalJsonWriter`, and the extraction final-write at it. Keep the extraction *checkpoint* serializer separate if it intentionally uses `WriteIndented=false` (note that). Add a test that `CanonicalTypeFixerService` output round-trips entity types as strings (the bug guard).

**F1 — extraction dedup.** Introduce `ExtractOneAsync(ScannerInput candidate, …) → EntityEnvelope?` capturing the shared per-candidate logic (extract fields → strip confidence → normalize name → derive needsReview → build envelope). Introduce a single driver that iterates a candidate sequence, checkpoints, and accumulates results/errors; `RunFullExtractionAsync` and `RunErrorsOnlyAsync` become thin wrappers passing the full vs error-only candidate set. The existing extraction tests + green suite verify identical behavior.

**F8 — extraction split (after F1).** Extract `EntitySchemaProvider` (`LoadSchemas` + `InjectConfidenceField`), `ExtractionCheckpointStore` (checkpoint read/write + `CheckpointOptions`), and `CandidateExtractor` (the LLM tool-call + `ExtractCandidateFieldsAsync`/`StripConfidence`). `EntityExtractionOrchestrator` becomes a thin coordinator. Each extracted unit is independently unit-testable.

**F5 — DI scope validation.** A test that constructs the application's `IServiceCollection` via the real `Add*` extension methods, substitutes only the external clients that can't run in-test (Qdrant, Ollama/`IChatClient`/embeddings, Postgres `DbContext`, Marker HTTP), and calls `BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true })`, asserting no throw. Generalizes the existing narrow `RetrievalRegistrationTests`.

**F6 — endpoint integration smoke.** A `WebApplicationFactory<Program>`-based suite (or minimal host) hitting the admin endpoints: happy path, missing optional params (defaults), and admin-key auth (401 without key). External deps stubbed via test service overrides. Keep it thin — binding/auth/serialization coverage, not business logic (already unit-tested).

**F3 — shared test doubles.** `Tests/TestDoubles/`: `RecordingEntityVectorStore` (records Upsert/Delete calls, in-memory semantics), `FakeChatClient` (captures outgoing messages), `StubEmbeddingService`, `StubReranker`. Migrate `EntityIngestionSelfDeleteTests`, `ReindexEntityTests`, `DndChatServiceTests`, and the retrieval tests to use them; delete the local copies.

**F7 — admin endpoint helper.** A shared route-group helper/endpoint-filter for admin-key enforcement + a consistent result envelope (`Results.Ok`/`Problem` shaping). Apply to `Features/Admin/*Endpoints.cs`. Preserve current response JSON shapes (or only additively standardize) so no contract break; if any shape changes, update `.http` + `.insomnia.json`.

## Risks / Trade-offs

- **Refactor regressions.** Mitigated by per-workstream commits + full suite after each, and by the existing 602 tests (extraction, ingestion, retrieval, chat, admin services).
- **F1/F8 are the riskiest (largest behavior surface).** Do F1 (dedup, mechanical, behavior-equal) before F8 (structural split). If F8 proves large, it can be deferred to a follow-up without blocking the rest.
- **F6 WebApplicationFactory wiring.** The app composes many external clients; the test must override them cleanly. If full `WebApplicationFactory<Program>` is heavy, a narrower endpoint-host test is acceptable — the goal is binding/auth coverage, not E2E.
- **F2 checkpoint vs canonical settings.** The extraction checkpoint deliberately may be unindented; keep it distinct from the canonical writer to avoid changing checkpoint format.
