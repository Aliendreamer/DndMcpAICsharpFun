## 1. NuGet Package

- [x] 1.1 Add `Microsoft.EntityFrameworkCore.Sqlite` to `DndMcpAICsharpFun.csproj`
- [x] 1.2 Run `dotnet restore` and confirm no errors

## 2. Domain & Value Objects

- [x] 2.1 Create `Domain/ContentCategory.cs` enum: `Spell`, `Monster`, `Class`, `Background`, `Item`, `Rule`, `Unknown`
- [x] 2.2 Create `Domain/DndVersion.cs` enum: `Edition2014`, `Edition2024`
- [x] 2.3 Create `Domain/ChunkMetadata.cs` record with `SourceBook`, `Version`, `Category`, `EntityName`, `Chapter`, `PageNumber`, `ChunkIndex`
- [x] 2.4 Create `Domain/ContentChunk.cs` record with `Text` and `Metadata`

## 3. Ingestion Tracking (SQLite)

- [x] 3.1 Create `Infrastructure/Sqlite/IngestionRecord.cs` entity with all fields from spec
- [x] 3.2 Create `Infrastructure/Sqlite/IngestionDbContext.cs` with `DbSet<IngestionRecord>`
- [x] 3.3 Register `IngestionDbContext` in `Program.cs` with SQLite connection string from `IngestionOptions:DatabasePath`
- [x] 3.4 Add `Database.MigrateAsync()` call on startup
- [x] 3.5 Generate initial EF Core migration: `dotnet ef migrations add InitialCreate`
- [x] 3.6 Create `Features/Ingestion/Tracking/IIngestionTracker.cs` interface
- [x] 3.7 Create `Features/Ingestion/Tracking/SqliteIngestionTracker.cs` implementing `IIngestionTracker`
- [x] 3.8 Register `IIngestionTracker` in DI

## 4. PDF Extraction

- [x] 4.1 Create `Features/Ingestion/Pdf/IPdfTextExtractor.cs` interface returning `IEnumerable<(int PageNumber, string Text)>`
- [x] 4.2 Create `Features/Ingestion/Pdf/PdfPigTextExtractor.cs` using PdfPig with `page.Text`
- [x] 4.3 Add sparse-page warning logging (configurable min char threshold from `IngestionOptions:MinPageCharacters`)
- [x] 4.4 Register `IPdfTextExtractor` in DI

## 5. Content Chunking & Detection

- [x] 5.1 Create `Features/Ingestion/Chunking/ChapterContextTracker.cs` that detects chapter headings and maintains current category context
- [x] 5.2 Create `Features/Ingestion/Chunking/Detectors/SpellPatternDetector.cs`
- [x] 5.3 Create `Features/Ingestion/Chunking/Detectors/MonsterPatternDetector.cs`
- [x] 5.4 Create `Features/Ingestion/Chunking/Detectors/BackgroundPatternDetector.cs`
- [x] 5.5 Create `Features/Ingestion/Chunking/Detectors/ClassPatternDetector.cs`
- [x] 5.6 Create `Features/Ingestion/Chunking/ContentCategoryDetector.cs` composing all detectors with confidence threshold
- [x] 5.7 Create `Features/Ingestion/Chunking/EntityNameExtractor.cs` extracting name from line before anchor pattern
- [x] 5.8 Create `Features/Ingestion/Chunking/DndChunker.cs` implementing entity-boundary chunking with fixed-size fallback
- [x] 5.9 Register chunking services in DI

## 6. Ingestion Orchestrator

- [x] 6.1 Create `Features/Ingestion/IIngestionOrchestrator.cs` interface with `IngestBookAsync(int recordId)`
- [x] 6.2 Create `Features/Ingestion/IngestionOrchestrator.cs`:
  - Mark record as `Processing`
  - Compute SHA256 hash; skip if already `Completed` with same hash
  - Extract text via `IPdfTextExtractor`
  - Chunk and classify via `DndChunker` + `ContentCategoryDetector`
  - Hand chunks to `IEmbeddingIngestor` (stubbed interface, implemented in next change)
  - Mark record `Completed` with chunk count, or `Failed` with error
- [x] 6.3 Create `Features/Embedding/IEmbeddingIngestor.cs` stub interface (no-op implementation for now)
- [x] 6.4 Register `IIngestionOrchestrator` and stub `IEmbeddingIngestor` in DI

## 7. Background Service

- [x] 7.1 Create `Features/Ingestion/IngestionBackgroundService.cs` extending `BackgroundService`
- [x] 7.2 Implement immediate first-run then 24h repeat loop
- [x] 7.3 Query `Pending` and `Failed` records from tracker on each pass
- [x] 7.4 Call `IIngestionOrchestrator.IngestBookAsync` for each eligible record
- [x] 7.5 Emit structured pass-summary log after each cycle
- [x] 7.6 Register `IngestionBackgroundService` as hosted service in `Program.cs`

## 8. Admin Endpoints

- [x] 8.1 Create `Features/Admin/BooksAdminEndpoints.cs` with minimal API endpoint definitions
- [x] 8.2 Implement `POST /admin/books/register` — validate file exists, compute hash, create record
- [x] 8.3 Implement `GET /admin/books` — return all records from tracker
- [x] 8.4 Implement `POST /admin/books/{id}/reingest` — reset status to Pending, trigger immediate orchestrator call
- [x] 8.5 Map all endpoints in `Program.cs` under `/admin` route group

## 9. Configuration

- [x] 9.1 Add `MinPageCharacters`, `MaxChunkTokens`, `OverlapTokens` to `IngestionOptions`
- [x] 9.2 Update `appsettings.json` and `appsettings.Development.json` with sensible defaults (512 / 64 / 100)

## 10. Verification

- [x] 10.1 `dotnet build` passes with zero errors
- [ ] 10.2 Register a test PDF via `POST /admin/books/register` and confirm 201 response
- [ ] 10.3 Trigger `POST /admin/books/{id}/reingest` and confirm chunks appear in logs
- [ ] 10.4 `GET /admin/books` shows the record with `status = Completed` and a non-zero `chunk_count`
- [ ] 10.5 Re-register the same file and confirm duplicate is rejected (same hash, returns existing record)
