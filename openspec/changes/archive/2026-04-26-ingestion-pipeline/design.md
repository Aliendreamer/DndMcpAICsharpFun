## Context

The foundation change provides DI-registered Qdrant, Ollama, and PdfPig clients plus admin key middleware. This change builds the pipeline that goes from a registered PDF book to chunks stored in Qdrant, coordinated by a background service and tracked in SQLite for idempotency.

Key constraints:
- PDF books are mounted in a Docker volume; the app cannot enumerate files freely — books must be explicitly registered via the admin endpoint
- The same book file may be re-added with a different name; SHA256 hash is the canonical identity
- Ingestion can fail partway through (e.g. Ollama down); the tracker must support clean retry
- The 2014 and 2024 editions of the same book (e.g. PHB) have different formatting; the detector must work for both

## Goals / Non-Goals

**Goals:**
- Admin endpoint to register books with rich metadata (source, version, display name)
- SQLite tracker that prevents re-ingesting an already-completed file
- Background service that retries failed ingestions automatically every 24h
- PDF text extraction with best-effort two-column reconstruction via PdfPig
- Chunking that respects D&D entity boundaries (spell entries, stat blocks, background entries)
- Category detection (spell, monster, class, background, item, rule) per chunk
- Entity name extraction per chunk

**Non-Goals:**
- Embedding (handled in `embedding-vector-store` change)
- Storing chunks in Qdrant (handled in `embedding-vector-store` change)
- OCR for scanned PDFs
- Parsing tables into structured data (class progression tables stored as raw text chunks)

## Decisions

### D1 — SQLite via EF Core for ingestion tracking

`IngestionRecord` entity tracked by `IngestionDbContext` (EF Core + SQLite provider). Migrations generated and applied on startup.

Alternatives considered:
- Dapper + raw SQL: less ceremony for a simple table but more boilerplate for migrations
- In-memory: doesn't survive restarts

Rationale: EF Core is already a familiar pattern in .NET; the model is trivially simple (one table) so the overhead is negligible, and migrations-on-startup ensures the schema is always current.

### D2 — Book registration as explicit admin action (Option C)

Books are not auto-discovered from the volume. An operator must call `POST /admin/books/register` with `{ filePath, sourceName, version, displayName }`.

Rationale: D&D books have ambiguous file names ("phb.pdf", "book1.pdf"). Metadata (PHB, 2024) cannot be reliably inferred from arbitrary filenames. Explicit registration ensures every chunk has accurate version and source metadata from day one, which is critical for filtered retrieval.

### D3 — SHA256 hash as idempotency key

On each background cycle, the service computes SHA256 of the file bytes. If a record with that hash and `status = Completed` exists, the file is skipped regardless of name or path changes.

Rationale: Protects against re-ingestion when a file is moved or renamed. Also handles the case where two differently-named files are identical (deduplicated).

### D4 — Entity-boundary chunking with fixed-size fallback

```
ChunkingStrategy:
  1. Scan text for entity-start patterns (spell header, stat block header, background header)
  2. If pattern found → start new chunk at that boundary
  3. If resulting chunk > MaxTokens → sub-split with OverlapTokens overlap
  4. If no pattern found in a window → fixed-size split at sentence boundary
```

MaxTokens default: 512 tokens (configurable via `IngestionOptions`)
OverlapTokens default: 64 tokens

Rationale: Entity-boundary chunks produce far better RAG results than fixed-size. A complete spell entry or stat block as one chunk means retrieval returns a usable, self-contained answer. Fixed-size fallback handles narrative/rules text that has no structured entity format.

### D5 — Category detection via layered pattern matching

```
Layer 1: Chapter context tracker
  Detects chapter headings from PDF structure → sets default category for subsequent chunks
  e.g. "Chapter 11: Spells" → default = spell

Layer 2: Pattern detectors (per chunk)
  SpellDetector:      "Casting Time:" AND "Range:" AND "Components:" AND "Duration:"
  MonsterDetector:    "Armor Class:" AND "Hit Points:" AND "Speed:"
  BackgroundDetector: "Skill Proficiencies:" AND "Feature:"
  ClassDetector:      "Hit Dice:" AND "Proficiencies:" AND level table markers
  ItemDetector:       "Weapon" OR "Armor" category line + "Properties:"

Layer 3: Fallback
  category = "rule"
```

Each detector returns a `float confidence`. Highest confidence above threshold (0.7) wins. Below threshold → chapter context or fallback.

### D6 — Background service period: 24h with immediate first run

`IngestionBackgroundService` extends `BackgroundService`. On `ExecuteAsync`:
1. Run one ingestion pass immediately on startup
2. Then wait 24 hours, repeat

This ensures newly registered books are processed without waiting a full cycle.

### D7 — Ingestion orchestrator is separate from background service

`IngestionOrchestrator` handles the actual pipeline logic (scan registered books → hash → check tracker → extract → chunk → classify → hand off to embedding). The background service only calls the orchestrator on a timer.

This allows `POST /admin/books/{id}/reingest` to call the orchestrator directly, and makes the orchestrator independently testable.

## Risks / Trade-offs

- **Two-column PDF layout** → PdfPig's `PageTextLayerOptions` with `TopToBottom` ordering partially handles this; may produce garbled text for complex layouts. Mitigation: log extraction warnings; operator can inspect chunk quality via admin endpoint.
- **2014 vs 2024 formatting differences** → Both editions use similar structured entry formats for spells and monsters. Class feature formatting changed more significantly in 2024. Mitigation: `version` field available at detection time; detectors can have version-specific branches.
- **Large PDFs (600+ pages)** → Ingestion of one book may take several minutes with Ollama embedding in the loop. Mitigation: embedding is a separate change; this change only produces chunks and hands them off. Track `chunk_count` in SQLite for observability.
- **EF Core migrations on startup** → `Database.MigrateAsync()` on startup is fine for single-instance Docker deployment; would be a concern for multi-instance. Not a concern here.

## Migration Plan

1. Add `Microsoft.EntityFrameworkCore.Sqlite` NuGet package
2. Create `IngestionRecord` entity and `IngestionDbContext`
3. Generate initial EF Core migration
4. Implement `PdfTextExtractor`, `DndChunker`, `ContentCategoryDetector`, `EntityNameExtractor`
5. Implement `IngestionOrchestrator` (extract → chunk → classify; embedding call stubbed/interface only)
6. Implement `IngestionBackgroundService`
7. Add admin endpoints and wire into `Program.cs`
8. Verify: register a book, trigger ingestion, check SQLite status, inspect logged chunks
