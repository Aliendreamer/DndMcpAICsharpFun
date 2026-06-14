## 1. F4 — remove orphaned config (quick win)

- [ ] 1.1 Confirm `RerankerOptions.TopK` has no remaining references (grep whole tree); delete the property and any `Reranker:TopK` in `appsettings*.json`
- [ ] 1.2 `dotnet build` clean + `dotnet test` green; commit `refactor(retrieval): remove orphaned RerankerOptions.TopK`

## 2. F2 — consolidate canonical JSON serialization (fixes latent enum bug)

- [ ] 2.1 Write a failing test: `CanonicalTypeFixerService` output encodes `EntityType` as a string (currently can be an int → guard the bug)
- [ ] 2.2 Add `CanonicalJson` static (shared `WriteOptions`/`ReadOptions` + `WriteAsync(path,file,ct)` with trailing newline); re-point `CanonicalNameNormalizerService`, `CanonicalTypeFixerService`, `CanonicalJsonWriter`, and the extraction final-write at it (leave the checkpoint serializer distinct if intentionally unindented); make tests green
- [ ] 2.3 Full suite green; commit `refactor(canonical): single shared JSON serializer (fixes enum-converter bug)`

## 3. F1 — dedup extraction run-modes

- [ ] 3.1 Extract `ExtractOneAsync(candidate, …) → EntityEnvelope?` (shared per-candidate logic) and a single loop driver parameterized by candidate source; rewrite `RunFullExtractionAsync`/`RunErrorsOnlyAsync` as thin wrappers
- [ ] 3.2 Full suite green (existing extraction tests are the behavior contract); commit `refactor(extraction): dedup full/errors-only run modes`

## 4. F5 — DI scope-validation test

- [ ] 4.1 Add a test building the app's `IServiceCollection` via the real `Add*` extensions with external clients stubbed (Qdrant/IChatClient/embeddings/DbContext/Marker), calling `BuildServiceProvider(ValidateScopes=true, ValidateOnBuild=true)` and asserting no throw
- [ ] 4.2 Green; commit `test(di): full-container scope/build validation`

## 5. F6 — admin endpoint integration smoke

- [ ] 5.1 Add a `WebApplicationFactory<Program>` (or narrow host) smoke suite over the admin endpoints: happy path, missing optional params use defaults, admin-key 401 — external deps overridden with fakes
- [ ] 5.2 Green; commit `test(admin): endpoint binding + auth integration smoke`

## 6. F3 — shared test doubles

- [ ] 6.1 Create `Tests/TestDoubles/`: `RecordingEntityVectorStore`, `FakeChatClient`, `StubEmbeddingService`, `StubReranker`
- [ ] 6.2 Migrate `EntityIngestionSelfDeleteTests`, `ReindexEntityTests`, `DndChatServiceTests`, retrieval tests to the shared doubles; delete local copies; green; commit `test: consolidate shared test doubles`

## 7. F7 — admin endpoint plumbing helper

- [ ] 7.1 Add a shared admin route-group helper / endpoint filter (admin-key + consistent result envelope); apply to `Features/Admin/*Endpoints.cs`, preserving current response shapes (backward-compatible)
- [ ] 7.2 If any response envelope changed, update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`; green; commit `refactor(admin): shared endpoint plumbing`

## 8. F8 — split the extraction orchestrator (after F1)

- [ ] 8.1 Extract `EntitySchemaProvider` (LoadSchemas + InjectConfidenceField), `ExtractionCheckpointStore` (checkpoint read/write), `CandidateExtractor` (LLM tool-call + ExtractCandidateFields/StripConfidence); reduce `EntityExtractionOrchestrator` to a thin coordinator; add focused unit tests for the extracted units
- [ ] 8.2 Full suite green; `EntityExtractionOrchestrator` materially smaller; commit `refactor(extraction): split orchestrator into focused units`

## 9. Wrap

- [ ] 9.1 Final `dotnet build` 0 warnings + `dotnet test` fully green; confirm no behavior change (only F2's enum fix); spot-check a re-extract/ingest on one small book if feasible
