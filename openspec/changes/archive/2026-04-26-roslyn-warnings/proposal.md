## Why

The project had CA1873 warnings (potentially-expensive logging arguments evaluated even when the log level is disabled) and no build gate to keep warnings from accumulating. Enabling `TreatWarningsAsErrors` with `EnforceCodeStyleInBuild` makes warning hygiene permanent — nothing merges with a warning.

## What Changes

- Enable `EnforceCodeStyleInBuild=true` and `TreatWarningsAsErrors=true` in the project file
- Replace all direct `_logger.LogXxx(...)` calls with `[LoggerMessage]`-generated partial methods (fixes CA1873)
- Convert all service classes from field-assignment constructors to primary constructors
- Add `static` to non-capturing lambdas in `Program.cs` and `RagRetrievalService.cs`
- Use collection expressions (`[...]`) in `QdrantCollectionInitializer`
- Exclude EF Core `Migrations/` from all analyzer diagnostics via `.editorconfig`
- Fix path traversal vulnerability in `BooksAdminEndpoints`: sanitise uploaded filename with `Path.GetFileName`

## Capabilities

### New Capabilities

None — this is a code-quality change only.

### Modified Capabilities

- `build-gate`: `dotnet build` now fails on any warning; enforces permanent warning-free baseline

## Impact

- `DndMcpAICsharpFun.csproj` — `EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`
- `.editorconfig` — Migrations exclusion block
- `Features/Ingestion/IngestionOrchestrator.cs` — primary constructor + `[LoggerMessage]`
- `Features/Ingestion/IngestionBackgroundService.cs` — primary constructor + `[LoggerMessage]`
- `Features/Embedding/EmbeddingIngestor.cs` — primary constructor + `[LoggerMessage]`
- `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — primary constructor + `[LoggerMessage]` + collection expressions
- `Features/Retrieval/RagRetrievalService.cs` — primary constructor + static lambdas
- `Features/Ingestion/Pdf/PdfPigTextExtractor.cs` — `[LoggerMessage]`
- `Features/Admin/BooksAdminEndpoints.cs` — `[LoggerMessage]` + path traversal fix
- `Program.cs` — static lambdas
