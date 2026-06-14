# Code Audit — DndMcpAICsharpFun

**Date:** 2026-06-14
**Scope:** whole codebase + tests (~10.0k production LOC, ~9.9k test LOC)
**Nature:** read-only audit. Nothing changed. Each finding below is a candidate for its own small, behaviour-preserving refactor change (the green test suite is the safety net).

## Summary

The codebase is small and generally well-factored — clear feature folders, options-bound config, focused services, strong test coverage (602 tests). Complexity has concentrated in a few predictable spots: the extraction orchestrator, scattered canonical-JSON serialization, and hand-rolled test doubles. The two bugs that only surfaced at runtime this week (a DI lifetime mismatch and an endpoint binding) point at two real coverage gaps.

### Scorecard

| Area | Health | Notes |
|---|---|---|
| Feature structure / boundaries | 🟢 Good | Clear `Features/<area>` split |
| Config / options | 🟢 Good | 20 options classes, consistently bound |
| Duplication | 🟡 Watch | Extraction run-modes; canonical writers; test doubles |
| Oversized files / SRP | 🟡 Watch | One 690-LOC orchestrator; rest ≤320 |
| Dead code | 🟢 Mostly clean | One orphaned config field found |
| Consistency | 🟡 Watch | Canonical serializer settings diverge; endpoint plumbing repeats |
| Test quality | 🟡 Watch | Duplicated fakes; minimal-API binding + full-DI not covered |

### Prioritized findings

| # | Finding | Effort | Risk | Priority |
|---|---|---|---|---|
| F1 | Extraction `RunFull` / `RunErrorsOnly` ~350 LOC near-duplicate | M | Med | **P1** |
| F2 | Canonical JSON (de)serialization scattered + inconsistent | S–M | Low | **P1** |
| F3 | Duplicated test doubles (fake stores/clients/embeddings) | M | Low | P2 |
| F4 | Orphaned config `RerankerOptions.TopK` | S | Low | P2 (quick win) |
| F5 | No full-container DI scope validation test | S | Med | P2 |
| F6 | Admin/minimal-API endpoints have no integration coverage | M | Med | P2 |
| F7 | Endpoint plumbing (admin-key, error shapes) repeated across 8 files | M | Low | P3 |
| F8 | `EntityExtractionOrchestrator` owns too many responsibilities | L | Med | P3 |

### Quick wins (low effort, low risk)

- **F4** — delete the orphaned `RerankerOptions.TopK`.
- **F2** (the settings half) — one shared canonical serializer settings constant; fixes the noisy reformat diffs and a latent enum bug.

---

## Findings

### F1 — Extraction run-modes are ~350 LOC of near-duplicate `[P1, M, Med]`

`Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs`:
- `RunFullExtractionAsync` (lines ~116–295) and `RunErrorsOnlyAsync` (~296–467) are large and largely parallel: both build scanner inputs, loop candidates, call `ExtractCandidateFieldsAsync`, normalize the display name, derive `needsReview`, build an `EntityEnvelope`, checkpoint, and write canonical/errors/warnings. The candidate→entity construction was duplicated enough that the `mandatory-entity-name-normalization` change had to edit **both** sites identically.

**Why it matters:** every future change to extraction (new field, new flag, normalization tweak) must be made twice and can drift. It's the project's highest-LOC file and its biggest duplication.

**Fix:** extract the per-candidate pipeline into one method (`ExtractOneAsync(candidate, …) → EntityEnvelope?`) and a shared loop driver parameterized by the candidate source (all candidates vs error-only). The two public modes become thin wrappers selecting the candidate set.

---

### F2 — Canonical JSON serialization is scattered and inconsistent `[P1, S–M, Low]`

At least four places define their own canonical-write `JsonSerializerOptions`:
- `EntityExtractionOrchestrator` — `CheckpointOptions` (`WriteIndented = false`, enum converter)
- `CanonicalNameNormalizerService` — `WriteIndented = true`, enum converter
- `CanonicalJsonWriter` (new) — `WriteIndented = true`, enum converter
- `CanonicalTypeFixerService` — `new JsonSerializerOptions { WriteIndented = true }` **with no enum converter**

**Why it matters:** (1) the divergent indentation is why a normalize/ingest pass reformats whole files and produces huge, noisy diffs; (2) `CanonicalTypeFixerService` lacking the enum converter is a **latent bug** — it can serialize entity-type enums as integers, corrupting canonical files it writes. Reads are also duplicated (`CanonicalJsonLoader` vs ad-hoc parsing).

**Fix:** one shared `CanonicalJson` static (single `WriteOptions` + `ReadOptions` + a `WriteAsync(path, file)` helper with trailing newline). Point all writers at it. Small, high-leverage, removes a latent bug and the diff noise.

---

### F3 — Duplicated test doubles `[P2, M, Low]`

Multiple test files hand-roll their own fakes of the same interfaces:
- fake `IEntityVectorStore` appears as `FakeEntityVectorStore` (EntityIngestionSelfDeleteTests) and `RecordingStore` (ReindexEntityTests), plus NSubstitute mocks elsewhere
- `FakeChatClient` (DndChatServiceTests), fake `IEmbeddingService`, fake reranker across retrieval tests

**Why it matters:** the same recording/fake logic is re-implemented per file; behaviour drifts and each new test reinvents the wheel.

**Fix:** a `Tests/TestDoubles/` with reusable `RecordingEntityVectorStore`, `FakeChatClient`, `StubEmbeddingService`, `StubReranker`. Consolidate incrementally.

---

### F4 — Orphaned config `RerankerOptions.TopK` `[P2, S, Low — quick win]`

`Features/Retrieval/RerankerOptions.cs` still has `TopK` (default 20). After the `fused-reranked-retrieval` change introduced `CandidatePoolSize`, consumers (`EntityRetrievalService`, `FusedRetrievalService`) use `CandidatePoolSize`; no reference to `TopK` remains in the retrieval code. **Verify** no other consumer, then delete it (and any `Reranker:TopK` in appsettings).

---

### F5 — No full-container DI scope-validation test `[P2, S, Med]`

The `FusedRetrievalService` singleton-consuming-scoped bug reached runtime because nothing validated the composed container. A targeted lifetime test was added (`RetrievalRegistrationTests`), but it only guards retrieval.

**Why it matters:** any future scoped-from-singleton mistake is again caught only by a container rebuild.

**Fix:** a test that builds the app's service collection (or the `Add*` extension groups with stubbed external clients) with `validateScopes: true, validateOnBuild: true` and asserts it doesn't throw. Catches the whole class of lifetime bugs.

---

### F6 — Admin/minimal-API endpoints have no integration coverage `[P2, M, Med]`

The `needs-review` `offset/limit` required-param 400 reached runtime because endpoint binding is unit-untested (services are tested; the minimal-API layer isn't). Eight endpoint files map routes with no integration tests over the actual binding/auth/serialization.

**Fix:** a thin `WebApplicationFactory` smoke suite hitting the admin endpoints (happy path + missing-param + auth) with external dependencies stubbed. Doesn't need the full stack.

---

### F7 — Endpoint plumbing repeated across 8 files `[P3, M, Low]`

`Features/Admin/*Endpoints.cs` (Books, CanonicalValidation, CanonicalNameNormalizer, CanonicalTypeFixer, Fivetools, NeedsReview) + retrieval endpoints each repeat the `MapGet/MapPost` + admin-key + result-shaping boilerplate. Error/response shapes vary slightly per file.

**Fix:** a shared admin route-group helper / endpoint filter for the common concerns (auth already partly centralized via `AdminApiKeyMiddleware`); standardize the result envelope. Cosmetic but improves consistency and shrinks each file.

---

### F8 — `EntityExtractionOrchestrator` owns too many responsibilities `[P3, L, Med]`

Beyond the run-modes (F1), this one class also does: schema loading + confidence-field injection (`LoadSchemas`, `InjectConfidenceField`), checkpoint read/write (`WriteCheckpointAsync`, `LoadCheckpoint…`), scanner-input building, source/edition derivation, and the LLM tool-call extraction. That's ~5 responsibilities in 690 LOC.

**Why it matters:** hard to hold in context, hard to test in isolation, the natural home for more growth.

**Fix (larger):** split into `ExtractionPipeline` (the loop/orchestration), `CandidateExtractor` (LLM tool-call + JSON), `ExtractionCheckpointStore`, and `EntitySchemaProvider`. Do this *after* F1 (dedup first, then split). Bigger effort — schedule deliberately.

---

## Suggested sequencing

1. **F4** (delete orphaned config) and **F2-settings** (shared serializer) — quick wins, immediate diff-noise + latent-bug payoff.
2. **F1** (dedup extraction run-modes) — highest duplication payoff; unblocks F8.
3. **F5** + **F6** (DI + endpoint coverage) — close the two gaps that bit us at runtime.
4. **F3** (shared test doubles) — ongoing as tests are touched.
5. **F7**, **F8** — larger, schedule deliberately.

Each becomes its own brainstorm → propose → implement change; none should alter behaviour (suite stays green).
