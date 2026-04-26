## 1. Project Scaffold

- [x] 1.1 Create `DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj` targeting `net10.0` with xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, NSubstitute, and coverlet.collector
- [x] 1.2 Add a project reference to `DndMcpAICsharpFun.csproj` in the test project
- [x] 1.3 Add the test project to the solution with `dotnet sln add`
- [x] 1.4 Add a `coverlet.runsettings` (or inline `<CoverageThresholdPercent>70</CoverageThresholdPercent>`) to enforce 70% line/branch threshold
- [x] 1.5 Verify `dotnet build` and `dotnet test` pass with 0 errors

## 2. Pattern Detector Tests

- [x] 2.1 Create `Tests/Chunking/Detectors/SpellPatternDetectorTests.cs` — test `Detect` at 0/1/3 hits and `IsEntityBoundary`
- [x] 2.2 Create `Tests/Chunking/Detectors/MonsterPatternDetectorTests.cs` — same shape
- [x] 2.3 Create `Tests/Chunking/Detectors/ClassPatternDetectorTests.cs` — same shape
- [x] 2.4 Create `Tests/Chunking/Detectors/BackgroundPatternDetectorTests.cs` — same shape
- [x] 2.5 Create `Tests/Chunking/Detectors/TreasurePatternDetectorTests.cs` — same shape
- [x] 2.6 Create `Tests/Chunking/Detectors/EncounterPatternDetectorTests.cs` — same shape
- [x] 2.7 Create `Tests/Chunking/Detectors/TrapPatternDetectorTests.cs` — same shape
- [x] 2.8 Run `dotnet test` — all detector tests pass

## 3. ContentCategoryDetector Tests

- [x] 3.1 Create `Tests/Chunking/ContentCategoryDetectorTests.cs`
- [x] 3.2 Test: high-confidence detector overrides chapter default (score ≥ 0.7)
- [x] 3.3 Test: all detectors below threshold returns chapter default
- [x] 3.4 Test: `FindBoundaryDetector` returns correct detector on match
- [x] 3.5 Test: `FindBoundaryDetector` returns null when no match
- [x] 3.6 Run `dotnet test` — all new tests pass

## 4. EntityNameExtractor Tests

- [x] 4.1 Create `Tests/Chunking/EntityNameExtractorTests.cs`
- [x] 4.2 Test: returns trimmed line immediately before boundary index
- [x] 4.3 Test: returns null when boundary is at index 0
- [x] 4.4 Test: skips empty lines within lookback window
- [x] 4.5 Run `dotnet test` — all new tests pass

## 5. QdrantPayloadMapper Tests

- [x] 5.1 Create `Tests/Retrieval/QdrantPayloadMapperTests.cs`
- [x] 5.2 Test: full payload produces correct `ChunkMetadata` with all fields
- [x] 5.3 Test: missing `entity_name` maps to null
- [x] 5.4 Test: unknown enum string maps to default enum value
- [x] 5.5 Test: `GetText` returns text field value
- [x] 5.6 Run `dotnet test` — all new tests pass

## 6. AdminApiKeyMiddleware Tests

- [x] 6.1 Create `Tests/Admin/AdminApiKeyMiddlewareTests.cs`
- [x] 6.2 Test: correct key calls next delegate, response not 401
- [x] 6.3 Test: missing key returns 401, next not called
- [x] 6.4 Test: wrong key returns 401
- [x] 6.5 Run `dotnet test` — all new tests pass

## 7. EmbeddingIngestor Tests

- [x] 7.1 Create `Tests/Embedding/EmbeddingIngestorTests.cs` using NSubstitute mocks for `IEmbeddingService` and `IVectorStoreService`
- [x] 7.2 Test: all chunks embedded and upserted (single batch)
- [x] 7.3 Test: chunks split across multiple batches when count > batch size
- [x] 7.4 Test: `UpsertAsync` call count equals `ceil(N / batchSize)`
- [x] 7.5 Run `dotnet test` — all new tests pass

## 8. IngestionOrchestrator Tests

- [x] 8.1 Create `Tests/Ingestion/IngestionOrchestratorTests.cs` using NSubstitute mocks for all dependencies
- [x] 8.2 Test: null record → returns without calling extractor or ingestor
- [x] 8.3 Test: completed record with unchanged hash → extractor not called
- [x] 8.4 Test: pending record → extractor called, ingestor called, tracker marked Completed
- [x] 8.5 Test: exception during ingest → tracker called with `MarkFailedAsync` containing error message
- [x] 8.6 Run `dotnet test` — all new tests pass

## 9. RagRetrievalService Tests

- [x] 9.1 Create `Tests/Retrieval/RagRetrievalServiceTests.cs` with NSubstitute mocks for `IEmbeddingService`; use a fake `QdrantClient` substitute or wrap via a thin interface if needed
- [x] 9.2 Test: `SearchAsync` maps scored points to `RetrievalResult` list correctly
- [x] 9.3 Test: `TopK` > `MaxTopK` results in `limit == MaxTopK` sent to Qdrant
- [x] 9.4 Run `dotnet test` — all new tests pass

## 10. Coverage Gate Verification

- [x] 10.1 Run `dotnet test --collect:"XPlat Code Coverage"` and inspect the generated `coverage.cobertura.xml`
- [x] 10.2 Confirm line coverage ≥ 70% and branch coverage ≥ 70%
- [x] 10.3 If below threshold, add targeted tests for the lowest-coverage files first
- [x] 10.4 Commit all test files and configuration
