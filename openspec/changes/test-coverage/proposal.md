## Why

The project has no automated tests, making it impossible to refactor safely or verify correctness of the chunking, retrieval, and ingestion logic. Adding a test suite with 70% coverage sets a quality floor and unblocks confident iteration.

## What Changes

- Add a new `DndMcpAICsharpFun.Tests` xUnit project to the solution
- Wire coverlet for coverage collection; target ≥ 70% line and branch coverage
- Cover priority units: pattern detectors, `ContentCategoryDetector`, `DndChunker`, `QdrantPayloadMapper`, `RagRetrievalService` (with mocked Qdrant), `EmbeddingIngestor` (with mocked dependencies), `IngestionOrchestrator` (with mocked deps), admin endpoints

## Capabilities

### New Capabilities

- `test-suite`: xUnit test project with coverlet coverage gate at 70% line/branch coverage

### Modified Capabilities

<!-- No existing specs change requirements -->

## Impact

- New project: `DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj`
- New solution entry in `DndMcpAICsharpFun.sln`
- CI/CD: `dotnet test --collect:"XPlat Code Coverage"` must pass with ≥ 70% threshold
- No changes to production code contracts or APIs
