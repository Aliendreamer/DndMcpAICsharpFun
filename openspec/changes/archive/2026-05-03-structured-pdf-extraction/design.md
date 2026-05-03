## Context

The ingestion pipeline extracts D&D entities from PDFs using PdfPig for text extraction and a local Ollama LLM (llama3.2) for entity classification. The current `PdfPigTextExtractor` groups words by Y-coordinate, which interleaves multi-column layouts. D&D sourcebooks (PHB, etc.) are consistently two-column with section headings — the LLM receives garbled text and has no heading hierarchy, causing misclassifications (e.g., Bear Totem as a Monster). Embeddings use `nomic-embed-text` at 768 dims stored in Qdrant.

## Goals / Non-Goals

**Goals:**
- Fix multi-column text ordering using `DocstrumBoundingBoxes` + `UnsupervisedReadingOrderDetector`
- Give the LLM heading context via `[H2]/[H3]/body` formatted prompt input
- Store structured blocks alongside entities in per-page JSON
- Add `PageEnd` tracking to `ChunkMetadata` and Qdrant payload
- Upgrade embedding model to `mxbai-embed-large` (1024 dims)
- Add synchronous single-page extraction endpoint for fast iteration

**Non-Goals:**
- Backwards compatibility with existing extracted JSON files (volumes are wiped)
- Per-block embeddings (entity descriptions remain the embedding unit)
- OCR or scanned PDF support
- Changing the retrieval API contract

## Decisions

### D1 — New interface `IPdfStructuredExtractor` rather than modifying `IPdfTextExtractor`

`IPdfTextExtractor` returns `IEnumerable<(int PageNumber, string Text)>`. The new extractor needs to return structured blocks. Modifying the interface would break every consumer. A new interface is cleaner and allows the old one to be deleted entirely rather than left as dead code.

`IPdfStructuredExtractor` returns `IEnumerable<StructuredPage>` where `StructuredPage` is a record:
```
StructuredPage(int PageNumber, string RawText, IReadOnlyList<PageBlock> Blocks)
PageBlock(int Order, string Level, string Text)  // Level: "h1"|"h2"|"h3"|"body"
```

### D2 — Heading level inferred from font size, not PDF tag structure

PDF heading tags are unreliable in practice (most D&D PDFs are not tagged). Font size within each `DocstrumBoundingBoxes` block is computed as the median letter font size. Blocks are ranked by font size across the page: largest → `h1`, next distinct size → `h2`, next → `h3`, remainder → `body`. Ties at body size stay `body`. This is a heuristic — it works well for consistently styled books like the PHB.

### D3 — Enriched page JSON wraps entities rather than separate artifact directory

Keeping blocks and entities in the same `extracted/<bookId>/page_<n>.json` file means one read gives full context for both re-extraction and ingestion. A separate `pages/` directory would require two reads and two write paths. Since old files are not supported, there is no migration cost.

### D4 — `PageEnd` tracked in merge pass, capped by TOC chapter boundary

The merge pass already iterates partial entity chains across pages. As it follows the chain it records the last page seen. The TOC `TocCategoryMap` already maps page ranges to chapters — if the chain runs past a chapter boundary (indicating a merge error), the chapter end page acts as a hard cap. This avoids `PageEnd` values that span unrelated chapters.

### D5 — `mxbai-embed-large` over `e5-large`

Both are 335M / 1024 dims and fit easily in 8 GB VRAM alongside llama3.2 (they never run simultaneously). `mxbai-embed-large` consistently outperforms `e5-large` on MTEB benchmarks and is available directly via `ollama pull`. Config-only change — no code differences between models.

### D6 — Single-page endpoint is synchronous and does not persist by default

The purpose is fast feedback during development. Persisting by default would corrupt partially-extracted book state. A `?save=true` query parameter allows optional persistence for surgical re-extraction of a single page without re-running the full book.

## Risks / Trade-offs

- [Heading heuristic breaks on non-standard PDFs] → Font-size ranking works for PHB-style books. For books with inconsistent typography, blocks may be mis-levelled. Mitigation: single-page endpoint lets you inspect results before committing to a full run.
- [DocstrumBoundingBoxes is slower than simple word grouping] → Benchmarked at ~50–200ms per page on CPU; acceptable since extraction is a one-time background job. Mitigation: none needed.
- [Qdrant collection must be recreated on VectorSize change] → `QdrantCollectionInitializer` already handles creation on startup. Mitigation: document in `docker-compose.yml` that `qdrant_data` volume must be wiped when `VectorSize` changes.
- [Merge pass PageEnd tracking adds complexity] → The pass is already O(n²) over partial entities per page pair. Adding a last-seen-page counter is O(1) per chain. Mitigation: covered by existing merge pass tests.

## Migration Plan

1. Wipe `qdrant_data` and `books_data` Docker volumes
2. Pull new embedding model: `docker exec ollama ollama pull mxbai-embed-large`
3. Deploy updated service — `QdrantCollectionInitializer` recreates collection at 1024 dims on startup
4. Re-register and re-extract all books via admin API
5. Re-ingest JSON to populate Qdrant

Rollback: restore previous image and volumes from backup. No schema migrations required (SQLite `IngestionRecord` is unchanged).

## Open Questions

- None — all decisions resolved during design review.
