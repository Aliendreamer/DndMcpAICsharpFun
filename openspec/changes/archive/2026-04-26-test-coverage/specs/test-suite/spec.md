# test-suite

## Purpose

Defines requirements for the automated test suite covering the DndMcpAICsharpFun project, including project structure, coverage targets, and per-component test expectations.

## ADDED Requirements

### Requirement: An xUnit test project exists and builds cleanly
The system SHALL include a `DndMcpAICsharpFun.Tests` project targeting `net10.0`, referenced in the solution file, with xUnit, NSubstitute, and coverlet.collector as NuGet dependencies.

#### Scenario: Test project compiles without errors
- **WHEN** `dotnet build` is run on the solution
- **THEN** both `DndMcpAICsharpFun` and `DndMcpAICsharpFun.Tests` build with 0 errors

#### Scenario: Tests can be discovered and run
- **WHEN** `dotnet test` is run
- **THEN** the test runner discovers all test classes and all tests pass

### Requirement: Overall line and branch coverage meets the 70% threshold
The system SHALL achieve ≥ 70% line coverage and ≥ 70% branch coverage across all non-excluded production source files, measured by coverlet and enforced at build time.

#### Scenario: Coverage threshold is met
- **WHEN** `dotnet test --collect:"XPlat Code Coverage"` is run
- **THEN** reported line coverage is ≥ 70% and branch coverage is ≥ 70%

#### Scenario: Coverage threshold enforcement fails the build when not met
- **WHEN** coverage drops below 70%
- **THEN** the test run exits with a non-zero exit code

### Requirement: All pattern detectors are covered
The system SHALL have tests for all 7 `IPatternDetector` implementations (`SpellPatternDetector`, `MonsterPatternDetector`, `ClassPatternDetector`, `BackgroundPatternDetector`, `TreasurePatternDetector`, `EncounterPatternDetector`, `TrapPatternDetector`), verifying `Detect` score and `IsEntityBoundary`.

#### Scenario: Detect returns high score on matching text
- **WHEN** `Detect` is called with text containing all keyword signals for a detector
- **THEN** the returned score equals `1.0f`

#### Scenario: Detect returns zero on non-matching text
- **WHEN** `Detect` is called with text containing no keyword signals
- **THEN** the returned score equals `0.0f`

#### Scenario: Detect returns partial score on partial match
- **WHEN** `Detect` is called with text containing some (but not all) keyword signals
- **THEN** the returned score is between `0.0f` and `1.0f` exclusive

#### Scenario: IsEntityBoundary returns true on boundary line
- **WHEN** `IsEntityBoundary` is called with a line containing the detector's boundary signal
- **THEN** the method returns `true`

#### Scenario: IsEntityBoundary returns false on non-boundary line
- **WHEN** `IsEntityBoundary` is called with a line that does not contain the boundary signal
- **THEN** the method returns `false`

### Requirement: ContentCategoryDetector is covered
The system SHALL have tests for `ContentCategoryDetector.Detect` and `FindBoundaryDetector`, covering the confidence threshold logic and fallback to chapter default.

#### Scenario: High-confidence detector wins over chapter default
- **WHEN** `Detect` is called and one detector returns a score ≥ 0.7
- **THEN** the returned category matches that detector's category, not the chapter default

#### Scenario: Low-confidence detectors fall back to chapter default
- **WHEN** `Detect` is called and all detectors return a score < 0.7
- **THEN** the returned category equals the chapter default passed in

#### Scenario: FindBoundaryDetector returns matching detector
- **WHEN** `FindBoundaryDetector` is called with a line matching a detector's boundary signal
- **THEN** the returned detector is the one whose `IsEntityBoundary` returned `true`

#### Scenario: FindBoundaryDetector returns null when no match
- **WHEN** `FindBoundaryDetector` is called with a line matching no detector
- **THEN** `null` is returned

### Requirement: EntityNameExtractor is covered
The system SHALL have tests for `EntityNameExtractor.Extract` covering the lookback logic.

#### Scenario: Returns the non-empty line immediately before the boundary
- **WHEN** `Extract` is called with a line list where the line before the boundary index is non-empty
- **THEN** the returned string equals that line trimmed

#### Scenario: Returns null when all preceding lines within lookback are empty
- **WHEN** `Extract` is called at boundary index 0 (no preceding lines)
- **THEN** `null` is returned

### Requirement: QdrantPayloadMapper is covered
The system SHALL have tests for `QdrantPayloadMapper.ToChunkMetadata` and `GetText`, verifying correct mapping from a `Value` dictionary to `ChunkMetadata`.

#### Scenario: Full payload maps to correct ChunkMetadata
- **WHEN** `ToChunkMetadata` is called with a payload containing all 7 metadata fields
- **THEN** every field of the returned `ChunkMetadata` matches the payload values

#### Scenario: Missing optional EntityName maps to null
- **WHEN** the payload does not contain the `entity_name` key
- **THEN** `ChunkMetadata.EntityName` is `null`

#### Scenario: Unknown enum string maps to default
- **WHEN** the payload contains an unrecognised string for `version` or `category`
- **THEN** the corresponding property is the default enum value

### Requirement: AdminApiKeyMiddleware is covered
The system SHALL have tests for `AdminApiKeyMiddleware.InvokeAsync` covering the accept and reject paths.

#### Scenario: Valid key passes through to next middleware
- **WHEN** a request carries the correct `X-Admin-Api-Key` header value
- **THEN** the next middleware delegate is invoked and response status is not 401

#### Scenario: Missing key returns 401
- **WHEN** a request has no `X-Admin-Api-Key` header
- **THEN** the response status is 401 and the next delegate is not invoked

#### Scenario: Wrong key returns 401
- **WHEN** a request has an `X-Admin-Api-Key` header with an incorrect value
- **THEN** the response status is 401

### Requirement: EmbeddingIngestor is covered
The system SHALL have tests for `EmbeddingIngestor.IngestAsync` verifying batching behaviour and correct delegation to `IEmbeddingService` and `IVectorStoreService`.

#### Scenario: All chunks are embedded and upserted
- **WHEN** `IngestAsync` is called with N chunks
- **THEN** `IEmbeddingService.EmbedAsync` and `IVectorStoreService.UpsertAsync` are called with all chunks covered

#### Scenario: Chunks are processed in batches
- **WHEN** the chunk count exceeds `EmbeddingBatchSize`
- **THEN** `UpsertAsync` is called multiple times, each time with at most `EmbeddingBatchSize` chunks

### Requirement: IngestionOrchestrator is covered
The system SHALL have tests for `IngestionOrchestrator.IngestBookAsync` covering the skip, success, and failure paths.

#### Scenario: Missing record returns without error
- **WHEN** `IngestBookAsync` is called with an id that returns null from the tracker
- **THEN** the method returns without calling the extractor or embedding ingestor

#### Scenario: Unchanged completed book is skipped
- **WHEN** the record has status Completed and the computed file hash matches the stored hash
- **THEN** neither the extractor nor the embedding ingestor is called

#### Scenario: New or changed book is fully ingested
- **WHEN** the record is Pending or the hash has changed
- **THEN** the extractor is called, chunks are produced, embedding ingestor is called, and tracker is marked Completed

#### Scenario: Exception during ingestion marks record as Failed
- **WHEN** the embedding ingestor throws an exception
- **THEN** the tracker is called with `MarkFailedAsync` and the error message is preserved

### Requirement: RagRetrievalService is covered
The system SHALL have tests for `RagRetrievalService.SearchAsync` verifying that query, filter, topK, and score threshold parameters are forwarded correctly to Qdrant.

#### Scenario: Results are returned mapped from scored points
- **WHEN** `SearchAsync` is called and Qdrant returns scored points
- **THEN** the returned list contains `RetrievalResult` objects with text, metadata, and score matching the scored points

#### Scenario: TopK is capped at MaxTopK
- **WHEN** `SearchAsync` is called with `TopK` exceeding `MaxTopK`
- **THEN** the `limit` passed to Qdrant equals `MaxTopK`
