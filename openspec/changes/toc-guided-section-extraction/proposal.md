## Why

Extraction times out on dense pages (Class, Race) because the LLM receives an entire page as one prompt and attempts to produce one giant JSON object. The PHB's table of contents already tells us exactly which entity lives on which page range — we should use it to drive extraction with precise context rather than asking the model to discover structure from raw text.

## What Changes

- **BREAKING**: `POST /admin/books/register` now requires `tocPage` (int) — missing field returns 400
- **BREAKING**: `POST /admin/books/register-path` endpoint removed
- `IngestionRecord` gains `TocPage` field
- `ITocCategoryClassifier` replaced by new `ITocMapExtractor` — reads TOC page text, returns full `{title, category, startPage, endPage}[]` map via LLM
- `TocCategoryMap` enhanced with `title` and `endPage` per entry
- Extraction changes from one LLM call per page to one LLM call per block-group (heading + following body blocks), with entity name/range as context hint
- New `Trait` and `Lore` values added to `ContentCategory` enum
- Qdrant chunks gain `section_title`, `section_start`, `section_end` payload fields

## Capabilities

### New Capabilities
- `toc-map-extraction`: LLM parses a single TOC page and returns a structured map of all sections with title, category, and page range
- `section-level-extraction`: Per-page blocks grouped by heading; each group extracted as a focused LLM call with entity context hint

### Modified Capabilities
- `ingestion-pipeline`: Book registration requires `tocPage`; extraction loop changed to section-level grouping
- `llm-extraction`: Extraction prompt enriched with entity name, category, and page range context; new Trait/Lore categories supported
- `embedding-vector-store`: Chunks carry `section_title`, `section_start`, `section_end` Qdrant payload fields
- `dm-content-categories`: `Trait` and `Lore` added to `ContentCategory` enum

## Impact

- `IngestionRecord` (SQLite schema change — migration required)
- `BooksAdminEndpoints.cs` — register endpoint signature change, register-path removed
- `OllamaTocCategoryClassifier.cs` — replaced by `OllamaTocMapExtractor.cs`
- `TocCategoryMap.cs` — extended with title + endPage
- `IngestionOrchestrator.cs` — extraction loop refactored to section grouping
- `OllamaLlmEntityExtractor.cs` — prompt updated with context hint
- `QdrantPayloadFields.cs`, `QdrantCollectionInitializer.cs`, `QdrantVectorStoreService.cs` — new fields
- `ContentCategory.cs` — two new enum values
- `DndMcpAICsharpFun.http` — register example updated, register-path example removed
