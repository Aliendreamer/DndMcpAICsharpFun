## Why

With the infrastructure foundation in place, the system needs to get D&D content from PDF books into Qdrant. This change implements the full ingestion pipeline: book registration, idempotent file tracking via SQLite, background processing, PDF text extraction, intelligent chunking with category detection, and entity name extraction — so every stored chunk carries rich metadata enabling version- and category-aware retrieval.

## What Changes

- Add `POST /admin/books/register` endpoint to register a PDF book with metadata (source, version, full name)
- Add `GET /admin/books` endpoint to list all registered books and their ingestion status
- Add `POST /admin/books/{id}/reingest` endpoint to force reprocessing of a specific book
- Add SQLite database (`IngestionDbContext`) with `ingestion_records` table tracking file hash, status, chunk count
- Add `IngestionBackgroundService` (IHostedService) that scans registered books every 24 hours
- Add `PdfTextExtractor` using PdfPig for text-layer extraction with two-column layout handling
- Add `DndChunker` that splits extracted text on detected entity boundaries (spells, monsters, backgrounds) with fixed-size fallback
- Add `ContentCategoryDetector` using pattern matching to classify chunks (spell, monster, class, background, item, rule)
- Add `EntityNameExtractor` that identifies the D&D entity name for each chunk
- Add `ChunkMetadata` value object carrying all tagging fields

## Capabilities

### New Capabilities

- `book-registration`: Admin-secured CRUD for registering PDF books with source/version metadata
- `ingestion-tracking`: SQLite-backed idempotent ingestion tracker (SHA256 hash, status, retry on failure)
- `pdf-extraction`: PdfPig-based text extraction with two-column reconstruction
- `content-chunking`: Entity-boundary chunking with category detection and fixed-size fallback
- `ingestion-background-service`: 24-hour cycle background service that processes new and failed books

### Modified Capabilities

## Impact

- `Features/Ingestion/` — new home for all ingestion code
- `Infrastructure/Sqlite/` — `IngestionDbContext`, EF Core migrations
- `Program.cs` — registers `IngestionBackgroundService`, `IngestionDbContext`, admin endpoints
- `DndMcpAICsharpFun.csproj` — adds `Microsoft.EntityFrameworkCore.Sqlite`
- New admin endpoints under `/admin/books/*` (protected by existing `AdminApiKeyMiddleware`)
- Depends on: `foundation` change (Qdrant client, Ollama client, admin middleware all in place)
