## Context

The project has zero automated tests. All business logic (pattern detectors, chunker, retrieval mapping, orchestrator) is untested. The absence of a test suite makes refactoring risky and prevents CI from providing correctness guarantees. The goal is to establish a 70% line/branch coverage floor, measured with coverlet, enforced at build time.

## Goals / Non-Goals

**Goals:**
- Add `DndMcpAICsharpFun.Tests` xUnit project to the solution
- Reach ≥ 70% line and branch coverage on production code
- Cover all 7 pattern detectors, `ContentCategoryDetector`, `EntityNameExtractor`, `QdrantPayloadMapper`, `AdminApiKeyMiddleware`, `EmbeddingIngestor`, `IngestionOrchestrator`, `RagRetrievalService`, and admin/retrieval endpoints
- Enforce coverage threshold in CI via `coverlet` `<CoverageThresholdPercent>` or MSBuild fail-on-threshold

**Non-Goals:**
- 100% coverage — infrastructure code (`QdrantVectorStoreService`, `PdfPigTextExtractor`, `OllamaEmbeddingService`) wraps external I/O and is excluded from coverage targets
- Integration tests against real Qdrant/Ollama/SQLite — unit tests only (mocked dependencies)
- E2E HTTP tests via `WebApplicationFactory` — out of scope for this change

## Decisions

**xUnit over NUnit/MSTest**
xUnit is the de-facto standard in the .NET OSS ecosystem, has first-class async support, no shared state between tests (no `[SetUp]`/`[TearDown]` anti-pattern), and is what the ASP.NET Core team uses internally. Alternatives: NUnit (more assertions API), MSTest (Microsoft standard). xUnit wins on community tooling.

**NSubstitute over Moq**
NSubstitute's API is more readable and does not require `.Object` unwrapping. Moq 4.20+ has a controversial SponsorLink package; NSubstitute has no such issues. Both are feature-equivalent for this use case.

**coverlet.collector (DataCollector) over coverlet.msbuild**
`coverlet.collector` integrates with `dotnet test --collect:"XPlat Code Coverage"` and works with all CI systems without extra MSBuild props. Threshold enforcement added via `runsettings` file or directly in the `.csproj` using `<CoverageThresholdPercent>`.

**Test project structure: single flat project**
All tests in one `DndMcpAICsharpFun.Tests` project, organized by namespace mirroring the source. Avoids over-engineering at this stage — split later if project grows.

**Excluded from coverage:**
- `Migrations/` — EF-generated
- `Infrastructure/Ollama/`, `Infrastructure/Qdrant/QdrantVectorStoreService.cs`, `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`, `Infrastructure/Sqlite/SqliteIngestionTracker.cs` — external I/O wrappers
- `Features/Ingestion/Pdf/PdfPigTextExtractor.cs` — PDF parsing, requires real files
- `Program.cs` — composition root

## Risks / Trade-offs

- **Qdrant.Client types are sealed** → `QdrantVectorStoreService` and `RagRetrievalService` can't be tested without real Qdrant or a wrapper. Mitigation: `RagRetrievalService` is tested via `IRagRetrievalService` mock at the orchestrator level; its internal `ExecuteSearchAsync` is excluded from coverage.
- **`DndChunker` tokenization is approximate** (whitespace-based) → tests use known input/output pairs that are robust to the approximation.
- **70% threshold may not be achievable on first pass** → if coverage falls short, lower threshold temporarily and file a follow-up task.

## Migration Plan

1. Add `DndMcpAICsharpFun.Tests` csproj and add to solution
2. Write tests in priority order: detectors → detector aggregator → mapper → middleware → orchestrator → retrieval
3. Verify `dotnet test` passes with coverage ≥ 70%
4. Commit

No rollback strategy needed (additive change only).
